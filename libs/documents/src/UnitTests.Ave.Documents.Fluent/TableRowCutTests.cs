using Ave.Documents.Core;
using Ave.Documents.DocumentModel;
using Ave.Documents.Fluent;
using Ave.Documents.Layout;
using Ave.Documents.TestSupport;
using Ave.Extensions.Functional;
using UnitTests.Ave.Documents.Layout;
using static Ave.Documents.Fluent.Elements;

namespace UnitTests.Ave.Documents.Fluent;

/// <summary>
/// The rule for a row that a page break cuts (fix round 5, SP-1, after probe run 35): a row is cut inside itself only
/// when, on this page and below the top of that row, some cell draws something, and something of the row is left for
/// the next page. Decided from the placed result, not from the document tree. So a short label that spans rows, whose
/// text sits above the row that does not fit, does not make that row a strip at the page end; nor does a cell that
/// holds only invisible characters or an empty block with padding. A cell whose content does draw at the cut (an empty
/// block with a border or a background) may be cut. Every page is checked with <see cref="RowParts"/>.
/// </summary>
public class TableRowCutTests
{
    private static Result<ValueList<PageFragment>, global::Ave.Extensions.ErrorPaths.Error> Paginate(TableElement table, double offset) =>
        DocumentTranslator.ToModel(Document([Paragraph("intro").MarginBottom(offset.Pt()), table], Page(300.Pt(), 200.Pt()).Margin(10.Pt()), title: "Title", language: "en").FontFamily("Lato"))
            .OnSuccessBind(model => Paginator.Paginate(LayoutEnvironment.Create(TestFonts.SetWithItalic), model));

    private static IEnumerable<string> Words(Fragment fragment) => fragment switch
    {
        BoxFragment box => box.Children.SelectMany(child => Words(child.Fragment)),
        LineFragment line => line.Children.SelectMany(child => Words(child.Fragment)),
        TextFragment text => string.Concat(text.Glyphs.Select(glyph => glyph.Text)).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(word => word.Any(char.IsLetterOrDigit)),
        _ => [],
    };

    private static IEnumerable<BoxFragment> Tables(Fragment fragment) =>
        fragment is not BoxFragment box ? []
        : box.Box.Role == SemanticRole.Table ? [box]
        : box.Children.SelectMany(child => Tables(child.Fragment));

    private static string Lorem(int words, string tag) => string.Join(" ", Enumerable.Range(0, words).Select(index => $"{tag}{index:000}"));

