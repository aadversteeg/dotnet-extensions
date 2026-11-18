using System.IO;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Functional;
using UnitTests.Extensions.Documents.Layout;
using Xunit.Abstractions;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// The sweep of item A of milestone 2 (where a page starts and where a row is cut): generated documents of every kind of
/// leading space before every kind of first piece, at 51 start offsets each, on the 276 × 150 pt page area of the
/// <c>a1-*</c> reference cases. For every document: it is laid out (no refusal: nothing in it is taller than a page),
/// every word is on some page exactly once, nothing passes the end of the page area, and no part of a table row that
/// shows nothing is left at a page end or start — except where the row shows nothing over a whole page, which no layout
/// can avoid. The generator is deterministic; the counts go to the test output.
/// </summary>
public class PageStartSweepTests(ITestOutputHelper output)
{
    private const double AreaHeight = 150;

    private static PageSetup Area => Page(300.Pt(), 174.Pt()).Margin(12.Pt());

    private static readonly Color Grey = new(0xCC, 0xCC, 0xCC);

    private static ParagraphElement Lines(int count, string prefix) =>
        Paragraph([.. Enumerable.Range(0, count).SelectMany(i => i == 0 ? new[] { Text($"{prefix}{i}") } : [LineBreak(), Text($"{prefix}{i}")])]);

    private static Span[] Breaks(int count) => [.. Enumerable.Range(0, count).Select(_ => LineBreak())];

    /// <summary>The kinds of leading space: what comes before the first piece, moving it down by the offset.</summary>
    private static readonly (string Name, Func<double, Element, Element[]> Make)[] Leading =
    [
        ("nothing", (_, piece) => [piece]),
        ("empty block with a bottom margin", (offset, piece) => [Block([]).MarginBottom(offset.Pt()), piece]),
        ("the piece's own top margin, in a block", (offset, piece) => [Block([piece]).MarginTop(offset.Pt())]),
        ("a line of text with a bottom margin", (offset, piece) => [Paragraph("intro").MarginBottom(offset.Pt()), piece]),
        ("a forced break, then an empty block with a bottom margin", (offset, piece) => [Paragraph("first"), PageBreak(), Block([]).MarginBottom(offset.Pt()), piece]),
        ("a block with top padding holding the piece", (offset, piece) => [Block([piece]).PaddingTop(offset.Pt()).Background(Grey)]),
        ("a margin collapsing through a grey block that holds the piece", (offset, piece) => [Block([Block([]).MarginBottom(offset.Pt()), piece]).Background(Grey)]),
        ("two empty blocks with half the margin each", (offset, piece) => [Block([]).MarginBottom((offset / 2).Pt()), Block([]).MarginTop((offset / 2).Pt()), piece]),
    ];

    private static TableElement Lined(TableElement table) => table.GridLines(0.75.Pt(), Color.Black).SplitLinesAtBreaks();

