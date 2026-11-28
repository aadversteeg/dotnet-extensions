using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Functional;
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
    public void The_cut_row_alignment_modifier_sets_its_policy_and_a_value_it_does_not_name_is_refused()
    {
        var model = DocumentTranslator.ToModel(Doc().CutRowAlignment(CutRowAlignment.TopOnEveryPage)).Value;
        var undefined = DocumentTranslator.ToModel(Doc() with { Profile = new LayoutProfile { CutRowAlignment = (CutRowAlignment)4 } });

        LayoutProfile.Default.CutRowAlignment.Should().Be(CutRowAlignment.RestAlignedInItsPart);
        model.Profile.Should().Be(new LayoutProfile { CutRowAlignment = CutRowAlignment.TopOnEveryPage });
        undefined.Error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.LayoutProfile);
    }

    [Fact]
    public void The_slice_rest_end_modifier_sets_its_policy_and_a_value_it_does_not_name_is_refused()
    {
        var model = DocumentTranslator.ToModel(Doc().SliceRestEnd(SliceRestEnd.AtLineCentre)).Value;
        var undefined = DocumentTranslator.ToModel(Doc() with { Profile = new LayoutProfile { SliceRestEnd = (SliceRestEnd)4 } });

        LayoutProfile.Default.SliceRestEnd.Should().Be(SliceRestEnd.InsideCell);
        model.Profile.Should().Be(new LayoutProfile { SliceRestEnd = SliceRestEnd.AtLineCentre });
        undefined.Error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.LayoutProfile);
    }

    [Fact]
    public void The_repeated_header_rows_modifier_sets_its_policy_and_a_value_it_does_not_name_is_refused()
    {
        var model = DocumentTranslator.ToModel(Doc().RepeatedHeaderRows(RepeatedHeaderRows.WhereContentIsLaidOut)).Value;
        var undefined = DocumentTranslator.ToModel(Doc() with { Profile = new LayoutProfile { RepeatedHeaderRows = (RepeatedHeaderRows)4 } });

        LayoutProfile.Default.RepeatedHeaderRows.Should().Be(RepeatedHeaderRows.AboveEverySlice);
        model.Profile.Should().Be(new LayoutProfile { RepeatedHeaderRows = RepeatedHeaderRows.WhereContentIsLaidOut });
        undefined.Error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.LayoutProfile);
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
    public void A_document_built_in_code_breaks_its_lines_by_the_standard_rules()
    {
        var model = DocumentTranslator.ToModel(Doc()).Value;

        LayoutProfile.Default.LineBreakRules.Should().Be(LineBreakRules.Standard);
        new LayoutProfile().LineBreakRules.Should().Be(LineBreakRules.Standard);
        model.Profile.LineBreakRules.Should().Be(LineBreakRules.Standard);
    }

    [Fact]
    public void A_text_that_the_two_line_break_rules_break_otherwise_takes_the_standard_lines_in_a_document_built_in_code()
    {
        // UAX #14 breaks after the slash before a letter and keeps the hyphens of a date with its digits (LB25); Chromium's
        // rules keep "and/or" whole and break after each hyphen of the date (matrix rows and/or, 2026-11-28).
        var document = Document([Paragraph("and/or 2026-11-28").PaddingRight(270.Pt())], Page(300.Pt(), 200.Pt()).Margin(12.Pt()), title: "Title", language: "en")
            .FontFamily("Lato").FontSize(12.Pt()).LineHeight(18.Pt());
        var model = DocumentTranslator.ToModel(document).Value;

        Lines(model).Should().Equal("and/", "or", "2026-11-28");
        Lines(model with { Profile = model.Profile with { LineBreakRules = LineBreakRules.Chromium } }).Should().Equal("and/or", "2026-", "11-", "28");
    }

    /// <summary>The texts of the lines of the first page of the model's layout.</summary>
    private static List<string> Lines(StyledDocument model)
    {
        var printed = DocumentLayout.Run(model, new FontSet([TestFonts.Regular])).OnSuccessMap(PrintedPages.Engine);
        printed.IsSuccess.Should().BeTrue(printed.IsFailure ? printed.Error.ToString() : string.Empty);
        return [.. printed.Value.Pages[0].Lines.Select(line => line.Text)];
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