    /// <summary>Lays the table out after "intro" with the given bottom margin, and checks every page: no empty strip,
    /// every word of the table once.</summary>
    private static void NoEmptyStrip(TableElement table, IEnumerable<string> words, double offset, string because)
    {
        var result = Paginate(table, offset);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? because + ": " + result.Error : string.Empty);
        RowParts.EmptyStrips(result.Value).Should().BeEmpty(because);
        result.Value.SelectMany(page => Tables(page.Root.Fragment)).SelectMany(Words).Should().BeEquivalentTo(words, because + ": every word once");
    }

    /// <summary>The shapes with a label that spans <paramref name="span"/> rows of <paramref name="lines"/> lines each,
    /// text in every cell; with <paramref name="kept"/> every row is kept together. A third cell holds
    /// <paramref name="extra"/> when one is given. Thirty rows of one line, twelve of several: three pages or more.</summary>
    private static (TableElement Table, string[] Words) Labelled(int span, int lines, bool kept, Func<CellElement>? extra = null)
    {
        var rows = lines == 1 ? 30 : 12;
        var words = new List<string>();
        CellElement Text(string text)
        {
            words.AddRange(text.Split(' '));
            return Cell(text);
        }

        // With lines > 1, 3 × lines words: a row of several lines (two or three, by the width of the text column).
        CellElement RowText(int unit, int index) => Text(lines == 1 ? $"r{unit:00}x{index}" : Lorem(3 * lines, $"r{unit:00}x{index}y"));

        TableRowElement Row(ValueList<CellElement> cells)
        {
            var row = TableRow(extra is null ? cells : [.. cells, extra()]);
            return kept ? row.KeepTogether() : row;
        }

        var parts = Enumerable.Range(0, rows / span).SelectMany(unit => Enumerable.Range(0, span).Select(index => index == 0
            ? Row([Text($"m{unit:00}").RowSpan(span), RowText(unit, index)])
            : Row([RowText(unit, index)])));
        ValueList<TrackSize> columns = extra is null ? Columns(60.Pt(), Fill(1)) : Columns(60.Pt(), Fill(1), Fill(1));
        return (Table(columns, [.. parts]), [.. words]);
    }

    public static TheoryData<int, int, bool> LabelShapes()
    {
        var shapes = new TheoryData<int, int, bool>();
        foreach (var span in new[] { 2, 3 })
        {
            foreach (var lines in new[] { 1, 3 })
            {
                shapes.Add(span, lines, false);
                shapes.Add(span, lines, true);
            }
        }

        return shapes;
    }

    [Theory]
    [MemberData(nameof(LabelShapes))]
    public void A_short_label_that_spans_rows_never_leaves_an_empty_strip_at_the_page_end(int span, int lines, bool kept)
    {
        // Probe P2 of review 35: up to 120 of 120 start offsets ended a page with a strip of the row that did not fit,
        // because the label beside it had placed its text in an earlier row and still counted as placing part of it.
        foreach (var clone in new[] { true, false })
        {
            foreach (var line in new[] { 0.0, 1.0 })
            {
                for (var offset = 0.0; offset < 30; offset += 0.25)
                {
                    var (table, words) = Labelled(span, lines, kept);
                    table = line > 0 ? table.GridLines(line.Pt(), Color.Black) : table;
                    NoEmptyStrip(clone ? table : table.SplitLinesAtBreaks(), words, offset, $"label over {span} rows of {lines} lines, kept {kept}, {(clone ? "clone" : "slice")}, line {line}, offset {offset}");
                }
            }
        }
    }

    /// <summary>Cell content that draws nothing: invisible characters, white space that is not collapsed, an empty
    /// block with padding only, a table of one empty cell without lines.</summary>
    public static TheoryData<string> DrawsNothing => new()
    {
        "zero width space", "soft hyphen", "word joiner", "no-break space", "line break only", "padded empty block", "table of one empty cell",
    };

    /// <summary>Cell content that draws but holds no text: an empty block with a border, an empty block with a background.</summary>
    public static TheoryData<string> DrawsWithoutText => new() { "bordered empty block", "filled empty block" };

    private static CellElement Content(string kind) => kind switch
    {
        "zero width space" => Cell("\u200B"),
        "soft hyphen" => Cell("\u00AD"),
        "word joiner" => Cell("\u2060"),
        "no-break space" => Cell("\u00A0"),
        "line break only" => Cell([Paragraph([LineBreak()])]),
        "padded empty block" => Cell([Block([]).Padding(3.Pt())]),
        "table of one empty cell" => Cell([Table(Columns(Fill(1)), [TableRow([Cell([])])])]),
        "bordered empty block" => Cell([Block([]).Border(1.Pt(), Color.Black)]),
        "filled empty block" => Cell([Block([]).Padding(2.Pt()).Background(new Color(0xC0, 0xC0, 0xC0))]),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "not a cell kind of these tests"),
    };

    [Theory]
    [MemberData(nameof(DrawsNothing))]
    public void A_cell_that_draws_nothing_never_makes_an_empty_strip(string kind) => EveryShape(kind);

    [Theory]
    [MemberData(nameof(DrawsWithoutText))]
    public void A_cell_that_draws_without_text_is_cut_only_where_it_draws(string kind) => EveryShape(kind);

    /// <summary>The cell kind in one-line rows beside text (probe P3 of review 35), and as the third cell beside a
    /// label over three one-line rows and over two rows of three lines.</summary>
    private static void EveryShape(string kind)
    {
        foreach (var clone in new[] { true, false })
        {
            foreach (var line in new[] { 0.0, 1.0 })
            {
                for (var offset = 0.0; offset < 30; offset += 1)
                {
                    var because = $"{kind}, {(clone ? "clone" : "slice")}, line {line}, offset {offset}";
                    TableElement Lined(TableElement table)
                    {
                        var lined = line > 0 ? table.GridLines(line.Pt(), Color.Black) : table;
                        return clone ? lined : lined.SplitLinesAtBreaks();
                    }

                    var plain = Table(Columns(Fill(1), Fill(1)), [.. Enumerable.Range(0, 30).Select(i => (TablePart)TableRow([Cell($"r{i:00}"), Content(kind)]))]);
                    NoEmptyStrip(Lined(plain), Enumerable.Range(0, 30).Select(i => $"r{i:00}"), offset, because + ", one-line rows");
                    var (three, threeWords) = Labelled(3, 1, kept: false, () => Content(kind));
                    NoEmptyStrip(Lined(three), threeWords, offset, because + ", label over three one-line rows");
                    var (two, twoWords) = Labelled(2, 3, kept: false, () => Content(kind));
                    NoEmptyStrip(Lined(two), twoWords, offset, because + ", label over two rows of three lines");
                }
            }
        }
    }

    [Fact]
    public void P1_a_label_over_three_one_line_rows_moves_the_row_that_does_not_fit_whole()
    {
        // Probe P1 of review 35, clone with 1 pt lines, page area 180: page 1 ended with "row #57, 10.6 high: NO TEXT"
        // under "Day3 a3", and the whole row "b3" followed on page 2. That cut is refused now, and of the breaks left,
        // the one before the unit (no cell cut) wins over the one between its rows (which cuts the label): page 1 ends
        // with unit 2 and the label goes to page 2 with its three rows.
        var table = Table(Columns(60.Pt(), Fill(1)), [.. Enumerable.Range(0, 6).SelectMany(u => new TablePart[]
        {
            TableRow([Cell($"Day{u}").RowSpan(3), Cell($"a{u}")]), TableRow([Cell($"b{u}")]), TableRow([Cell($"c{u}")]),
        })]).GridLines(1.Pt(), Color.Black);

        var result = Paginate(table, 0);

        result.IsSuccess.Should().BeTrue();
        RowParts.EmptyStrips(result.Value).Should().BeEmpty();
        var pages = result.Value.Select(page => Tables(page.Root.Fragment).Single()).ToList();
        Words(pages[0]).Should().EndWith(["Day2", "a2", "b2", "c2"]);
        Words(pages[1]).Should().StartWith(["Day3", "a3", "b3", "c3"]);
    }

    [Fact]
    public void P3_a_cell_of_only_a_zero_width_space_does_not_cut_the_row_beside_it()
    {
        // Probe P3 of review 35: the table on page 1 was 165.6 high, its last 10.6 a strip without text (row "r10" on
        // page 2). The cell's text is a paragraph in the document tree, but its line draws nothing.
        var table = Table(Columns(Fill(1), Fill(1)), [.. Enumerable.Range(0, 14).Select(i => (TablePart)TableRow([Cell($"r{i:00}"), Cell("\u200B")]))]).GridLines(1.Pt(), Color.Black);

        var result = Paginate(table, 0);

        result.IsSuccess.Should().BeTrue();
        RowParts.EmptyStrips(result.Value).Should().BeEmpty();
        var first = Tables(result.Value[0].Root.Fragment).Single();
        first.Size.Height.Should().BeApproximately(155, 1e-6);
        Words(first).Should().EndWith(["r09"]);
    }
}
