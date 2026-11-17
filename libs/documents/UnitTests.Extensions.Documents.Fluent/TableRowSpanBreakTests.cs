using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Functional;
using UnitTests.Extensions.Documents.Layout;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// The rule for a break between two rows that a cell spanning rows crosses (fix round 6, SL-1, after probe run 36): a
/// row span does not keep its rows together. Such a break is an ordinary break between rows, with the appeal it would
/// have without the span; the spanning cell is continued on the next page. The author's own keep rules still apply: a
/// row group kept together, a spanning cell kept together, keep-with-next. Probe Q1 of review 36: with a label over six
/// rows every page held one group of rows and stayed half empty (210 pages where 123 were enough).
/// </summary>
public class TableRowSpanBreakTests
{
    /// <summary>The page area is 180 pt high (200 minus two 10 pt margins).</summary>
    private const double PageArea = 180;

    /// <summary>The rows of every table here: 48 divides into groups of 1, 3, 6 and 12.</summary>
    private const int RowCount = 48;

    private const double Epsilon = 1e-6;

    private static Result<ValueList<PageFragment>, global::Ave.Extensions.ErrorPaths.Error> Paginate(TableElement table, double offset) =>
        DocumentTranslator.ToModel(Document([Paragraph("intro").MarginBottom(offset.Pt()), table], Page(300.Pt(), 200.Pt()).Margin(10.Pt()), title: "Title", language: "en").FontFamily("Lato"))
            .OnSuccessBind(model => Paginator.Paginate(LayoutEnvironment.Create(TestFonts.SetWithItalic), model));

    /// <summary>Every text, rule and image on the page with its bottom edge, from the top of the page area.</summary>
    private static IEnumerable<(Fragment Fragment, double Bottom)> Drawn(Fragment fragment, double top) => fragment switch
    {
        BoxFragment box => box.Children.SelectMany(child => Drawn(child.Fragment, top + child.Offset.Y)),
        LineFragment line => line.Children.SelectMany(child => Drawn(child.Fragment, top + child.Offset.Y)),
        TextFragment or RuleFragment or ImageFragment => [(fragment, top + fragment.Size.Height)],
        _ => [],
    };

    /// <summary>The room between the lowest thing drawn on each page and the page end, for every page but the last.</summary>
    private static IReadOnlyList<double> UnusedRoom(ValueList<PageFragment> pages) =>
        [.. pages.Take(pages.Count - 1).Select(page => Math.Max(0, PageArea - Drawn(page.Root.Fragment, 0).Max(item => item.Bottom)))];

    private static IEnumerable<string> Words(Fragment fragment) => fragment switch
    {
        BoxFragment box => box.Children.SelectMany(child => Words(child.Fragment)),
        LineFragment line => line.Children.SelectMany(child => Words(child.Fragment)),
        TextFragment text => string.Concat(text.Glyphs.Select(glyph => glyph.Text)).Split(' ', StringSplitOptions.RemoveEmptyEntries),
        _ => [],
    };

    /// <summary>
    /// A table of <see cref="RowCount"/> one-line rows: a label column, whose label spans <paramref name="span"/> rows,
    /// and a text column. With <paramref name="span"/> 1 it is a table without row spans. With
    /// <paramref name="keptGroups"/> every group of rows under one label is a row group kept together; with
    /// <paramref name="keptLabels"/> every label is a cell kept together.
    /// </summary>
    private static TableElement Labelled(int span, bool keptGroups = false, bool keptLabels = false)
    {
        CellElement Label(int unit) => keptLabels ? Cell($"m{unit:00}").RowSpan(span).KeepTogether() : Cell($"m{unit:00}").RowSpan(span);
        ValueList<TableRowElement> Rows(int unit) =>
            [.. Enumerable.Range(0, span).Select(index => index == 0 ? TableRow([Label(unit), Cell($"r{unit:00}x{index:00}")]) : TableRow([Cell($"r{unit:00}x{index:00}")]))];
        var units = Enumerable.Range(0, RowCount / span);
        return keptGroups
            ? Table(Columns(60.Pt(), Fill(1)), [.. units.Select(unit => (TablePart)RowGroup(Rows(unit)).KeepTogether())]).GridLines(1.Pt(), Color.Black)
            : Table(Columns(60.Pt(), Fill(1)), [.. units.SelectMany(unit => Rows(unit).Select(row => (TablePart)row))]).GridLines(1.Pt(), Color.Black);
    }

