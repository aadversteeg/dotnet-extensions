using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// The tracks algorithm against Chromium's own layout of the same tables. The HTML cases, the script that measures them
/// and the definition of the reference are in <c>tools/reference/chromium</c> (README there): Chromium 159 in the pinned
/// Playwright container, font hinting off. All cells: Lato, <c>border-collapse: collapse; border: 0.75pt solid; padding:
/// 2pt 4pt; vertical-align: top</c>. The border is 0.75pt because Chromium draws a 1pt border as one device pixel (0.75pt
/// at 96 dpi) and lays out with that width.
/// Widths are compared with <see cref="ChromiumLayout"/>: within n layout units (1/64 px), n counted from the structure of
/// the case next to each check. Chromium truncates specified widths and paddings, ceils each text item and truncates the
/// shares of a distribution (README there, "The comparison rule"); a 0.75pt border is exactly 1px and adds nothing. The
/// width cases are at 10.5pt (14px), a size whose text Chromium lays out with the font's exact advances; the 10pt case
/// keeps the one place where Chromium is not exact under test. Page breaks are compared exactly: which rows on which page.
/// </summary>
public class ChromiumReferenceTests
{
    /// <summary>The font size of the width cases (<c>cases/widths-exact.html</c>): 14px, which Chromium keeps exact.</summary>
    private const double ExactSize = 10.5;

