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
    /// every word of the table once. Returns the pages.</summary>
    private static ValueList<PageFragment> NoEmptyStrip(TableElement table, IEnumerable<string> words, double offset, string because)
    {
        var result = Paginate(table, offset);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? because + ": " + result.Error : string.Empty);
        RowParts.EmptyStrips(result.Value).Should().BeEmpty(because);
        result.Value.SelectMany(page => Tables(page.Root.Fragment)).SelectMany(Words).Should().BeEquivalentTo(words, because + ": every word once");
        return result.Value;
    }

    /// <summary>The pages that end with a part of a row that goes on to the next page and shows no text: a cut where
    /// only a drawing (a border, a background, an image, a rule) is placed.</summary>
    private static int TextlessCuts(ValueList<PageFragment> pages)
    {
        var rows = pages.Select(page => Tables(page.Root.Fragment)
            .SelectMany(table => table.Children.Select(group => group.Fragment).OfType<BoxFragment>()
                .SelectMany(group => group.Children.Select(row => (Row: (BoxFragment)row.Fragment, Top: row.Offset.Y))))
            .Where(row => row.Row.Size.Height > 1e-6)
            .OrderBy(row => row.Top)
            .Select(row => row.Row)
            .ToList()).ToList();
        return Enumerable.Range(0, rows.Count - 1)
            .Count(page => rows[page].Count > 0 && !Words(rows[page][^1]).Any() && rows[page + 1].Any(row => row.Box.Id == rows[page][^1].Box.Id));
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
    /// block with padding only, an empty block with a background but no height, a table of one empty cell without
    /// lines; and a cell without content whose own background fills it (a cell's own background is not counted: it
    /// fills every part of the row).</summary>
    public static TheoryData<string> DrawsNothing => new()
    {
        "zero width space", "soft hyphen", "word joiner", "no-break space", "no-break space and zero width space", "line break only",
        "padded empty block", "table of one empty cell", "empty block with a background and no height", "empty cell with a background",
    };

    /// <summary>Cell content that draws but holds no text: an empty block with a border on every side or on one, an
    /// empty block with a background, an image, a table of one empty cell with lines (rules).</summary>
    public static TheoryData<string> DrawsWithoutText => new()
    {
        "bordered empty block", "block with a top border", "block with a bottom border", "block with a left border", "filled empty block", "image",
        "table of one empty cell with lines",
    };

    private static CellElement Content(string kind) => kind switch
    {
        "zero width space" => Cell("\u200B"),
        "soft hyphen" => Cell("\u00AD"),
        "word joiner" => Cell("\u2060"),
        "no-break space" => Cell("\u00A0"),
        "no-break space and zero width space" => Cell("\u00A0\u200B"),
        "line break only" => Cell([Paragraph([LineBreak()])]),
        "padded empty block" => Cell([Block([]).Padding(3.Pt())]),
        "table of one empty cell" => Cell([Table(Columns(Fill(1)), [TableRow([Cell([])])])]),
        "bordered empty block" => Cell([Block([]).Border(1.Pt(), Color.Black)]),
        "filled empty block" => Cell([Block([]).Padding(2.Pt()).Background(Grey)]),
        "empty block with a background and no height" => Cell([Block([]).Background(Grey)]),
        "empty cell with a background" => Cell([]).Background(Grey),
        "block with a top border" => Cell([Block([]).Padding(2.Pt()).BorderTop(1.Pt(), Color.Black)]),
        "block with a bottom border" => Cell([Block([]).Padding(2.Pt()).BorderBottom(1.Pt(), Color.Black)]),
        "block with a left border" => Cell([Block([]).Padding(2.Pt()).BorderLeft(1.Pt(), Color.Black)]),
        "image" => Cell([Image(TestImages.Bytes("rgb8.png")).Width(6.Pt()).Height(6.Pt()).Decorative()]),
        "table of one empty cell with lines" => Cell([Table(Columns(Fill(1)), [TableRow([Cell([])])]).GridLines(1.Pt(), Color.Black)]),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "not a cell kind of these tests"),
    };

    private static readonly Color Grey = new(0xC0, 0xC0, 0xC0);

    [Theory]
    [MemberData(nameof(DrawsNothing))]
    public void A_cell_that_draws_nothing_never_makes_an_empty_strip(string kind) =>
        EveryShape(kind).Should().Be(0, "a row part without text never goes on beside a cell that draws nothing");

    [Theory]
    [MemberData(nameof(DrawsWithoutText))]
    public void A_cell_that_draws_without_text_is_cut_only_where_it_draws(string kind) =>
        EveryShape(kind).Should().BePositive("where the text beside it does not fit yet but the drawing does, the row is cut and the drawing ends the page");

    /// <summary>The cell kind in one-line rows beside text (probe P3 of review 35), and as the third cell beside a
    /// label over three one-line rows and over two rows of three lines. In the one-line rows the text is 14 pt, so its
    /// line is taller than a line of the cell beside it (12 pt): page ends fall where that line fits and the text does
    /// not, and only what the cell draws decides the cut there. Returns how many pages of the one-line rows end with a
    /// row part that goes on and shows no text (<see cref="TextlessCuts"/>).</summary>
    private static int EveryShape(string kind)
    {
        var textless = 0;
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

                    var plain = Table(Columns(Fill(1), Fill(1)), [.. Enumerable.Range(0, 30).Select(i => (TablePart)TableRow([Cell($"r{i:00}").FontSize(14.Pt()), Content(kind)]))]);
                    textless += TextlessCuts(NoEmptyStrip(Lined(plain), Enumerable.Range(0, 30).Select(i => $"r{i:00}"), offset, because + ", one-line rows"));
                    var (three, threeWords) = Labelled(3, 1, kept: false, () => Content(kind));
                    NoEmptyStrip(Lined(three), threeWords, offset, because + ", label over three one-line rows");
                    var (two, twoWords) = Labelled(2, 3, kept: false, () => Content(kind));
                    NoEmptyStrip(Lined(two), twoWords, offset, because + ", label over two rows of three lines");
                }
            }
        }

        return textless;
    }

    [Fact]
    public void A_filled_block_in_a_column_of_no_width_draws_nothing()
    {
        // A column may be 0 pt wide; a block with a background in it has no area, so it draws nothing and never makes a
        // row cut beside the 14 pt text, which would leave an empty strip.
        foreach (var clone in new[] { true, false })
        {
            for (var offset = 0.0; offset < 30; offset += 1)
            {
                var table = Table(Columns(0.Pt(), Fill(1)), [.. Enumerable.Range(0, 30).Select(i => (TablePart)TableRow([Cell([Block([]).Padding(2.Pt()).Background(Grey)]), Cell($"r{i:00}").FontSize(14.Pt())]))]);
                NoEmptyStrip(clone ? table : table.SplitLinesAtBreaks(), Enumerable.Range(0, 30).Select(i => $"r{i:00}"), offset, $"{(clone ? "clone" : "slice")}, offset {offset}");
            }
        }
    }

    /// <summary>A label over three rows that holds only <paramref name="label"/>, beside 14 pt text in every row (probe
    /// Q5 of review 36). Eighteen rows; returns the table and its words.</summary>
    private static (TableElement Table, string[] Words) LabelHolding(Func<Element> label)
    {
        var table = Table(
            Columns(40.Pt(), Fill(1)),
            [.. Enumerable.Range(0, 18).Select(i => (TablePart)(i % 3 == 0
                ? TableRow([Cell([label()]).RowSpan(3), Cell($"r{i:00}").FontSize(14.Pt())])
                : TableRow([Cell($"r{i:00}").FontSize(14.Pt())])))]);
        return (table, [.. Enumerable.Range(0, 18).Select(i => $"r{i:00}")]);
    }

    public static TheoryData<string> DrawsOnlyInTheFirstRow => new() { "small table with lines", "small image" };

    [Theory]
    [MemberData(nameof(DrawsOnlyInTheFirstRow))]
    public void A_rule_or_an_image_counts_as_drawn_only_where_it_lies(string kind)
    {
        // Review 36, surviving mutant IM1: with a rule or an image counted wherever it lies, a label that holds only a
        // small nested table with lines (rules) or a small image, all within its first row, made a cut inside the
        // rows below it, and 91 of 120 pages ended with an empty strip. What the label draws lies above those rows.
        Element Label() => kind == "small table with lines"
            ? Table(Columns(Fill(1)), [TableRow([Cell([Block([]).Padding(2.Pt())])])]).GridLines(2.Pt(), Color.Black)
            : Image(TestImages.Bytes("rgb8.png")).Width(6.Pt()).Height(6.Pt()).Decorative();
        for (var offset = 0.0; offset < 60; offset += 0.5)
        {
            var (table, words) = LabelHolding(Label);
            NoEmptyStrip(table, words, offset, $"label holds only a {kind}, offset {offset}");
        }
    }

    [Fact]
    public void The_bottom_border_of_a_box_counts_as_drawn_at_the_bottom_of_the_box()
    {
        // Review 36, surviving mutant BB1: a label that holds only an empty block with 28 pt top padding and a 2 pt
        // bottom border; the border lies in the label's second row (its first row is one 14 pt line high). Where the
        // text of that row does not fit but the border does, the row is cut and the border ends the page. Measured at
        // the box's top instead, the border would lie in the first row and the row would never be cut there.
        var textless = 0;
        for (var offset = 0.0; offset < 60; offset += 0.5)
        {
            var (table, words) = LabelHolding(() => Block([]).PaddingTop(28.Pt()).BorderBottom(2.Pt(), Color.Black));
            textless += TextlessCuts(NoEmptyStrip(table, words, offset, $"offset {offset}"));
        }

        textless.Should().BePositive("a row is cut where only the bottom border of the label's block is placed");
    }

    [Fact]
    public void P1_a_label_over_three_one_line_rows_moves_the_row_that_does_not_fit_whole()
    {
        // Probe P1 of review 35, clone with 1 pt lines, page area 180: page 1 ended with "row #57, 10.6 high: NO TEXT"
        // under "Day3 a3", and the whole row "b3" followed on page 2. That cut is refused now, and the latest break left
        // is the one between "a3" and "b3": a row span does not keep its rows together (fix round 6, SL-1), so page 1
        // ends with "Day3 a3" and the label continues on page 2 beside "b3" and "c3".
        var table = Table(Columns(60.Pt(), Fill(1)), [.. Enumerable.Range(0, 6).SelectMany(u => new TablePart[]
        {
            TableRow([Cell($"Day{u}").RowSpan(3), Cell($"a{u}")]), TableRow([Cell($"b{u}")]), TableRow([Cell($"c{u}")]),
        })]).GridLines(1.Pt(), Color.Black);

        var result = Paginate(table, 0);

        result.IsSuccess.Should().BeTrue();
        RowParts.EmptyStrips(result.Value).Should().BeEmpty();
        var pages = result.Value.Select(page => Tables(page.Root.Fragment).Single()).ToList();
        Words(pages[0]).Should().EndWith(["Day2", "a2", "b2", "c2", "Day3", "a3"]);
        Words(pages[1]).Should().StartWith(["b3", "c3", "Day4"]);
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