    /// <summary>The kinds of first piece, each with its words.</summary>
    private static readonly (string Name, Func<Element> Make)[] Pieces =
    [
        ("a one-line paragraph", () => Paragraph("para")),
        ("a paragraph of four lines", () => Lines(4, "p")),
        ("a paragraph of nine lines kept together", () => Lines(9, "k").KeepTogether()),
        ("a one-row table", () => Lined(Table(Columns(Fill(1)), [TableRow([Cell("row")])]))),
        ("a table whose first row of nine lines is kept together", () => Lined(Table(Columns(Fill(1)),
            [TableRow([Cell([Lines(9, "r")])]).KeepTogether(), TableRow([Cell("next")])]))),
        ("a table with a header row", () => Lined(Table(Columns(Fill(1)), [HeaderRow([Cell("head")]), TableRow([Cell("r0")]), TableRow([Cell("r1")])]))),
        ("a 118 pt line in a block with 20 pt padding, then a tail", () => Block([Paragraph("W").FontSize(118.Pt()), Paragraph("tail")]).Padding(20.Pt()).Background(Grey)),
        ("a row with a blank stretch of 30 lines between its text", () => Lined(Table(Columns(Fill(1), Fill(1)),
            [TableRow([Cell("x"), Cell([Paragraph("top"), Paragraph([.. Breaks(30)]), Paragraph("end")])]), TableRow([Cell("after"), Cell("a")])]))),
        ("a row of twenty padded empty blocks beside an empty cell", () => Lined(Table(Columns(Fill(1), Fill(1)),
            [TableRow([Cell(""), Cell([.. Enumerable.Range(0, 20).Select(_ => (Element)Block([]).Padding(5.Pt()))])]), TableRow([Cell("after"), Cell("a")])]))),
        ("rows of a line beside two padded empty blocks", () => Lined(Table(Columns(Fill(1), Fill(1)),
            [.. Enumerable.Range(0, 6).Select(i => (TablePart)TableRow([Cell($"t{i}"), Cell([Block([]).Padding(4.Pt()), Block([]).Padding(4.Pt())])]))]))),
        ("units of two rows beside a spanning label, an 8 pt line under the first", () => Table(Columns(80.Pt(), Fill(1)),
            [.. Enumerable.Range(0, 3).SelectMany(unit => new TablePart[]
            {
                TableRow([Cell($"s{unit}a s{unit}b s{unit}c s{unit}d s{unit}e").RowSpan(2).PaddingTop(5.Pt()), Cell($"a{unit}").BorderBottom(8.Pt(), Color.Black)]),
                TableRow([Cell($"b{unit}")]),
            })]).GridLines(1.Pt(), Color.Black)),
        ("a row with a cell of three lines kept together beside nine lines", () => Lined(Table(Columns(Fill(1), Fill(1)),
            [TableRow([Cell([Lines(3, "c")]).KeepTogether(), Cell([Lines(9, "d")])]), TableRow([Cell("after"), Cell("z")])]))),
    ];

