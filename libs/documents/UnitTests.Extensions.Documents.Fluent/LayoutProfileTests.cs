using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fluent;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// The document's layout profile (<see cref="LayoutProfile"/>) from the code front-end: the defaults, the document
/// modifiers, how the document model's text form shows a profile, and the check of a profile set by hand.
/// </summary>
public class LayoutProfileTests
{
    private static Document Doc() => Document([Paragraph("text")], title: "Title", language: "en").FontFamily("Lato");

    [Fact]
    public void A_document_has_the_default_profile_and_its_text_form_does_not_mention_it()
    {
        var model = DocumentTranslator.ToModel(Doc()).Value;

        model.Profile.Should().Be(LayoutProfile.Default);
        model.Profile.MonolithicOverflow.Should().Be(MonolithicOverflow.Refuse);
        model.Profile.EmptyRowParts.Should().Be(EmptyRowParts.Avoid);
        model.Profile.FontMetricRounding.Should().Be(FontMetricRounding.Exact);
        model.Profile.FontSizeRounding.Should().Be(FontSizeRounding.Exact);
        model.Profile.LengthRounding.Should().Be(LengthRounding.Exact);
        DocumentModelText.Write(model).Should().NotContain("profile");
    }

    [Fact]
    public void The_modifiers_set_the_policies_and_the_text_form_shows_them()
    {
        var model = DocumentTranslator.ToModel(Doc().MonolithicOverflow(MonolithicOverflow.Slice).EmptyRowParts(EmptyRowParts.Allow)
            .FontMetricRounding(FontMetricRounding.WholePixels)).Value;

        model.Profile.Should().Be(new LayoutProfile
        {
            MonolithicOverflow = MonolithicOverflow.Slice,
            EmptyRowParts = EmptyRowParts.Allow,
            FontMetricRounding = FontMetricRounding.WholePixels,
        });
        DocumentModelText.Write(model).Split('\n')[2].Should().Be("profile monolithic-overflow=Slice empty-row-parts=Allow font-metric-rounding=WholePixels");
    }

    [Fact]
    public void The_font_size_rounding_modifier_sets_its_policy()
    {
        var model = DocumentTranslator.ToModel(Doc().FontSizeRounding(FontSizeRounding.FlooredToPixelFractions)).Value;

        model.Profile.Should().Be(new LayoutProfile { FontSizeRounding = FontSizeRounding.FlooredToPixelFractions });
    }

    [Fact]
    public void The_length_rounding_modifier_sets_its_policy()
    {
        var model = DocumentTranslator.ToModel(Doc().LengthRounding(LengthRounding.SixtyFourthPixels)).Value;

        model.Profile.Should().Be(new LayoutProfile { LengthRounding = LengthRounding.SixtyFourthPixels });
    }

    [Fact]
    public void The_cells_beside_slices_modifier_sets_its_policy_and_a_value_it_does_not_name_is_refused()
    {
        var model = DocumentTranslator.ToModel(Doc().CellsBesideSlices(CellsBesideSlices.Below)).Value;
        var undefined = DocumentTranslator.ToModel(Doc() with { Profile = new LayoutProfile { CellsBesideSlices = (CellsBesideSlices)4 } });

        LayoutProfile.Default.CellsBesideSlices.Should().Be(CellsBesideSlices.GoOn);
        model.Profile.Should().Be(new LayoutProfile { CellsBesideSlices = CellsBesideSlices.Below });
        undefined.Error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.LayoutProfile);
    }

    [Fact]
    public void A_profile_that_is_missing_or_names_no_policy_value_is_refused_with_every_problem()
    {
        var missing = DocumentTranslator.ToModel(Doc() with { Profile = null! });
        var undefined = DocumentTranslator.ToModel(Doc() with { Profile = new LayoutProfile { MonolithicOverflow = (MonolithicOverflow)7, EmptyRowParts = (EmptyRowParts)9, FontMetricRounding = (FontMetricRounding)5, FontSizeRounding = (FontSizeRounding)6, LengthRounding = (LengthRounding)3 } });

        missing.Error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.LayoutProfile);
        undefined.Error.Parts().Select(error => error.Code).Should().Equal(Enumerable.Repeat(DocumentErrorCodes.Input.Invalid.LayoutProfile, 5));
    }
}