    private static TableElement Broken(TableElement table, bool clone) => clone ? table : table.SplitLinesAtBreaks();

    private static ValueList<PageFragment> Laid(TableElement table, double offset, string because)
    {
        var result = Paginate(table, offset);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? because + ": " + result.Error : string.Empty);
        RowParts.EmptyStrips(result.Value).Should().BeEmpty(because);
        return result.Value;
    }

    [Theory]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(12)]
    public void Rows_under_a_label_that_spans_them_fill_their_pages_as_rows_without_a_label_do(int span)
    {
        // Thirty start offsets, clone and slice: the table with a label over `span` rows takes as many pages as the
        // same rows with a label in every row, and no page that is not the last ends with more unused room than the
        // most that table leaves anywhere in the sweep (less than one row: 14.4 pt of text and a 1 pt line). With 12
        // rows under a label the group is taller than the page (12 × 15.4 > 180), so it must be broken anyway.
        foreach (var clone in new[] { true, false })
        {
            var offsets = Enumerable.Range(0, 30).Select(offset => (double)offset).ToList();
            var plain = offsets.Select(offset => Laid(Broken(Labelled(1), clone), offset, $"label in every row, {(clone ? "clone" : "slice")}, offset {offset}")).ToList();
            var largest = plain.SelectMany(UnusedRoom).Max();
            largest.Should().BeLessThan(15.4, "the table without a label leaves less than one row at a page end");
            foreach (var (offset, without) in offsets.Zip(plain))
            {
                var because = $"label over {span} rows, {(clone ? "clone" : "slice")}, offset {offset}";
                var labelled = Laid(Broken(Labelled(span), clone), offset, because);
                labelled.Should().HaveCount(without.Count, because + ": as many pages as without the label");
                UnusedRoom(labelled).Should().OnlyContain(room => room <= largest + Epsilon, because + ": no more unused room at a page end than without the label");
            }
        }
    }

    [Theory]
    [InlineData(3, true, false)]
    [InlineData(6, true, false)]
    [InlineData(3, false, true)]
    [InlineData(6, false, true)]
    public void Rows_the_author_keeps_together_still_move_to_the_next_page_whole(int span, bool keptGroups, bool keptLabels)
    {
        // The author's keep rules still apply: a row group kept together, or a label that is a cell kept together,
        // keeps the rows under the label on one page. Every group of rows fits a page here (6 × 15.4 < 180).
        foreach (var clone in new[] { true, false })
        {
            for (var offset = 0.0; offset < 30; offset += 1)
            {
                var because = $"label over {span} rows, kept {(keptGroups ? "group" : "label")}, {(clone ? "clone" : "slice")}, offset {offset}";
                var pages = Laid(Broken(Labelled(span, keptGroups, keptLabels), clone), offset, because);
                var unitsByPage = pages.Select(page => Words(page.Root.Fragment).Where(word => word.StartsWith('r')).Select(word => word[1..3]).Distinct().ToList()).ToList();
                foreach (var unit in Enumerable.Range(0, RowCount / span).Select(unit => $"{unit:00}"))
                {
                    unitsByPage.Count(units => units.Contains(unit)).Should().Be(1, because + $": the rows of unit {unit} are on one page");
                }
            }
        }
    }

    [Fact]
    public void A_cell_kept_together_is_not_cut_inside_its_row()
    {
        // A cell kept together (break-inside: avoid) is a keep rule of the author's, like a row kept together: a break
        // inside its row would break inside the cell, so the row moves to the next page whole. Rows of one line
        // (15.4 pt with the line), then a row whose second cell has eight lines and is kept together; the start offset
        // puts that row across the page end.
        foreach (var clone in new[] { true, false })
        {
            for (var offset = 0.0; offset < 30; offset += 1)
            {
                var because = $"{(clone ? "clone" : "slice")}, offset {offset}";
                var kept = Cell([.. Enumerable.Range(0, 8).Select(line => (Element)Paragraph($"k{line}"))]).KeepTogether();
                var table = Table(Columns(60.Pt(), Fill(1)),
                [
                    .. Enumerable.Range(0, 6).Select(row => (TablePart)TableRow([Cell($"a{row}"), Cell($"t{row}")])),
                    TableRow([Cell("b"), kept]),
                    TableRow([Cell("c"), Cell("after")]),
                ]).GridLines(1.Pt(), Color.Black);
                var pages = Laid(Broken(table, clone), offset, because);
                pages.Count(page => Words(page.Root.Fragment).Any(word => word.StartsWith('k'))).Should().Be(1, because + ": the kept cell is on one page");
            }
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void A_label_kept_together_over_rows_of_several_lines_is_not_cut(int lines)
    {
        // The same keep rule for a label over three rows of two or three lines each: neither a break between those
        // rows nor a break inside one of them is taken while a better break exists, so each label is on one page with
        // its rows. Sixty start offsets put the page end in each of the label's three rows.
        foreach (var clone in new[] { true, false })
        {
            for (var offset = 0.0; offset < 60; offset += 1)
            {
                var because = $"{lines} lines, {(clone ? "clone" : "slice")}, offset {offset}";
                CellElement Lines(int unit, int row) => Cell([.. Enumerable.Range(0, lines).Select(line => (Element)Paragraph($"r{unit:00}x{row}{line}"))]);
                var table = Table(Columns(60.Pt(), Fill(1)), [.. Enumerable.Range(0, 6).SelectMany(unit => Enumerable.Range(0, 3).Select(row => (TablePart)(row == 0
                    ? TableRow([Cell($"m{unit:00}").RowSpan(3).KeepTogether(), Lines(unit, row)])
                    : TableRow([Lines(unit, row)]))))]).GridLines(1.Pt(), Color.Black);
                var pages = Laid(Broken(table, clone), offset, because);
                var unitsByPage = pages.Select(page => Words(page.Root.Fragment).Where(word => word.StartsWith('r')).Select(word => word[1..3]).Distinct().ToList()).ToList();
                foreach (var unit in Enumerable.Range(0, 6).Select(unit => $"{unit:00}"))
                {
                    unitsByPage.Count(units => units.Contains(unit)).Should().Be(1, because + $": the rows of unit {unit} are on one page");
                }
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void A_cell_kept_together_holds_back_only_the_breaks_that_cut_it(int keptRow)
    {
        // A label over three rows (not kept); one of its rows, `keptRow`, has a text cell of four lines kept together.
        // A break between two of these rows cuts only the label, unless the kept cell spans it, which it does not: it
        // starts below the one break and ends above the other. So both are ordinary breaks between rows and each is
        // taken where it is the latest that fits; the kept cell moves on whole. Counted as cut by the break before it
        // or after it, the cell would hold that break back and the page would end earlier. Sixty start offsets put the
        // page end in each of the three rows.
        string Kind(string word) => word[3..4];
        var kinds = keptRow == 1 ? new[] { "a", "k", "c" } : ["a", "b", "k"];
        foreach (var clone in new[] { true, false })
        {
            var splits = new HashSet<(string Above, string Below)>();
            for (var offset = 0.0; offset < 60; offset += 1)
            {
                var because = $"kept row {keptRow}, {(clone ? "clone" : "slice")}, offset {offset}";
                CellElement Text(int unit, int row) => row == keptRow
                    ? Cell([.. Enumerable.Range(0, 4).Select(line => (Element)Paragraph($"r{unit:00}k{line}"))]).KeepTogether()
                    : Cell($"r{unit:00}{kinds[row]}");
                var table = Table(Columns(60.Pt(), Fill(1)), [.. Enumerable.Range(0, 6).SelectMany(unit => Enumerable.Range(0, 3).Select(row => (TablePart)(row == 0
                    ? TableRow([Cell($"m{unit:00}").RowSpan(3), Text(unit, row)])
                    : TableRow([Text(unit, row)]))))]).GridLines(1.Pt(), Color.Black);
                var pages = Laid(Broken(table, clone), offset, because)
                    .Select(page => Words(page.Root.Fragment).Where(word => word.StartsWith('r')).ToList()).ToList();
                foreach (var unit in Enumerable.Range(0, 6).Select(unit => $"r{unit:00}k"))
                {
                    pages.Count(page => page.Any(word => word.StartsWith(unit))).Should().Be(1, because + $": the kept cell {unit} is on one page");
                }

                // The page breaks that fall inside a unit, as the kinds of the rows on both sides.
                splits.UnionWith(Enumerable.Range(0, pages.Count - 1)
                    .Where(page => pages[page][^1][1..3] == pages[page + 1][0][1..3])
                    .Select(page => (Kind(pages[page][^1]), Kind(pages[page + 1][0]))));
            }

            splits.Should().Contain([(kinds[0], kinds[1]), (kinds[1], kinds[2])], $"kept row {keptRow}, {(clone ? "clone" : "slice")}: the page breaks before and after the kept row, inside the unit");
        }
    }
}
