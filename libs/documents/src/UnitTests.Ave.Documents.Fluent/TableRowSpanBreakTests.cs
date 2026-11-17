using Ave.Documents.Core;
using Ave.Documents.Fluent;
using Ave.Documents.Layout;
using Ave.Documents.TestSupport;
using Ave.Extensions.Functional;
using UnitTests.Ave.Documents.Layout;
using static Ave.Documents.Fluent.Elements;

namespace UnitTests.Ave.Documents.Fluent;

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
}
