using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Functional;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// Both values of <see cref="BodyClip"/>: under <see cref="BodyClip.BodyArea"/> each page's body is drawn under a clip to
/// its area, the page area less the header and footer areas, as Chromium prints it; under <see cref="BodyClip.None"/>,
/// the default, nothing is clipped.
/// </summary>
public class BodyClipTests
{
    private static PageDocument Run(Element[] body, BodyClip clip, PageSetup? page = null)
    {
        var result = DocumentTranslator.ToModel(Document([.. body], page ?? Page(300.Pt(), 204.Pt()).Margin(12.Pt()), title: "Title", language: "en")
                .FontFamily("Lato").FontSize(12.Pt()).LineHeight(15.Pt())
                .EmptyRowParts(EmptyRowParts.Allow).MonolithicOverflow(MonolithicOverflow.Slice).FontMetricRounding(FontMetricRounding.WholePixels)
                .SplitLineHalves(SplitLineHalves.AtLeastOnePixel).TableEndOverflow(TableEndOverflow.ContinuesOnNextPage).BodyClip(clip))
            .OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic));
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    [Fact]
    public void The_clip_is_the_body_s_area_and_the_header_and_footer_areas_are_outside_it()
    {
        var page = Page(300.Pt(), 204.Pt()).Margin(12.Pt()).Header(Paragraph("head"), 30.Pt()).Footer(Paragraph("foot"), 24.Pt());
        var items = Run([Paragraph("body")], BodyClip.BodyArea, page).Pages[0].Items;

        var start = items.Select((item, index) => (item, index)).Single(pair => pair.item is ClipStart);
        ((ClipStart)start.item).Area.Should().Be(new Rect(new Point(12, 42), new Size(276, 126)), "the page area 276 × 180 less 30 above and 24 below");
        var end = items.Select((item, index) => (item, index)).Single(pair => pair.item is ClipEnd).index;
        items.Take(start.index).OfType<ArtifactStart>().Should().ContainSingle(artifact => artifact.Kind == ArtifactKind.PageHeader);
        items.Skip(end).OfType<ArtifactStart>().Should().ContainSingle(artifact => artifact.Kind == ArtifactKind.PageFooter);
        items.Skip(start.index).Take(end - start.index).OfType<GlyphRun>().Should().ContainSingle();
        Run([Paragraph("body")], BodyClip.None, page).Pages[0].Items.Should().NotContain(item => item is ClipStart);
    }

    [Fact]
    public void The_clip_is_smaller_by_the_page_s_body_clip_inset_at_its_right_and_bottom()
    {
        // As the HTML front-end sets it for 10 pt margins on a 300 pt page: layout's area 280.5 pt wide from 9.75,
        // Chromium's clip 280 pt (case mf4-line-0).
        var model = DocumentTranslator.ToModel(Document([Paragraph("body")], Page(300.Pt(), 204.Pt()).Margin(12.Pt()), title: "Title", language: "en")
            .FontFamily("Lato").BodyClip(BodyClip.BodyArea)).Value;
        var inset = model with { Page = model.Page with { BodyClipInset = new Size(0.5, 0.25) } };

        var items = DocumentLayout.Run(inset, TestFonts.SetWithItalic).Value.Pages[0].Items;

        items.OfType<ClipStart>().Single().Area.Should().Be(new Rect(new Point(12, 12), new Size(275.5, 179.75)));
    }

    [Theory]
    [InlineData(-0.5, 0)]
    [InlineData(0, double.NaN)]
    [InlineData(276.5, 0)]
    public void A_body_clip_inset_below_0_or_beyond_the_page_area_is_refused(double width, double height)
    {
        var model = DocumentTranslator.ToModel(Document([Paragraph("body")], Page(300.Pt(), 204.Pt()).Margin(12.Pt()), title: "Title", language: "en")
            .FontFamily("Lato")).Value;

        var result = DocumentLayout.Run(model with { Page = model.Page with { BodyClipInset = new Size(width, height) } }, TestFonts.SetWithItalic);

        result.Error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.Length);
        result.Error.Message.Should().StartWith("The body clip's inset must be 0 or more");
    }

    [Fact]
    public void The_code_front_end_s_default_clips_nothing_and_the_modifier_sets_the_other_value()
    {
        var document = Document([Paragraph("a")], title: "Title", language: "en").FontFamily("Lato");

        LayoutProfile.Default.BodyClip.Should().Be(BodyClip.None);
        DocumentTranslator.ToModel(document).Value.Profile.BodyClip.Should().Be(BodyClip.None);
        DocumentTranslator.ToModel(document.BodyClip(BodyClip.BodyArea)).Value.Profile
            .Should().Be(LayoutProfile.Default with { BodyClip = BodyClip.BodyArea });
    }

    [Fact]
    public void A_profile_that_names_no_body_clip_value_is_refused()
    {
        var result = DocumentTranslator.ToModel(Document([Paragraph("a")], title: "Title", language: "en").FontFamily("Lato").BodyClip((BodyClip)7));

        result.Error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.LayoutProfile);
        result.Error.Message.Should().StartWith("The body clip policy must be None or BodyArea.");
    }
}
