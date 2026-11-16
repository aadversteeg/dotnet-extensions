using Ave.Documents.Core;
using Ave.Documents.DocumentModel;
using Ave.Documents.Fluent;
using Ave.Extensions.Functional;
using Ave.Documents.Layout;
using Ave.Documents.TestSupport;
using static Ave.Documents.Fluent.Elements;

namespace UnitTests.Ave.Documents.Fluent;

/// <summary>
/// A table row with an empty cell at a page break (spot check of fix round 3, finding SC-1, probes K6 and K7). The
/// rule: a cell without content does not count as content placed on the page; a row is cut inside itself only when a
/// cell with content places part of it on this page and leaves part for the next one. So a row that does not fit moves
/// to the next page whole, and no page shows a strip of a row without text, whatever the empty cell is made of.
/// </summary>
public class TableEmptyCellBreakTests
{
    private const double Epsilon = 1e-6;

    /// <summary>The ways a cell can be without content.</summary>
    public static TheoryData<string> EmptyCells => new() { "no children", "empty string", "only spaces", "empty block", "empty paragraph" };

    private static CellElement Empty(string kind) => kind switch
    {
        "no children" => Cell([]),
        "empty string" => Cell(""),
        "only spaces" => Cell("   "),
        "empty block" => Cell([Block([])]),
        "empty paragraph" => Cell([Paragraph("")]),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "not an empty cell kind"),
    };

    private static ValueList<PageFragment> Paginate(TableElement table, double offset)
    {
        var document = Document([Paragraph("intro").MarginBottom(offset.Pt()), table], Page(300.Pt(), 200.Pt()).Margin(10.Pt()), title: "Title", language: "en").FontFamily("Lato");
        var result = DocumentTranslator.ToModel(document).OnSuccessBind(model => Paginator.Paginate(LayoutEnvironment.Create(TestFonts.SetWithItalic), model));
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    private static IEnumerable<(BoxFragment Box, Point At)> Boxes(Fragment fragment, Point at, SemanticRole role) =>
        fragment is BoxFragment box
            ? (box.Box.Role == role ? [(box, at)] : Enumerable.Empty<(BoxFragment, Point)>())
                .Concat(box.Children.SelectMany(child => Boxes(child.Fragment, at + child.Offset, role)))
            : [];

    private static IEnumerable<string> Words(Fragment fragment) => fragment switch
    {
        BoxFragment box => box.Children.SelectMany(child => Words(child.Fragment)),
        LineFragment line => line.Children.SelectMany(child => Words(child.Fragment)),
        TextFragment text => string.Concat(text.Glyphs.Select(glyph => glyph.Text)).Split(' ', StringSplitOptions.RemoveEmptyEntries),
        _ => [],
    };

    private static string Text(int words, string tag) => string.Join(" ", Enumerable.Range(0, words).Select(index => $"{tag}{index:000}"));

    /// <summary>Checks every page of the table: no part of a row without text, and every word of the table once (a cut row shows its short cells before the rest of its long cell).</summary>
    private static void EveryRowPartHoldsText(ValueList<PageFragment> pages, IEnumerable<string> expected, string because)
    {
        var words = new List<string>();
        foreach (var (page, number) in pages.Select((page, index) => (page, index + 1)))
        {
            foreach (var (table, _) in Boxes(page.Root.Fragment, new Point(0, 0), SemanticRole.Table))
            {
                foreach (var (row, rowAt) in Boxes(table, new Point(0, 0), SemanticRole.TableRow).Where(row => row.Box.Size.Height > Epsilon))
                {
                    Words(row).Should().NotBeEmpty(because + $": page {number}, the row part at {rowAt.Y:0.##} ({row.Size.Height:0.##} high) shows text");
                }

                words.AddRange(Words(table));
            }
        }

        words.Should().BeEquivalentTo(expected, because + ": every word once");
    }

    [Theory]
    [MemberData(nameof(EmptyCells))]
    public void K6_a_long_cell_beside_an_empty_one_is_cut_only_where_the_long_cell_goes_on(string empty)
    {
        foreach (var clone in new[] { true, false })
        {
            for (var offset = 100.0; offset < 180; offset += 0.5)
            {
                var table = Table(Columns(Fill(1), Fill(1)), [TableRow([Cell("a"), Cell("b")]), TableRow([Cell(Text(40, "w")), Empty(empty)]), TableRow([Cell("c"), Cell("d")])])
                    .GridLines(2.Pt(), Color.Black);
                var pages = Paginate(clone ? table : table.SplitLinesAtBreaks(), offset);
                EveryRowPartHoldsText(pages, ["a", "b", .. Text(40, "w").Split(' '), "c", "d"], $"{empty}, {(clone ? "clone" : "slice")}, offset {offset}");
            }
        }
    }

    /// <summary>The row shapes of probe K7: one-line rows, two-line rows, and three-line text beside an empty and a
    /// short cell; each with the empty cell of the given kind.</summary>
    private static (string Name, ValueList<TrackSize> Columns, Func<int, TableRowElement> Row, Func<int, IEnumerable<string>> Words)[] Shapes(string empty) =>
    [
        ("one-line rows", Columns(Fill(1), Fill(1)), i => TableRow([Cell($"r{i:00}"), Empty(empty)]), i => [$"r{i:00}"]),
        ("two-line rows", Columns(Fill(1), Fill(1)), i => TableRow([Cell([Paragraph($"r{i:00}"), Paragraph($"q{i:00}")]), Empty(empty)]), i => [$"r{i:00}", $"q{i:00}"]),
        ("three-line text beside an empty and a short cell", Columns(100.Pt(), Fill(1), 40.Pt()),
            i => TableRow([Cell($"r{i:00} word word word word word word word word word word word word"), Empty(empty), Cell("x")]),
            i => [$"r{i:00}", .. Enumerable.Repeat("word", 12), "x"]),
    ];

    [Theory]
    [MemberData(nameof(EmptyCells))]
    public void K7_ordinary_rows_with_an_empty_cell_never_leave_an_empty_strip_at_the_page_end(string empty)
    {
        foreach (var (name, columns, row, words) in Shapes(empty))
        {
            foreach (var clone in new[] { true, false })
            {
                foreach (var line in new[] { 0.0, 1.0 })
                {
                    for (var offset = 0.0; offset < 30; offset += 0.25)
                    {
                        var table = Table(columns, [.. Enumerable.Range(0, 30).Select(row)]);
                        table = line > 0 ? table.GridLines(line.Pt(), Color.Black) : table;
                        var pages = Paginate(clone ? table : table.SplitLinesAtBreaks(), offset);
                        EveryRowPartHoldsText(pages, [.. Enumerable.Range(0, 30).SelectMany(words)], $"{name}, {empty}, {(clone ? "clone" : "slice")}, line {line}, offset {offset}");
                    }
                }
            }
        }
    }

    [Fact]
    public void Rows_with_text_in_every_cell_still_break_as_before()
    {
        // The control of probe K7: one-line rows with text in both cells never showed a strip; they still do not.
        for (var offset = 0.0; offset < 30; offset += 0.25)
        {
            var table = Table(Columns(Fill(1), Fill(1)), [.. Enumerable.Range(0, 30).Select(i => TableRow([Cell($"r{i:00}"), Cell("v")]))]).GridLines(1.Pt(), Color.Black);
            EveryRowPartHoldsText(Paginate(table, offset), [.. Enumerable.Range(0, 30).SelectMany(i => new[] { $"r{i:00}", "v" })], $"offset {offset}");
        }
    }
}
