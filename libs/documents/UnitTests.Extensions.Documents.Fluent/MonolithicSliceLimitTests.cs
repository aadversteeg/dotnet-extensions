using System.IO;
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
/// The limit on slicing (<see cref="MonolithicOverflow.Slice"/>): one piece of content — an image, a line box — is cut
/// into at most 100 slices, one per page, so the number of pages follows from the size of the input and not from the
/// numbers in it. A piece that would need more is refused with <c>Documents.Layout.TooManySlices</c>, a clean error.
/// The 100 slices show the longest image the front-end accepts (14 400 pt) whole on a page area of 144 pt.
/// </summary>
public class MonolithicSliceLimitTests
{
    private static byte[] Tall => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "chromium", "tall.png"));

    /// <summary>A page 300 × 204 pt whose page area is <paramref name="area"/> pt high (top margin 12 pt, the rest at the
    /// bottom).</summary>
    private static PageSetup Area(double area) => Page(300.Pt(), 204.Pt()).Margins(new Edges(12.Pt(), 12.Pt(), (192 - area).Pt(), 12.Pt()));

    private static Result<PageDocument, global::Ave.Extensions.ErrorPaths.Error> Run(PageSetup page, params Element[] body) =>
        DocumentTranslator.ToModel(Document([.. body], page, title: "Title", language: "en").FontFamily("Lato").MonolithicOverflow(MonolithicOverflow.Slice))
            .OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic));

    private static ImageElement Image14400() => Image(Tall).Alt("tall").Height(14_400.Pt());

    [Fact]
    public void The_longest_image_on_a_page_area_of_144_pt_takes_the_100_slices_the_limit_allows()
    {
        var result = Run(Area(144), Image14400(), Paragraph("after"));

        // 100 slices of 144 pt fill pages 1 to 100 exactly; "after" starts page 101.
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        result.Value.Pages.Select(page => page.Items.OfType<ClipStart>().Count()).Should().Equal([.. Enumerable.Repeat(1, 100), 0]);
    }

    [Fact]
    public void An_image_that_needs_101_slices_is_refused()
    {
        var result = Run(Area(143.9), Image14400(), Paragraph("after"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(LayoutErrorCodes.TooManySlices);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.1)]
    [InlineData(0.01)]
    [InlineData(0.002)]
    public void An_image_on_a_page_area_far_smaller_than_its_height_is_refused_after_at_most_100_pages_of_work(double area)
    {
        // Without the limit these give 14 400 to 7 200 000 pages (the last ran out of memory).
        var result = Run(Area(area), Image14400());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(LayoutErrorCodes.TooManySlices);
    }

    [Theory]
    [InlineData(12.0, 14_400.0)]
    [InlineData(14_400.0, 100.0)]
    [InlineData(14_400.0, 14_400.0)]
    public void A_line_taller_than_100_page_areas_is_refused(double fontSize, double lineHeight)
    {
        // A line height given as a number may be a large multiple of the font size, so one line on A4 can be taller than
        // any page; without the limit these give 212, 1 761 and 253 531 pages.
        var result = Run(PageSetup.A4.Margin(12.Pt()), Paragraph("W").FontSize(fontSize.Pt()).LineHeight(lineHeight));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(LayoutErrorCodes.TooManySlices);
    }

    [Fact]
    public void A_line_that_takes_the_100_slices_the_limit_allows_is_laid_out()
    {
        // 12 pt × 1 200 is a line of 14 400 pt: 100 slices of 144 pt.
        var result = Run(Area(144), Paragraph("W").FontSize(12.Pt()).LineHeight(1_200));

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        result.Value.Pages.Select(page => page.Items.OfType<ClipStart>().Count()).Should().Equal(Enumerable.Repeat(1, 100));
    }

    /// <summary>Nine images of 100 slices, one of <paramref name="slices"/> slices and one of 2 (200 pt) on a page area of
    /// 144 pt: 902 + <paramref name="slices"/> slices in all, none of them past the limit for one piece.</summary>
    private static Element[] ElevenImages(int slices) =>
        [.. Enumerable.Range(0, 9).Select(_ => (Element)Image14400()), Image(Tall).Alt("tall").Height((slices * 144.0).Pt()), Image(Tall).Alt("tall").Height(200.Pt()), Paragraph("after")];

    [Fact]
    public void A_document_whose_pieces_take_1000_slices_together_is_laid_out()
    {
        // 9 × 100 + 98 + 2: each tall image starts a page, the last slice of each but the 200 pt one fills its page.
        var result = Run(Area(144), ElevenImages(98));

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        result.Value.Pages.Sum(page => page.Items.OfType<ClipStart>().Count()).Should().Be(1_000);
        result.Value.Pages.Should().HaveCount(1_000);
    }

    [Fact]
    public void A_document_whose_pieces_would_take_1001_slices_together_is_refused_and_the_message_names_the_limit_for_one_document()
    {
        // 9 × 100 + 99 + 2: every piece within the limit for one piece, the document one slice past its own.
        var result = Run(Area(144), ElevenImages(99));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(LayoutErrorCodes.TooManySlices);
        result.Error.Message.Should().Contain("1000 slices").And.Contain("the limit for one document");
    }

    [Fact]
    public void The_refusal_of_a_piece_names_the_limit_for_one_piece()
    {
        var result = Run(Area(143.9), Image14400());

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("100 slices").And.Contain("the limit for one piece");
    }

    [Fact]
    public void A_document_without_a_piece_taller_than_the_page_area_never_meets_the_limit_for_one_document()
    {
        // 1 200 paragraphs of one line, 12 lines a page: 100 pages under Slice, no slice among them, as under Refuse.
        var body = Enumerable.Range(0, 1_200).Select(i => (Element)Paragraph($"p{i}").LineHeight(12.Pt())).ToArray();
        var sliced = Run(Area(144), body);

        sliced.IsSuccess.Should().BeTrue(sliced.IsFailure ? sliced.Error.ToString() : string.Empty);
        sliced.Value.Pages.Should().HaveCount(100);
        sliced.Value.Pages.SelectMany(page => page.Items.OfType<ClipStart>()).Should().BeEmpty();
    }
}
