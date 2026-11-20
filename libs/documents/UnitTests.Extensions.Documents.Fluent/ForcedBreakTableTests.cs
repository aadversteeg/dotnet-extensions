using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Functional;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// A table after a forced page break, with only its margin above it on the page: there is no breakpoint before it, so a
/// break before it leaves only the margin on the page, a last resort as much as a cut inside its first row. Chromium
/// takes the cut: the first row's top line and padding stay on the page, its content goes on on the next
/// (<c>tools/reference/chromium</c>, cases <c>fr-forced-table</c>, <c>fr-forced-table-in-box</c>); a header row, which
/// it does not cut, moves the whole table (<c>fr-forced-table-header</c>); a first row that fits is placed there
/// (<c>fr-forced-table-row-fits</c>). Page area 276 × 150 pt, Lato 10.5 pt, line height 12 pt, cell padding 2 pt and
/// 4 pt, 0.75 pt grid lines split at breaks.
/// </summary>
public class ForcedBreakTableTests
{
    private static CellElement Padded(string text) => Cell(text).PaddingTop(2.Pt()).PaddingBottom(2.Pt()).PaddingLeft(4.Pt()).PaddingRight(4.Pt());

    private static TableElement Grid(params TablePart[] rows) => Table(Columns(Fill(1)), [.. rows]).GridLines(0.75.Pt(), Color.Black).SplitLinesAtBreaks();

    /// <summary>The body of each case, as its HTML file builds it.</summary>
    private static Element[] Body(string caseName) => caseName switch
    {
        "fr-forced-table-in-box" => [Paragraph("i"), PageBreak(), Block([Grid(TableRow([Padded("w")]))]).MarginTop(140.Pt()), Paragraph("z")],
        "fr-forced-table-header" => [Paragraph("i"), PageBreak(), Grid(HeaderRow([Padded("h")]), TableRow([Padded("w")])).MarginTop(140.Pt()), Paragraph("z")],
        "fr-forced-table-row-fits" => [Paragraph("i"), PageBreak(), Grid(TableRow([Padded("w")]), TableRow([Padded("x")])).MarginTop(128.Pt()), Paragraph("z")],
        _ => throw new ArgumentOutOfRangeException(nameof(caseName)),
    };

    private static PrintedPages Printed(Element[] body, bool asChromium, double fontSize = 10.5, double? lineHeight = 12)
    {
        var document = Document([.. body], Page(300.Pt(), 174.Pt()).Margin(12.Pt()), title: "Title", language: "en")
            .FontFamily("Lato").FontSize(fontSize.Pt()).FontMetricRounding(FontMetricRounding.WholePixels);
        document = lineHeight is { } points ? document.LineHeight(points.Pt()) : document;
        document = asChromium ? document.MonolithicOverflow(MonolithicOverflow.Slice).EmptyRowParts(EmptyRowParts.Allow) : document;
        var result = DocumentTranslator.ToModel(document).OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic)).OnSuccessMap(PrintedPages.Engine);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    /// <summary>Where the page's highest rectangle starts, or null when it has none.</summary>
    private static double? TopLine(PrintedPage page) => page.Rectangles.IsEmpty ? null : page.Rectangles.Min(box => box.Y0);

    private static void ShouldBeAsChromium(PrintedPages engine, PrintedPages chromium)
    {
        var words = engine.Pages.SelectMany(page => page.Words).ToList();
        var measured = chromium.Pages.SelectMany(page => page.Words).ToList();
        words.Select(word => (word.Page, word.Text)).Should().Equal(measured.Select(word => (word.Page, word.Text)));
        words.Zip(measured, (word, other) => Math.Abs(word.Baseline - other.Baseline)).Should().OnlyContain(apart => apart <= 0.375, "every baseline in the same whole pixel");
        engine.Pages.Select(TopLine).Zip(chromium.Pages.Select(TopLine), (mine, theirs) => mine is null == theirs is null && (mine is null || Math.Abs(mine.Value - theirs!.Value) <= 0.375))
            .Should().OnlyContain(same => same, "each page's highest line where Chromium draws it");
    }

    [Theory]
    [InlineData("fr-forced-table-in-box", true)]
    [InlineData("fr-forced-table-in-box", false)]
    [InlineData("fr-forced-table-header", true)]
    [InlineData("fr-forced-table-header", false)]
    [InlineData("fr-forced-table-row-fits", true)]
    [InlineData("fr-forced-table-row-fits", false)]
    public void A_table_after_a_forced_break_prints_as_in_Chromium(string caseName, bool asChromium)
    {
        // in-box: the top line at 152.25 on page 2, w on page 3. header: page 2 blank, the whole table on page 3, under
        // both profiles. row-fits: the first row on page 2, x on page 3.
        ShouldBeAsChromium(Printed(Body(caseName), asChromium), PrintedPages.Chromium(caseName));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_table_with_only_its_margin_above_it_keeps_its_first_row_s_top_line_on_the_page(bool asChromium)
    {
        // fr-forced-table: Lato 12 pt, line height normal, the cell padding of the case's style sheet. Both profiles.
        var body = new Element[] { Paragraph("i"), PageBreak(), Grid(TableRow([Padded("w")])).MarginTop(140.Pt()), Paragraph("z") };
        var engine = Printed(body, asChromium, fontSize: 12, lineHeight: null);
        var chromium = PrintedPages.Chromium("fr-forced-table");
        TopLine(chromium.Pages[1]).Should().Be(152.25);
        ShouldBeAsChromium(engine, chromium);
    }
}