    private static ValueList<PageFragment> Paginate(StyledDocument document)
    {
        var result = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), document);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    /// <summary>Every box fragment on a page with the given role, in document order.</summary>
    private static IEnumerable<BoxFragment> Boxes(Fragment fragment, SemanticRole role) =>
        fragment is BoxFragment box
            ? (box.Box.Role == role ? [box] : Enumerable.Empty<BoxFragment>()).Concat(box.Children.SelectMany(child => Boxes(child.Fragment, role)))
            : [];

    /// <summary>The text of the first line of each cell in column 0, with the cell's top on the page.</summary>
    private static IEnumerable<string> FirstColumnTexts(PageFragment page) =>
        Boxes(page.Root.Fragment, SemanticRole.TableRow).Select(row => Text(row.Children[0].Fragment));

    private static string Text(Fragment fragment) => fragment switch
    {
        BoxFragment box => string.Concat(box.Children.Select(child => Text(child.Fragment))),
        LineFragment line => string.Concat(line.Children.Select(child => Text(child.Fragment))),
        TextFragment text => string.Concat(text.Glyphs.Select(glyph => glyph.Text)),
        _ => string.Empty,
    };

    /// <summary>Checks one length against Chromium's within n layout units; <paramref name="lengths"/> names what n counts.</summary>
    private static void ShouldMatchChromium(double ours, double chromium, int n, string lengths) =>
        ChromiumLayout.DifferenceInLayoutUnits(ours, chromium).Should().BeLessThanOrEqualTo(
            n + ChromiumLayout.FloatSlack, $"ours {ours} must be within {n} layout units of Chromium's {chromium} ({lengths})");

    private static double[] CellWidths(PageFragment page) => [.. Boxes(page.Root.Fragment, SemanticRole.TableCell).Select(cell => cell.Size.Width)];

    private static double TableWidth(PageFragment page) => Boxes(page.Root.Fragment, SemanticRole.Table).Single().Size.Width;

    /// <summary>Case t1: three auto columns whose content is too wide for the 300pt container.</summary>
    private static StyledDocument AutoColumns(TableModels t) =>
        TableModels.Page(300, 400, t.Table(
            [TrackSize.Auto.Instance, TrackSize.Auto.Instance, TrackSize.Auto.Instance],
            t.Body(t.Row(t.Cell("alpha beta gamma delta epsilon"), t.Cell("one two"), t.Cell("a much longer piece of text that needs wrapping in the column")))));

    /// <summary>Case t2: <c>&lt;col style="width:100pt"&gt;</c> and two auto columns, table width auto, in a 400pt container.</summary>
    private static StyledDocument FixedAndAutoColumns(TableModels t) =>
        TableModels.Page(400, 400, t.Table(
            [new TrackSize.Fixed(100.Pt()), TrackSize.Auto.Instance, TrackSize.Auto.Instance],
            t.Body(t.Row(t.Cell("fixed"), t.Cell("short"), t.Cell("somewhat longer text")))));

    /// <summary>Cases w3 and w4: a CSS grid of fr columns, width 300pt, plain items (no padding or border).</summary>
    private static StyledDocument FillColumns(TableModels t, double[] weights, string[] texts) =>
        TableModels.Page(300, 400, t.Table(
            [.. weights.Select(weight => (TrackSize)new TrackSize.Fill(weight))],
            t.Body(t.Row([.. texts.Select(text => t.Cell(text, BoxStyle.Initial))]))));

    [Fact]
    public void Auto_columns_too_wide_for_the_table_share_the_space_like_Chromium()
    {
        // Chromium (widths-exact, t1): td widths 95.098, 36.199, 167.953; table 300 (the container's width).
        var page = Paginate(AutoColumns(new TableModels(ExactSize))).Single();

        var cells = CellWidths(page);
        // Every column's share of the space between the minimum and the maximum widths depends on the snapped lengths of
        // all columns, so n is large: 6 paddings (2 per column), 3 min-content words and 3 max-content text items, plus the
        // column's own truncated share. The last column takes the remainder and so carries the other two truncations too.
        const string Shares = "6 paddings, 3 min-content and 3 max-content text items, its own truncated share";
        ShouldMatchChromium(cells[0], 95.098, 13, Shares);
        ShouldMatchChromium(cells[1], 36.199, 13, Shares);
        ShouldMatchChromium(cells[2], 167.953, 14, Shares + ", the truncations of the other two (it takes the remainder)");
        // The sum is exact: the columns fill the container, and 300pt is exactly 400px.
        ShouldMatchChromium(TableWidth(page), 300, 0, "nothing snapped: 300pt is 400px");
    }

    [Fact]
    public void A_fixed_column_and_auto_columns_that_fit_make_a_table_as_wide_as_its_content_like_Chromium()
    {
        // Chromium (widths-exact, t2): td widths 99.996, 32.695, 108.797; table 242.238 = the three columns plus half of
        // the left and right outer border (CSS 2.1 §17.6.2). The columns fit, so each takes its maximum width.
        var page = Paginate(FixedAndAutoColumns(new TableModels(ExactSize))).Single();

        var cells = CellWidths(page);
        ShouldMatchChromium(cells[0], 99.996, 1, "the specified 100pt, truncated");
        ShouldMatchChromium(cells[1], 32.695, 3, "2 truncated paddings, 1 ceiled text item");
        ShouldMatchChromium(cells[2], 108.797, 3, "2 truncated paddings, 1 ceiled text item");
        ShouldMatchChromium(TableWidth(page), 242.238, 7, "the three columns' 1 + 3 + 3; the half borders are whole pixels");
    }

    [Theory]
    [InlineData(new[] { 1.0, 2.0, 1.0 }, new[] { "one", "two words", "Supercalifragilisticexpialidocious" }, new[] { 50.555, 101.133, 148.313 }, new[] { 2, 2, 1 })]
    [InlineData(new[] { 2.5, 3.0, 10.0, 2.0 }, new[] { "a", "b", "c", "Duration" }, new[] { 41.777, 50.145, 167.145, 40.934 }, new[] { 2, 2, 2, 1 })]
    public void Fill_columns_share_the_leftover_like_CSS_grid_fr_in_Chromium(double[] weights, string[] texts, double[] chromium, int[] layoutUnits)
    {
        // Chromium (widths-exact, w3 and w4): display: grid; grid-template-columns: 1fr 2fr 1fr (and 2.5fr 3fr 10fr 2fr);
        // width 300pt. A column whose content is wider than its share keeps its min-content and the others share the rest
        // (§11.7.1). n: the column held at its min-content is one ceiled text item (1); every other column is a truncated
        // share of the leftover, which holds that text item (2). Chromium carries the truncated fractions to the next track.
        var page = Paginate(FillColumns(new TableModels(ExactSize), weights, texts)).Single();

        var cells = CellWidths(page);
        for (var index = 0; index < cells.Length; index++)
        {
            ShouldMatchChromium(cells[index], chromium[index], layoutUnits[index],
                layoutUnits[index] == 1 ? "1 ceiled text item, the track's base size" : "the base size of the text track in the leftover, its own truncated share");
        }
    }

    /// <summary>
    /// The one place the reference is not exact, kept under test (<c>cases/widths.html</c>, 10pt): at a font size Chromium
    /// rounds, its text is narrower than the font's advances by <see cref="ChromiumLayout.FontSizeFactor"/>, an artefact of
    /// the oracle (Blink floors the size to 1/100px, the Linux font backend truncates the pixels per em to 1/64px; README
    /// in tools/reference/chromium, "Where Chromium is not exact"). The engine keeps the exact advances. This test applies
    /// the documented factor to the engine's text widths and then compares with the same n as at an exact size; without the
    /// factor the long text items would be out of their bound. If Chromium or its font backend changes, this test says so.
    /// </summary>
    [Fact]
    public void At_a_font_size_Chromium_rounds_its_text_is_the_font_s_advances_scaled_by_the_documented_factor()
    {
        var factor = ChromiumLayout.FontSizeFactor(10);
        factor.Should().BeApproximately(13.328125 / (40.0 / 3), 1e-12, "10pt = 13.333px, laid out at 853/64px");

        // t2 at 10pt: cell = two 4pt paddings + two half borders (0.75pt) + the text. Chromium's paddings are truncated and
        // its text item is ceiled, as at an exact size: n = 3.
        const double Frame = 2 * 4 + 0.75;
        var cells = CellWidths(Paginate(FixedAndAutoColumns(new TableModels(10))).Single());
        ShouldMatchChromium(Frame + (cells[1] - Frame) * factor, 31.547, 3, "2 truncated paddings, 1 ceiled text item, text scaled by the factor");
        ShouldMatchChromium(Frame + (cells[2] - Frame) * factor, 103.992, 3, "2 truncated paddings, 1 ceiled text item, text scaled by the factor");

        // w3 and w4 at 10pt: the columns held at their min-content are one text item each: n = 1.
        var super = CellWidths(Paginate(FillColumns(new TableModels(10), [1, 2, 1], ["one", "two words", "Supercalifragilisticexpialidocious"])).Single())[2];
        var duration = CellWidths(Paginate(FillColumns(new TableModels(10), [2.5, 3, 10, 2], ["a", "b", "c", "Duration"])).Single())[3];
        ShouldMatchChromium(super * factor, 141.199, 1, "1 ceiled text item, scaled by the factor");
        ShouldMatchChromium(duration * factor, 38.965, 1, "1 ceiled text item, scaled by the factor");

        // The factor is what makes it fit: the engine's exact width of the long word is out of the one-unit bound.
        ChromiumLayout.DifferenceInLayoutUnits(super, 141.199).Should().BeGreaterThan(1, "Chromium's text at 10pt is narrower than the font's advances");
    }

    /// <summary>The print cases: a 260 × 160 pt page area (Chromium: <c>@page { size: 300pt 200pt; margin: 20pt }</c>),
    /// columns 80pt and the rest, a header row "head | title".</summary>
    private static StyledDocument PrintCase(TableModels t, params Box[] body) =>
        TableModels.Page(260, 160, t.Table([new TrackSize.Fixed(80.Pt()), new TrackSize.Fill(1)], [t.Head(t.Row(t.Cell("head"), t.Cell("title"))), .. body]));

    [Fact]
    public void A_long_table_breaks_between_rows_and_repeats_its_header_like_Chromium()
    {
        // Chromium print: page 1 head r01–r08, page 2 head r09–r16, page 3 head r17–r20; the first body row on every
        // page sits where r01 sits on page 1 (text top 39.63 on all three pages).
        var t = new TableModels();
        var document = PrintCase(t, t.Body([.. Enumerable.Range(1, 20).Select(i => t.Row(t.Cell($"r{i:00}"), t.Cell($"cell {i}")))]));

        var pages = Paginate(document);

        pages.Select(FirstColumnTexts).Should().BeEquivalentTo(
            [
                ["head", .. Enumerable.Range(1, 8).Select(i => $"r{i:00}")],
                ["head", .. Enumerable.Range(9, 8).Select(i => $"r{i:00}")],
                (string[])["head", .. Enumerable.Range(17, 4).Select(i => $"r{i:00}")],
            ],
            options => options.WithStrictOrdering());
        // The first body row is equally high on every page, so it starts where r01 starts: after the repeated header.
        var firstBodyRowHeights = pages.Select(page => Boxes(page.Root.Fragment, SemanticRole.TableRow).ElementAt(1)).Select(row => row.Size.Height);
        firstBodyRowHeights.Should().AllSatisfy(height => height.Should().BeApproximately(12 + 4 + 0.75, 1e-9, "line + padding + two half lines"));
    }

    [Fact]
    public void A_row_taller_than_the_space_left_breaks_inside_its_cells_like_Chromium()
    {
        // Chromium print: r05's second cell holds 25 lines (separated by <br>; here 25 paragraphs). Page 1:
        // line01–line06; page 2 (under the repeated header): line07–line17; page 3: line18–line25, then r06.
        var t = new TableModels();
        var lines = Enumerable.Range(1, 25).Select(j => $"line{j:00}").ToArray();
        var document = PrintCase(t, t.Body(
        [
            .. Enumerable.Range(1, 4).Select(i => t.Row(t.Cell($"r{i:00}"), t.Cell($"cell {i}"))),
            t.Row(t.Cell("r05"), t.Lines(lines)),
            t.Row(t.Cell("r06"), t.Cell("cell 6")),
        ]));

        var pages = Paginate(document);

        pages.Should().HaveCount(3);
        pages.Select(page => Boxes(page.Root.Fragment, SemanticRole.TableCell).Count(cell => Text(cell).Contains("line", StringComparison.Ordinal)))
            .Should().Equal(1, 1, 1);
        pages.Select(page => Text(page.Root.Fragment).Split("line").Length - 1).Should().Equal(6, 11, 8);
        FirstColumnTexts(pages[2]).Should().EndWith("r06");
    }

    [Fact]
    public void A_row_group_kept_together_moves_to_the_next_page_whole_like_Chromium()
    {
        // Chromium print: seven one-row groups, then <tbody style="break-inside: avoid"> with three rows. Page 1 holds
        // head and r01–r07 (g1 alone would still fit); page 2 holds head, g1, g2, g3.
        var t = new TableModels();
        var kept = BoxStyle.Initial with { BreakInside = BreakInside.Avoid };
        var document = PrintCase(t,
        [
            .. Enumerable.Range(1, 7).Select(i => t.Body(t.Row(t.Cell($"r{i:00}"), t.Cell($"cell {i}")))),
            t.Body(kept, t.Row(t.Cell("g1"), t.Cell("group")), t.Row(t.Cell("g2"), t.Cell("group")), t.Row(t.Cell("g3"), t.Cell("group"))),
        ]);

        var pages = Paginate(document);

        pages.Select(FirstColumnTexts).Should().BeEquivalentTo(
            [
                ["head", .. Enumerable.Range(1, 7).Select(i => $"r{i:00}")],
                (string[])["head", "g1", "g2", "g3"],
            ],
            options => options.WithStrictOrdering());
    }

    /// <summary>
    /// The row-span print cases <c>b4-row-span</c> and <c>b5-row-span-kept-cell</c>: no header, four labels that
    /// each span six one-line rows, on the 260 × 160 pt page area; in the second case every label is a cell kept
    /// together (<c>break-inside: avoid</c>).
    /// </summary>
    private static StyledDocument LabelCase(TableModels t, bool keptLabels)
    {
        var label = TableModels.ReferenceCell with { Span = new CellSpan(1, 6), BreakInside = keptLabels ? BreakInside.Avoid : BreakInside.Auto };
        var rows = Enumerable.Range(0, 4).SelectMany(unit => Enumerable.Range(0, 6).Select(index => index == 0
            ? t.Row(t.Cell($"m{unit}", label), t.Cell($"r{unit}x{index}"))
            : t.Row(t.Cell($"r{unit}x{index}"))));
        return TableModels.Page(260, 160, t.Table([new TrackSize.Fixed(80.Pt()), new TrackSize.Fill(1)], t.Body([.. rows])));
    }

    /// <summary>The text of every cell on a page that shows text.</summary>
    private static IEnumerable<string> CellTexts(PageFragment page) =>
        Boxes(page.Root.Fragment, SemanticRole.TableCell).Select(cell => Text(cell)).Where(text => text.Length > 0);

    [Fact]
    public void Rows_under_a_label_that_spans_them_break_between_them_like_Chromium()
    {
        // Chromium print (b4-row-span): a row span does not keep its rows together. Page 1 holds m0 and its six rows,
        // m1 and three of its rows; page 2 the other three rows of m1 (the label continues, its text stays on page 1),
        // m2 and its six rows; page 3 m3 and its six rows.
        var pages = Paginate(LabelCase(new TableModels(), keptLabels: false));

        pages.Select(page => CellTexts(page).ToList()).Should().BeEquivalentTo(
            [
                ["m0", "r0x0", "r0x1", "r0x2", "r0x3", "r0x4", "r0x5", "m1", "r1x0", "r1x1", "r1x2"],
                ["r1x3", "r1x4", "r1x5", "m2", "r2x0", "r2x1", "r2x2", "r2x3", "r2x4", "r2x5"],
                (List<string>)["m3", "r3x0", "r3x1", "r3x2", "r3x3", "r3x4", "r3x5"],
            ],
            options => options.WithStrictOrdering());
    }

    [Fact]
    public void Rows_under_a_label_kept_together_move_to_the_next_page_whole_like_Chromium()
    {
        // Chromium print (b5-row-span-kept-cell): with break-inside: avoid on the label, a break between its rows would
        // break inside it, so each label moves to the next page with its six rows: four pages.
        var pages = Paginate(LabelCase(new TableModels(), keptLabels: true));

        pages.Select(page => CellTexts(page).ToList()).Should().BeEquivalentTo(
            Enumerable.Range(0, 4).Select(unit => (List<string>)[$"m{unit}", .. Enumerable.Range(0, 6).Select(index => $"r{unit}x{index}")]),
            options => options.WithStrictOrdering());
    }
}