    /// <summary>The words of a fragment tree; a repeated header row and every slice after the first of content taller
    /// than a page are copies, not words of their own.</summary>
    private static IEnumerable<string> Words(Fragment fragment) => fragment switch
    {
        BoxFragment { IsRepeated: true } => [],
        SliceFragment slice => slice.IsContinuation ? [] : Words(slice.Content.Fragment),
        BoxFragment box => box.Children.SelectMany(child => Words(child.Fragment)),
        LineFragment line => line.Children.SelectMany(child => Words(child.Fragment)),
        TextFragment text => string.Concat(text.Glyphs.Select(glyph => glyph.Text)).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Any(char.IsLetterOrDigit)),
        _ => [],
    };

    /// <summary>The lowest bottom of anything a fragment tree draws or holds, from the page area's top.</summary>
    private static double Bottom(Fragment fragment, double top) => fragment switch
    {
        BoxFragment box => box.Children.Select(child => Bottom(child.Fragment, top + child.Offset.Y)).Append(top + box.Size.Height).Max(),
        LineFragment line => line.Children.Select(child => Bottom(child.Fragment, top + child.Offset.Y)).DefaultIfEmpty(top).Max(),
        _ => top + fragment.Size.Height,
    };

    [Fact]
    public void Generated_page_starts_and_row_cuts_lay_out_without_refusal_lost_words_overflow_or_avoidable_strips()
    {
        var (documents, pages, blankPages) = (0, 0, 0);
        var problems = new List<string>();
        foreach (var (leadingName, leading) in Leading)
        {
            foreach (var (pieceName, piece) in Pieces)
            {
                // The words of the document without leading space: the same words, whatever the offset.
                var expected = Paginate([.. leading(0, piece())]).Value.SelectMany(page => Words(page.Root.Fragment))
                    .OrderBy(word => word, StringComparer.Ordinal).ToList();
                for (var step = 0; step <= 50; step++)
                {
                    var offset = step * 3.0;
                    var because = $"{leadingName}, {pieceName}, offset {offset}";
                    documents++;
                    var result = Paginate([.. leading(offset, piece())]);
                    if (result.IsFailure)
                    {
                        problems.Add($"{because}: refused {result.Error.Code}: {result.Error.Message}");
                        continue;
                    }

                    var laid = result.Value;
                    pages += laid.Count;
                    blankPages += laid.Count(page => !Words(page.Root.Fragment).Any());
                    var words = laid.SelectMany(page => Words(page.Root.Fragment)).OrderBy(word => word, StringComparer.Ordinal).ToList();
                    if (!words.SequenceEqual(expected))
                    {
                        problems.Add($"{because}: words [{string.Join(" ", words)}] where [{string.Join(" ", expected)}]");
                    }

                    if (laid.Select(page => Bottom(page.Root.Fragment, 0)).Max() is var over && over > AreaHeight + 1e-6)
                    {
                        problems.Add($"{because}: content reaches {over:0.###} pt, past the page area");
                    }

                    foreach (var strip in RowParts.EmptyStrips(laid).Where(strip => !Unavoidable(laid, strip)))
                    {
                        problems.Add($"{because}: {strip}");
                    }
                }
            }
        }

        output.WriteLine($"{documents} documents ({Leading.Length} kinds of leading space × {Pieces.Length} first pieces × 51 offsets), {pages} pages, "
            + $"{blankPages} pages without text (pages a break leaves behind leading space on, and blank parts of rows), {problems.Count} problems.");
        foreach (var problem in problems.Take(20))
        {
            output.WriteLine(problem);
        }

        problems.Should().BeEmpty();
        documents.Should().Be(Leading.Length * Pieces.Length * 51);
    }

    /// <summary>The kinds of piece taller than the page area (180 pt here: 204 pt pages), each with its words.</summary>
    private static readonly (string Name, Func<Element> Make)[] TallPieces =
    [
        ("a 408 pt image", () => Block([TallImage(), Paragraph("after")])),
        ("a 240 pt line between two lines", () => Paragraph([Text("one "), Text("W").FontSize(200.Pt()), LineBreak(), Text("two")])),
        ("a 240 pt line in a block with a background", () => Block([Paragraph("W").FontSize(200.Pt()), Paragraph("tail")]).Background(Grey)),
        ("a 408 pt image in a cell beside text", () => Lined(Table(Columns(Fill(1), Fill(1)),
            [TableRow([Cell("left"), Cell([TallImage()])]), TableRow([Cell("after"), Cell("z")])]))),
        ("a 240 pt line in a cell under a header row", () => Lined(Table(Columns(Fill(1)),
            [HeaderRow([Cell("head")]), TableRow([Cell([Paragraph("W").FontSize(200.Pt())])]), TableRow([Cell("next")])]))),
    ];

    private static readonly byte[] Tall = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "chromium", "tall.png"));

    private static ImageElement TallImage() => Image(Tall).Alt("tall").Width(102.Pt()).Height(408.Pt());

    [Fact]
    public void Generated_documents_with_content_taller_than_a_page_are_sliced_without_refusal_lost_words_or_overflow()
    {
        // MonolithicOverflow.Slice under both row-part policies: every kind of leading space before every tall piece, at
        // 51 offsets, on 204 pt pages (a 180 pt page area). Every word is on some page exactly once (a later slice is a
        // copy), no slice reaches past the end of the page area, and the slices of each piece show it whole and once:
        // their heights add up to its height, and each shows the piece moved up by the heights of the slices before it.
        var (documents, pages) = (0, 0);
        var problems = new List<string>();
        foreach (var parts in new[] { EmptyRowParts.Avoid, EmptyRowParts.Allow })
        {
            foreach (var (leadingName, leading) in Leading)
            {
                foreach (var (pieceName, piece) in TallPieces)
                {
                    var expected = Paginate([.. leading(0, piece())], TallArea, parts).Value.SelectMany(page => Words(page.Root.Fragment))
                        .OrderBy(word => word, StringComparer.Ordinal).ToList();
                    if (expected.Distinct().Count() != expected.Count || !expected.Contains("W", StringComparer.Ordinal) && !expected.Contains("after", StringComparer.Ordinal))
                    {
                        problems.Add($"{parts}, {leadingName}, {pieceName}: the words without leading space are [{string.Join(" ", expected)}]; every word once");
                    }
                    for (var step = 0; step <= 50; step++)
                    {
                        var offset = step * 3.0;
                        var because = $"{parts}, {leadingName}, {pieceName}, offset {offset}";
                        documents++;
                        var result = Paginate([.. leading(offset, piece())], TallArea, parts);
                        if (result.IsFailure)
                        {
                            problems.Add($"{because}: refused {result.Error.Code}: {result.Error.Message}");
                            continue;
                        }

                        var laid = result.Value;
                        pages += laid.Count;
                        var words = laid.SelectMany(page => Words(page.Root.Fragment)).OrderBy(word => word, StringComparer.Ordinal).ToList();
                        if (!words.SequenceEqual(expected))
                        {
                            problems.Add($"{because}: words [{string.Join(" ", words)}] where [{string.Join(" ", expected)}]");
                        }

                        problems.AddRange(SliceProblems(laid).Select(problem => $"{because}: {problem}"));
                        if (laid.Select(page => Bottom(page.Root.Fragment, 0)).Max() is var over && over > TallAreaHeight + 1e-6)
                        {
                            problems.Add($"{because}: content reaches {over:0.###} pt, past the page area");
                        }
                    }
                }
            }
        }

        output.WriteLine($"{documents} documents (2 row-part policies × {Leading.Length} kinds of leading space × {TallPieces.Length} tall pieces × 51 offsets), "
            + $"{pages} pages, {problems.Count} problems.");
        foreach (var problem in problems.Take(20))
        {
            output.WriteLine(problem);
        }

        problems.Should().BeEmpty();
        documents.Should().Be(2 * Leading.Length * TallPieces.Length * 51);
    }

    /// <summary>Where the slices of the pieces in <paramref name="pages"/> do not show each piece whole and once: a piece's
    /// slices, in page order, from its first slice on.</summary>
    private static IEnumerable<string> SliceProblems(IReadOnlyList<PageFragment> pages)
    {
        var slices = pages.SelectMany(page => Slices(page.Root.Fragment)).ToList();
        var shown = 0.0;
        double? whole = null;
        foreach (var slice in slices)
        {
            if (!slice.IsContinuation)
            {
                if (whole is { } height && Math.Abs(shown - height) > 1e-6)
                {
                    yield return $"slices show {shown:0.###} pt of a {height:0.###} pt piece";
                }

                (shown, whole) = (0, slice.Content.Fragment.Size.Height);
            }

            if (Math.Abs(slice.Content.Offset.Y + shown) > 1e-6)
            {
                yield return $"a slice shows its piece from {-slice.Content.Offset.Y:0.###} pt where {shown:0.###} pt were shown before";
            }

            shown += slice.Size.Height;
        }

        if (whole is { } last && Math.Abs(shown - last) > 1e-6)
        {
            yield return $"slices show {shown:0.###} pt of a {last:0.###} pt piece";
        }
    }

    private static IEnumerable<SliceFragment> Slices(Fragment fragment) => fragment switch
    {
        SliceFragment slice => [slice],
        BoxFragment box => box.Children.SelectMany(child => Slices(child.Fragment)),
        LineFragment line => line.Children.SelectMany(child => Slices(child.Fragment)),
        _ => [],
    };

    private const double TallAreaHeight = 180;

    private static PageSetup TallArea => Page(300.Pt(), 204.Pt()).Margin(12.Pt());

    /// <summary>A part of a row that shows nothing which no layout can avoid: the row shows nothing on any of its pages
    /// (twenty padded empty blocks), or the part fills the page it is on (a blank stretch of a page's height).</summary>
    private static bool Unavoidable(IReadOnlyList<PageFragment> pages, RowParts.EmptyStrip strip) =>
        strip.Height >= AreaHeight - 2 || RowParts.EmptyStrips(pages).Count(other => other.Row == strip.Row) == pages.Count(page => RowOn(page.Root.Fragment, strip.Row));

    private static bool RowOn(Fragment fragment, NodeId row) =>
        fragment is BoxFragment box && (box.Box.Id == row || box.Children.Any(child => RowOn(child.Fragment, row)));

    private static Result<ValueList<PageFragment>, global::Ave.Extensions.ErrorPaths.Error> Paginate(Element[] body) =>
        DocumentTranslator.ToModel(Document([.. body], Area, title: "Title", language: "en").FontFamily("Lato"))
            .OnSuccessBind(model => Paginator.Paginate(LayoutEnvironment.Create(TestFonts.SetWithItalic), model));

    private static Result<ValueList<PageFragment>, global::Ave.Extensions.ErrorPaths.Error> Paginate(Element[] body, PageSetup area, EmptyRowParts parts) =>
        DocumentTranslator.ToModel(Document([.. body], area, title: "Title", language: "en").FontFamily("Lato")
                .MonolithicOverflow(MonolithicOverflow.Slice).EmptyRowParts(parts))
            .OnSuccessBind(model => Paginator.Paginate(LayoutEnvironment.Create(TestFonts.SetWithItalic), model));
}
