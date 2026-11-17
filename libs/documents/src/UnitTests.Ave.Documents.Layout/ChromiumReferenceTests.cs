using Ave.Documents.Core;
using Ave.Documents.DocumentModel;
using Ave.Documents.Layout;
using Ave.Documents.TestSupport;

namespace UnitTests.Ave.Documents.Layout;

/// <summary>
/// The tracks algorithm against Chromium's own layout of the same tables. The HTML cases and the script that prints
/// and measures them are in <c>tools/reference/chromium</c> (README there: how to run it, which case each test uses);
/// measured with Chromium 157 and again with Chromium 159, same numbers. All cells: Lato 10 pt,
/// <c>border-collapse: collapse; border: 0.75pt solid; padding: 2pt 4pt; vertical-align: top</c>. The border is
/// 0.75pt because Chromium draws a 1pt border as one device pixel (0.75pt at 96 dpi) and lays out with that width.
/// Chromium snaps positions to 1/64 px; widths agree within <see cref="Tolerance"/>, row positions exactly in rows per
/// page.
/// </summary>
public class ChromiumReferenceTests
{
    /// <summary>Chromium's layout unit is 1/64 px = 0.0117 pt; text widths come from the same font, so 0.05 pt is
    /// a few layout units.</summary>
    private const double Tolerance = 0.05;

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

    [Fact]
    public void Auto_columns_too_wide_for_the_table_share_the_space_like_Chromium()
    {
        // Chromium: td widths 94.852, 35.59, 168.809; table 300 (the container's width).
        var t = new TableModels();
        var table = t.Table(
            [TrackSize.Auto.Instance, TrackSize.Auto.Instance, TrackSize.Auto.Instance],
            t.Body(t.Row(t.Cell("alpha beta gamma delta epsilon"), t.Cell("one two"), t.Cell("a much longer piece of text that needs wrapping in the column"))));

        var page = Paginate(TableModels.Page(300, 400, table)).Single();

        var cells = Boxes(page.Root.Fragment, SemanticRole.TableCell).Select(cell => cell.Size.Width).ToArray();
        cells.Should().Equal([94.852, 35.59, 168.809], (ours, chromium) => Math.Abs(ours - chromium) < Tolerance);
        Boxes(page.Root.Fragment, SemanticRole.Table).Single().Size.Width.Should().BeApproximately(300, Tolerance);
    }

    [Fact]
    public void A_fixed_column_and_auto_columns_that_fit_make_a_table_as_wide_as_its_content_like_Chromium()
    {
        // Chromium: <col style="width:100pt">, table width auto: td widths 99.996, 31.547, 104.004; table 236.297
        // = the three columns plus half of the left and right outer border (CSS 2.1 §17.6.2).
        var t = new TableModels();
        var table = t.Table(
            [new TrackSize.Fixed(100.Pt()), TrackSize.Auto.Instance, TrackSize.Auto.Instance],
            t.Body(t.Row(t.Cell("fixed"), t.Cell("short"), t.Cell("somewhat longer text"))));

        var page = Paginate(TableModels.Page(400, 400, table)).Single();

        Boxes(page.Root.Fragment, SemanticRole.TableCell).Select(cell => cell.Size.Width).Should()
            .Equal([99.996, 31.547, 104.004], (ours, chromium) => Math.Abs(ours - chromium) < Tolerance);
        Boxes(page.Root.Fragment, SemanticRole.Table).Single().Size.Width.Should().BeApproximately(236.297, Tolerance);
    }

    [Theory]
    [InlineData(new[] { 1.0, 2.0, 1.0 }, new[] { "one", "two words", "Supercalifragilisticexpialidocious" }, new[] { 52.922, 105.867, 141.211 })]
    [InlineData(new[] { 2.5, 3.0, 10.0, 2.0 }, new[] { "a", "b", "c", "Duration" }, new[] { 42.094, 50.52, 168.41, 38.977 })]
    public void Fill_columns_share_the_leftover_like_CSS_grid_fr_in_Chromium(double[] weights, string[] texts, double[] chromium)
    {
        // Chromium: display: grid; grid-template-columns: 1fr 2fr 1fr (and 2.5fr 3fr 10fr 2fr); width 300pt. A column
        // whose content is wider than its share keeps its min-content and the others share the rest (§11.7.1).
        var t = new TableModels();
        var plain = BoxStyle.Initial;
        var row = t.Table([.. weights.Select(weight => (TrackSize)new TrackSize.Fill(weight))], t.Body(t.Row([.. texts.Select(text => t.Cell(text, plain))])));

        var page = Paginate(TableModels.Page(300, 400, row)).Single();

        Boxes(page.Root.Fragment, SemanticRole.TableCell).Select(cell => cell.Size.Width).Should()
            .Equal(chromium, (ours, reference) => Math.Abs(ours - reference) < Tolerance);
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
