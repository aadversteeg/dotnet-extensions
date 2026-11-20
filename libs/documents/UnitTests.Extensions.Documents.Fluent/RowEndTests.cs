using System.Globalization;
using System.IO;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Functional;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// Under <see cref="EmptyRowParts.Allow"/>, in a table that continues from the page before, a unit that starts its page
/// area (a row that continues from the page before, or the first on the page), whose cells' content fits and whose grid
/// line below does not, is placed whole, and that line passes the page end, as Chromium prints it; in a table that
/// starts the page, a first row whose line below passes by its outer half only (<c>TablePage.PassesOnlyByItsLineBelow</c>; the first row of such a
/// unit too, <c>TableBreaks.InsideUnit</c>). Against Chromium's print (<c>tools/reference/chromium</c>, cases
/// <c>a2-row-end*</c>), and with the limits of docs/architecture.md, "Known limits", pinned. Page area 276 × 180 pt, Lato
/// 12 pt, line height 15 pt, 0.75 pt grid lines split at breaks.
/// </summary>
public class RowEndTests
{
    private const double AreaBottom = 192;

    private static PageSetup Area => Page(300.Pt(), 204.Pt()).Margin(12.Pt());

    private static ParagraphElement Lines(string prefix, int count) =>
        Paragraph([.. Enumerable.Range(0, count).SelectMany(i => i == 0 ? new[] { Text($"{prefix}{i}") } : [LineBreak(), Text($"{prefix}{i}")])]);

    private static CellElement Lines(string prefix, int count, int rowSpan)
    {
        var cell = count == 0 ? Cell("") : Cell([Lines(prefix, count)]);
        return rowSpan > 1 ? cell.RowSpan(rowSpan) : cell;
    }

    private static CellElement Empty => Cell("");

    private static TableElement Grid(int columns, params TablePart[] rows) =>
        Table(Columns([.. Enumerable.Range(0, columns).Select(_ => Fill(1))]), [.. rows]).GridLines(0.75.Pt(), Color.Black).SplitLinesAtBreaks();

    private static Result<PageDocument, global::Ave.Extensions.ErrorPaths.Error> Run(
        Element[] body, EmptyRowParts parts, MonolithicOverflow overflow = MonolithicOverflow.Refuse, FontMetricRounding rounding = FontMetricRounding.WholePixels) =>
        DocumentTranslator.ToModel(Document([.. body], Area, title: "Title", language: "en").FontFamily("Lato").LineHeight(15.Pt())
                .EmptyRowParts(parts).MonolithicOverflow(overflow).FontMetricRounding(rounding))
            .OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic));

    private static PrintedPages Printed(Element[] body, MonolithicOverflow overflow = MonolithicOverflow.Refuse)
    {
        var result = Run(body, EmptyRowParts.Allow, overflow);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return PrintedPages.Engine(result.Value);
    }

    /// <summary>The documents around the refusal: tables of one or two rows of k and m lines, alone or after a paragraph;
    /// 2,100 documents, each with its name (the names of <c>a2-row-end-family.tsv</c>).</summary>
    public static IEnumerable<(string Name, Element[] Body)> Family()
    {
        for (var k = 0; k <= 13; k++)
        {
            for (var m = 1; m <= 30; m++)
            {
                yield return ($"one column: row a ({k} lines), row b ({m} lines)",
                    [Grid(1, TableRow([Lines("a", k, 1)]), TableRow([Lines("b", m, 1)]))]);
                yield return ($"two columns: row [empty | {k} lines], row [{m} lines | empty]",
                    [Grid(2, TableRow([Empty, Lines("a", k, 1)]), TableRow([Lines("b", m, 1), Empty]))]);
                yield return ($"two columns: row [{k} lines | {k} lines], row [{m} lines | empty]",
                    [Grid(2, TableRow([Lines("c", k, 1), Lines("a", k, 1)]), TableRow([Lines("b", m, 1), Empty]))]);
                yield return ($"two columns: row [{k} lines | {k} lines], row [{m} lines | {m} lines]",
                    [Grid(2, TableRow([Lines("c", k, 1), Lines("a", k, 1)]), TableRow([Lines("b", m, 1), Lines("d", m, 1)]))]);
                yield return ($"paragraph of {k} lines, then one column: row ({m} lines)",
                    [Lines("a", Math.Max(1, k)), Grid(1, TableRow([Lines("b", m, 1)]))]);
            }
        }
    }

    /// <summary>What passes the bottom of the page area: a glyph's ink, an image, a clip, a filled rectangle. Under Allow a
    /// grid line or a cell background (a filled rectangle) may pass by at most <paramref name="line"/>, the width of the
    /// grid line below the row; anything else, or anything under Avoid, is listed.</summary>
    private static List<string> PastTheArea(PageDocument document, EmptyRowParts parts, double line)
    {
        const double tolerance = 1e-6;
        var past = new List<string>();
        foreach (var (page, index) in document.Pages.Select((page, index) => (page, index + 1)))
        {
            var clips = 0;
            foreach (var item in page.Items)
            {
                switch (item)
                {
                    case ClipStart clip:
                        clips++;
                        if (clip.Area.Origin.Y + clip.Area.Size.Height > AreaBottom + tolerance)
                        {
                            past.Add($"page {index}: a clip to {clip.Area.Origin.Y + clip.Area.Size.Height}");
                        }

                        break;
                    case ClipEnd:
                        clips--;
                        break;
                    case GlyphRun run when clips == 0:
                        var lowest = run.Glyphs.Select(glyph => run.Font.GlyphBounds(glyph.Glyph))
                            .Where(bounds => bounds.HasValue).Select(bounds => bounds.Value.YMin).DefaultIfEmpty(0).Min();
                        var ink = run.Origin.Y - run.Font.Metrics.ToPoints(lowest, run.FontSize);
                        if (ink > AreaBottom + tolerance)
                        {
                            past.Add($"page {index}: glyphs '{string.Concat(run.Glyphs.Select(glyph => glyph.Text))}' to {ink}");
                        }

                        break;
                    case DrawImage image when clips == 0:
                        if (image.Area.Origin.Y + image.Area.Size.Height > AreaBottom + tolerance)
                        {
                            past.Add($"page {index}: an image to {image.Area.Origin.Y + image.Area.Size.Height}");
                        }

                        break;
                    case FillRectangle fill when clips == 0:
                        var bottom = fill.Area.Origin.Y + fill.Area.Size.Height;
                        if (bottom > AreaBottom + (parts == EmptyRowParts.Allow ? line : 0) + tolerance)
                        {
                            past.Add($"page {index}: a rectangle {fill.Area.Size.Width} × {fill.Area.Size.Height} to {bottom}");
                        }

                        break;
                }
            }
        }

        return past;
    }

    private static List<string> Words(PageDocument document) =>
        [.. PrintedPages.Engine(document).Pages.SelectMany(page => page.Words).Select(word => word.Text)];

    [Theory]
    [InlineData(MonolithicOverflow.Refuse)]
    [InlineData(MonolithicOverflow.Slice)]
    public void No_document_of_the_family_is_refused_under_Allow_and_only_a_grid_line_passes_the_page_area(MonolithicOverflow overflow)
    {
        // Before the rule, 95 of these 2,100 documents were refused under Allow and laid out under Avoid. Now none is, and
        // on every page nothing but a grid line passes the page area, by at most its width; under Avoid nothing passes.
        var refused = new List<string>();
        var past = new List<string>();
        foreach (var (name, body) in Family())
        {
            foreach (var parts in Enum.GetValues<EmptyRowParts>())
            {
                var result = Run(body, parts, overflow, FontMetricRounding.Exact);
                if (result.IsFailure)
                {
                    refused.Add($"{name} ({parts}): {result.Error.Code}");
                    continue;
                }

                past.AddRange(PastTheArea(result.Value, parts, 0.75).Select(problem => $"{name} ({parts}): {problem}"));
            }
        }

        refused.Should().BeEmpty();
        past.Should().BeEmpty();
    }

    /// <summary>Chromium's words of the 95 documents, by name (<c>a2-row-end-family.tsv</c>).</summary>
    private static Dictionary<string, List<PrintedWord>> ChromiumFamily()
    {
        var documents = new Dictionary<string, List<PrintedWord>>();
        List<PrintedWord>? words = null;
        foreach (var line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "chromium", "a2-row-end-family.tsv")).Where(line => !line.StartsWith('#')))
        {
            var fields = line.Split('\t');
            if (fields[0] == "doc")
            {
                documents[fields[1]] = words = [];
                continue;
            }

            static double Number(string value) => double.Parse(value, CultureInfo.InvariantCulture);
            words!.Add(new PrintedWord(int.Parse(fields[1], CultureInfo.InvariantCulture), Number(fields[2]), 0, Number(fields[3]), 12, fields[4]));
        }

        return documents;
    }

    [Fact]
    public void The_95_documents_that_were_refused_print_the_words_of_Chromium_on_the_same_pages_and_in_87_at_the_same_places()
    {
        // Same words on the same pages in all 95, at the same places in 87. In the 8 with a first row of 10 or 11 lines and
        // a second of 23 (in the four table shapes), page 2 starts below the line between the rows, which the engine splits at the break and Chromium
        // draws whole on both pages: page 2 is 0.375 pt higher (the pinned difference of RowPartTests, the a2 split-line
        // cases).
        var chromium = ChromiumFamily();
        chromium.Should().HaveCount(95);
        var differences = new List<string>();
        var offsets = new List<string>();
        foreach (var (name, body) in Family().Where(document => chromium.ContainsKey(document.Name)))
        {
            var engine = Printed(body).Pages.SelectMany(page => page.Words).ToList();
            var expected = chromium[name].OrderBy(word => word.Page).ThenBy(word => Math.Round(word.Baseline, 3)).ThenBy(word => Math.Round(word.X0, 3)).ToList();
            if (!engine.Select(word => (word.Page, word.Text)).SequenceEqual(expected.Select(word => (word.Page, word.Text))))
            {
                differences.Add(name);
                continue;
            }

            offsets.AddRange(engine.Zip(expected)
                .Where(pair => Math.Abs(pair.First.X0 - pair.Second.X0) >= 0.01 || Math.Abs(pair.First.Baseline - pair.Second.Baseline) >= 0.01)
                .Select(pair => (pair.First.Page, X: Math.Round(pair.Second.X0 - pair.First.X0, 3), Y: Math.Round(pair.Second.Baseline - pair.First.Baseline, 3)))
                .Distinct()
                .Select(offset => $"{name}: page {offset.Page} {offset.X} {offset.Y}"));
        }

        differences.Should().BeEmpty();
        offsets.Should().Equal(Family().Select(document => document.Name)
            .Where(name => chromium.ContainsKey(name) && !name.StartsWith("paragraph", StringComparison.Ordinal) && (name.Contains("10 lines") || name.Contains("11 lines")) && name.Contains("23 lines"))
            .Select(name => $"{name}: page 2 0 0.375"));
        offsets.Should().HaveCount(8);
    }

    /// <summary>A one-column table that starts the page, whose first row's content is the page area exactly with the grid
    /// line above it and whose line below does not fit, then a row "after": a cell of 11 lines below 14.25 pt top padding
    /// (<c>a2-row-end-first-row</c>), or a cell of 12 lines of 14.9375 pt (<c>a2-row-end-first-row-cut</c>).</summary>
    private static Element[] FirstRow(string caseName) => caseName switch
    {
        "a2-row-end-first-row" => [Grid(1, TableRow([Cell([Lines("x", 11)]).PaddingTop(14.25.Pt())]), TableRow([Cell("after")]))],
        "a2-row-end-first-row-cut" => [Grid(1, TableRow([Cell([Lines("x", 12).LineHeight(14.9375.Pt())])]), TableRow([Cell("after")]))],
        _ => throw new ArgumentException(caseName, nameof(caseName)),
    };

    [Theory]
    [InlineData("a2-row-end-first-row", "", "x0 x1 x2 x3 x4 x5 x6 x7 x8 x9 x10", "after")]
    [InlineData("a2-row-end-first-row-cut", "x0 x1 x2 x3 x4 x5 x6 x7 x8 x9", "x10 x11 after")]
    public void A_table_that_starts_the_page_with_a_first_row_that_fits_but_for_its_line_below_is_refused_under_Allow(string caseName, params string[] chromiumPages)
    {
        // Limit, docs/architecture.md: in a table that starts the page the rule places a first row whose line below passes
        // the page end by its outer half only (the test below). Where the content ends at the page end, Chromium leaves the
        // first page empty and prints the row whole on the next, or, where the 12 lines in its unit of 1/64 pixel are a hair
        // taller than the page area, cuts the row before its last two lines. The engine prints neither and refuses the
        // document under Allow; under Avoid it cuts the row as before.
        foreach (var overflow in Enum.GetValues<MonolithicOverflow>())
        {
            var result = Run(FirstRow(caseName), EmptyRowParts.Allow, overflow);
            result.IsFailure.Should().BeTrue($"{overflow}: {(result.IsSuccess ? string.Join(" | ", PrintedPages.Engine(result.Value).PageTexts) : string.Empty)}");
            result.Error.Code.Should().Be(LayoutErrorCodes.DoesNotFit);
            Run(FirstRow(caseName), EmptyRowParts.Avoid, overflow).IsSuccess.Should().BeTrue(overflow.ToString());
        }

        PrintedPages.Chromium(caseName).PageTexts.Should().Equal(chromiumPages);
    }

    /// <summary>A one-row table whose cell has 11 lines below 13.875 pt top padding, so that the row up to the middle of
    /// its line below is the page area exactly and only the outer half of that line passes the page end, in the place
    /// <paramref name="caseName"/> names, then a paragraph "below" (cases <c>a2-row-end-first-row-half-*</c>); in
    /// <c>-second-row</c> a row "after" follows instead, in <c>-alone</c> nothing.</summary>
    private static Element[] HalfLine(string caseName)
    {
        TableElement Table(params CellElement[] cells) => Grid(cells.Length + 1, TableRow([Cell([Lines("x", 11)]).PaddingTop(13.875.Pt()), .. cells]));
        return caseName switch
        {
            "a2-row-end-first-row-half-top" => [Table(), Paragraph("below")],
            "a2-row-end-first-row-half-block" => [Block([Table()]), Paragraph("below")],
            "a2-row-end-first-row-half-emptyblock" => [Block([]), Table(), Paragraph("below")],
            "a2-row-end-first-row-half-break" => [Paragraph("i0"), Block([]), PageBreak(), Table(), Paragraph("below")],
            "a2-row-end-first-row-half-page2" => [Lines("i", 12), Table(), Paragraph("below")],
            "a2-row-end-first-row-half-2col" => [Table(Cell("b")), Paragraph("below")],
            "a2-row-end-first-row-half-second-row" => [Grid(1, TableRow([Cell([Lines("x", 11)]).PaddingTop(13.875.Pt())]), TableRow([Cell("after")]))],
            "a2-row-end-first-row-half-alone" => [Table()],
            _ => throw new ArgumentException(caseName, nameof(caseName)),
        };
    }

    [Theory]
    [InlineData("a2-row-end-first-row-half-top")]
    [InlineData("a2-row-end-first-row-half-block")]
    [InlineData("a2-row-end-first-row-half-emptyblock")]
    [InlineData("a2-row-end-first-row-half-break")]
    [InlineData("a2-row-end-first-row-half-page2")]
    [InlineData("a2-row-end-first-row-half-2col")]
    [InlineData("a2-row-end-first-row-half-second-row")]
    public void A_table_that_starts_the_page_places_a_first_row_whole_whose_line_below_passes_the_page_end_by_its_outer_half_under_Allow_as_in_Chromium(string caseName)
    {
        // The row's content ends 0.375 pt above the page end and the middle of its line below is the page end. Chromium
        // places the row whole on the page and lets the outer half of the line pass; what follows starts the next page.
        // The engine does the same under Allow, in a table that starts the page wherever it stands; a first row whose
        // content ends at the page end, or cut a hair lower, is still refused (the test above). With a second row the page
        // breaks between the two rows, where the line is split and its upper half fits, so that shape had Chromium's pages
        // without the rule as well.
        var chromium = PrintedPages.Chromium(caseName);

        chromium.PageTexts[^1].Should().Be(caseName.EndsWith("second-row", StringComparison.Ordinal) ? "after" : "below");
        foreach (var overflow in Enum.GetValues<MonolithicOverflow>())
        {
            Printed(HalfLine(caseName), overflow).PageTexts.Should().Equal(chromium.PageTexts, overflow.ToString());
        }
    }

    [Fact]
    public void A_table_that_starts_the_page_and_ends_the_document_with_such_a_first_row_has_a_page_less_than_in_Chromium()
    {
        // Limit, docs/architecture.md, row end (i): the same row with nothing after the table. Chromium ends the document
        // with a second page that shows nothing; the engine has the one page.
        PrintedPages.Chromium("a2-row-end-first-row-half-alone").PageTexts.Should().Equal("x0 x1 x2 x3 x4 x5 x6 x7 x8 x9 x10", "");
        foreach (var overflow in Enum.GetValues<MonolithicOverflow>())
        {
            Printed(HalfLine("a2-row-end-first-row-half-alone"), overflow).PageTexts.Should().Equal(["x0 x1 x2 x3 x4 x5 x6 x7 x8 x9 x10"], overflow.ToString());
        }
    }

    [Fact]
    public void A_table_below_a_paragraph_whose_first_row_fits_but_for_its_line_below_moves_to_the_next_page_as_in_Chromium()
    {
        // "i0", then the table: its first row's 10 lines below 14.25 pt top padding end at the page end, and the line below
        // the row does not fit. The table does not start the page and does not continue, so the rule does not place the row
        // there: the table moves to page 2 and "after" to page 3, under every profile.
        Element[] body = [Paragraph("i0"), Grid(1, TableRow([Cell([Lines("x", 10)]).PaddingTop(14.25.Pt())]), TableRow([Cell("after")]))];
        var chromium = PrintedPages.Chromium("a2-row-end-below-paragraph");
        foreach (var overflow in Enum.GetValues<MonolithicOverflow>())
        {
            foreach (var parts in Enum.GetValues<EmptyRowParts>())
            {
                var result = Run(body, parts, overflow);
                result.IsSuccess.Should().BeTrue($"{overflow}/{parts}: {(result.IsFailure ? result.Error.ToString() : string.Empty)}");
                PrintedPages.Engine(result.Value).PageTexts.Should().Equal(chromium.PageTexts, $"{overflow}/{parts}");
            }
        }

        chromium.PageTexts.Should().Equal("i0", "x0 x1 x2 x3 x4 x5 x6 x7 x8 x9", "after");
    }

    [Fact]
    public void Under_an_outer_table_s_repeated_header_a_continued_row_that_fits_but_for_its_line_below_is_not_placed_whole_there()
    {
        // A table inside the one body cell of a table with a header row "H": its row of 10 lines fills page 1, and its
        // second row, 10 lines below 14.25 pt top padding, starts page 2 below the repeated header. There the inner table
        // continues but does not start the page, so the rule does not place the row whole below the header. Under Avoid
        // the row is cut before "x8", as Chromium prints it. Under Allow the cut below the header is not taken: page 2
        // goes on without the header, where the rule places the row whole (docs/architecture.md, "Known limits"); Chromium
        // repeats the header and cuts the row.
        Element[] body =
        [
            Grid(1, HeaderRow([Cell("H")]), TableRow([Cell([Grid(1,
                TableRow([Cell([Lines("a", 10)])]),
                TableRow([Cell([Lines("x", 10)]).PaddingTop(14.25.Pt())]),
                TableRow([Cell("after")]))])])),
        ];
        var chromium = PrintedPages.Chromium("a2-row-end-nested-under-header");
        string[] withoutHeader = ["H a0 a1 a2 a3 a4 a5 a6 a7 a8 a9", "x0 x1 x2 x3 x4 x5 x6 x7 x8 x9", "H after"];
        foreach (var overflow in Enum.GetValues<MonolithicOverflow>())
        {
            foreach (var parts in Enum.GetValues<EmptyRowParts>())
            {
                var result = Run(body, parts, overflow);
                result.IsSuccess.Should().BeTrue($"{overflow}/{parts}: {(result.IsFailure ? result.Error.ToString() : string.Empty)}");
                PrintedPages.Engine(result.Value).PageTexts.Should().Equal(parts == EmptyRowParts.Avoid ? chromium.PageTexts : withoutHeader, $"{overflow}/{parts}");
            }
        }

        chromium.PageTexts.Should().Equal("H a0 a1 a2 a3 a4 a5 a6 a7 a8 a9", "H x0 x1 x2 x3 x4 x5 x6 x7", "H x8 x9 after");
    }

    /// <summary>The shape of <c>a2-row-end-nested-under-header</c> with <paramref name="lines"/> lines below
    /// <paramref name="padding"/> of top padding in the inner table's second row.</summary>
    private static Element[] NestedUnderHeader(int lines, double padding) =>
    [
        Grid(1, HeaderRow([Cell("H")]), TableRow([Cell([Grid(1,
            TableRow([Cell([Lines("a", 10)])]),
            TableRow([Cell([Lines("x", lines)]).PaddingTop(padding.Pt())]),
            TableRow([Cell("after")]))])])),
    ];

    [Fact]
    public void Under_an_outer_table_s_repeated_header_a_table_whose_rest_fits_but_for_the_outer_table_s_bottom_line_goes_on_without_the_header()
    {
        // The shape above with 9 lines below 12 pt top padding: on page 2 the rest of the inner table, "x0" to "after",
        // fits below the repeated header "H" but for the outer table's bottom line, which would pass the page end.
        // Chromium repeats the header and lets the line pass (it draws the line again on a page 3 with nothing else). The
        // engine goes on without the header there, under every profile (docs/architecture.md, "Known limits").
        var chromium = PrintedPages.Chromium("a2-row-end-nested-under-header-end");
        foreach (var overflow in Enum.GetValues<MonolithicOverflow>())
        {
            foreach (var parts in Enum.GetValues<EmptyRowParts>())
            {
                var result = Run(NestedUnderHeader(9, 12), parts, overflow);
                result.IsSuccess.Should().BeTrue($"{overflow}/{parts}: {(result.IsFailure ? result.Error.ToString() : string.Empty)}");
                PrintedPages.Engine(result.Value).PageTexts.Should().Equal(["H a0 a1 a2 a3 a4 a5 a6 a7 a8 a9", "x0 x1 x2 x3 x4 x5 x6 x7 x8 after"], $"{overflow}/{parts}");
            }
        }

        chromium.PageTexts.Should().Equal("H a0 a1 a2 a3 a4 a5 a6 a7 a8 a9", "H x0 x1 x2 x3 x4 x5 x6 x7 x8 after", "");
    }

    [Fact]
    public void Under_an_outer_table_s_repeated_header_a_continued_row_below_13_875_pt_of_padding_is_cut_as_in_Chromium_under_Avoid_only()
    {
        // The shape above with 10 lines below 13.875 pt top padding. Under Avoid the row is cut below the repeated header
        // as Chromium cuts it. Under Allow it is refused under Refuse, and under Slice page 2 goes on without the header,
        // as in a2-row-end-nested-under-header (docs/architecture.md, "Known limits").
        var chromium = PrintedPages.Chromium("a2-row-end-nested-under-header-13875");
        foreach (var overflow in Enum.GetValues<MonolithicOverflow>())
        {
            Run(NestedUnderHeader(10, 13.875), EmptyRowParts.Avoid, overflow).OnSuccessMap(PrintedPages.Engine).Value.PageTexts.Should().Equal(chromium.PageTexts, $"{overflow}/Avoid");
        }

        Run(NestedUnderHeader(10, 13.875), EmptyRowParts.Allow, MonolithicOverflow.Refuse).Error.Code.Should().Be(LayoutErrorCodes.DoesNotFit);
        Run(NestedUnderHeader(10, 13.875), EmptyRowParts.Allow, MonolithicOverflow.Slice).OnSuccessMap(PrintedPages.Engine).Value.PageTexts
            .Should().Equal("H a0 a1 a2 a3 a4 a5 a6 a7 a8 a9", "x0 x1 x2 x3 x4 x5 x6 x7 x8 x9", "H after");
        chromium.PageTexts.Should().Equal("H a0 a1 a2 a3 a4 a5 a6 a7 a8 a9", "H x0 x1 x2 x3 x4 x5 x6 x7", "H x8 x9 after");
    }

    /// <summary>The smallest of the 95: a paragraph, then a one-column table of a row of 22 lines; the row continues on
    /// page 2 with 12 lines, which fill the page area, and the line below it passes the page end.</summary>
    private static Element[] Smallest(bool after = false) =>
        [Lines("a", 1), Grid(1, TableRow([Lines("b", 22, 1)])), .. after ? new Element[] { Paragraph("after") } : []];

    [Theory]
    [InlineData(MonolithicOverflow.Refuse)]
    [InlineData(MonolithicOverflow.Slice)]
    public void A_continued_row_whose_words_fill_the_page_area_is_placed_whole_with_Chromium_s_words_on_every_page(MonolithicOverflow overflow)
    {
        var engine = Printed(Smallest(), overflow);
        var chromium = PrintedPages.Chromium("a2-row-end");

        engine.PageTexts.Should().Equal(chromium.PageTexts.Where(text => text.Length > 0));
        engine.Pages.SelectMany(page => page.Words).Select(word => (word.Page, word.Text, word.Baseline))
            .Should().Equal(chromium.Pages.SelectMany(page => page.Words).Select(word => (word.Page, word.Text, word.Baseline)));
        engine.Pages[1].Words.Select(word => word.Text).Should().Equal(Enumerable.Range(10, 12).Select(i => $"b{i}"));
    }

    [Fact]
    public void The_line_below_passes_the_page_area_whole_as_in_Chromium_but_the_engine_does_not_draw_it_again_on_a_next_page()
    {
        // Limit (i), docs/architecture.md: the whole bottom line, 192 to 192.75, on page 2 in both. Chromium draws the
        // overflowing border again on a page 3 that holds only that line; the table ends the document, and the engine
        // ends with page 2.
        var engine = Printed(Smallest());
        var chromium = PrintedPages.Chromium("a2-row-end");

        engine.Pages[1].Rectangles.Max(box => box.Y1).Should().Be(192.75);
        chromium.Pages[1].Rectangles.Max(box => box.Y1).Should().Be(192.75);
        chromium.Pages.Should().HaveCount(3);
        chromium.Pages[2].Words.Should().BeEmpty();
        chromium.Pages[2].Rectangles.Should().NotBeEmpty();
        engine.Pages.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("a2-row-end-after", 0.75)]
    [InlineData("a2-row-end-6pt-after", 6)]
    public void Content_after_the_table_starts_on_the_next_page_higher_than_in_Chromium_by_the_line_s_width(string caseName, double width)
    {
        // Limit (ii): Chromium starts "after" below the line it draws again at the top of page 3; the engine starts it at
        // the top of page 3, higher by the line's width. No reserved offset without the line (a gap without a line would
        // be a third behaviour); both become equal when borders are sliced.
        var body = Smallest(after: true);
        if (width > 1)
        {
            body = [Lines("a", 1), Table(Columns(Fill(1)), [TableRow([Lines("b", 22, 1)])]).GridLines(6.Pt(), Color.Black).SplitLinesAtBreaks(), Paragraph("after")];
        }

        var engine = Printed(body);
        var chromium = PrintedPages.Chromium(caseName);
        double Baseline(PrintedPages pages) => pages.Pages.SelectMany(page => page.Words).Single(word => word.Text == "after").Baseline;
        int PageOf(PrintedPages pages) => pages.Pages.SelectMany(page => page.Words).Single(word => word.Text == "after").Page;

        PageOf(chromium).Should().Be(3);
        PageOf(engine).Should().Be(3);
        Baseline(chromium).Should().Be(24 + width);
        Baseline(engine).Should().Be(24);
        engine.PageTexts.Should().Equal(chromium.PageTexts);
    }

    [Fact]
    public void With_a_6_pt_line_the_whole_line_passes_the_page_area_as_in_Chromium()
    {
        var body = new Element[] { Lines("a", 1), Table(Columns(Fill(1)), [TableRow([Lines("b", 22, 1)])]).GridLines(6.Pt(), Color.Black).SplitLinesAtBreaks() };
        var result = Run(body, EmptyRowParts.Allow);
        var engine = PrintedPages.Engine(result.Value);
        var chromium = PrintedPages.Chromium("a2-row-end-6pt");

        engine.PageTexts.Should().Equal(chromium.PageTexts.Where(text => text.Length > 0));
        engine.Pages[1].Rectangles.Max(box => box.Y1).Should().Be(198);
        chromium.Pages[1].Rectangles.Max(box => box.Y1).Should().Be(198);
        PastTheArea(result.Value, EmptyRowParts.Allow, 6).Should().BeEmpty();
        PastTheArea(result.Value, EmptyRowParts.Allow, 3).Should().NotBeEmpty("the whole line passes, not half of it");
    }

    [Fact]
    public void A_cell_background_ends_at_the_middle_of_the_line_below_where_Chromium_ends_it_at_its_outer_edge()
    {
        // The table model's own rule, not new here: a cell's box ends at the middle of its grid lines. The 0.375 pt between
        // is under the opaque line (the engine has no dashed or translucent lines), so the pages look the same.
        var light = new Color(0xc8, 0xdc, 0xff);
        var body = new Element[] { Lines("a", 1), Table(Columns(Fill(1)), [TableRow([Cell([Lines("b", 22)]).Background(light)])]).GridLines(0.75.Pt(), Color.Black).SplitLinesAtBreaks() };
        var result = Run(body, EmptyRowParts.Allow);
        var background = result.Value.Pages[1].Items.OfType<FillRectangle>().Where(fill => fill.Color == light).ToList();
        var chromium = PrintedPages.Chromium("a2-row-end-background");

        background.Max(fill => fill.Area.Origin.Y + fill.Area.Size.Height).Should().Be(192.375);
        chromium.Pages[1].Rectangles.Where(box => box.X1 - box.X0 > 200).Max(box => box.Y1).Should().Be(192.75);
        PrintedPages.Engine(result.Value).PageTexts.Should().Equal(chromium.PageTexts.Where(text => text.Length > 0));
        PastTheArea(result.Value, EmptyRowParts.Allow, 0.75).Should().BeEmpty();
    }

    /// <summary>The smallest form of seed 255 of the sweep: a unit of two rows, a cell that spans both beside a row of 23
    /// lines, then an empty row. The first row continues on page 2 with 12 lines, which fill the page area; the empty row
    /// goes on on page 3.</summary>
    private static Element[] Spanning(bool after = false) =>
        [Grid(2, TableRow([Lines("x", 0, 2), Lines("b", 23, 1)]), TableRow([Empty])), .. after ? new Element[] { Paragraph("after") } : []];

    [Fact]
    public void The_first_row_of_a_unit_that_starts_the_page_area_is_placed_whole_and_the_page_breaks_before_the_next_row()
    {
        // Chromium's words on all 3 pages. Limit (iii): at this break between rows the engine draws the upper half of the
        // line on page 2 (192 to 192.375) and nothing at the top of page 3, where the cell that spans both rows continues
        // (as Chromium prints a break inside a spanning cell, case b4-row-span-6pt), keeping the half's room: the first
        // line on page 3 is the table's bottom line, 12.375 to 13.125. Chromium lets the whole line pass the end of page 2
        // and draws it again at the top of page 3, so "after" is 0.375 pt higher than in Chromium (+1.125 below the top of
        // the page against +1.5).
        var engine = Printed(Spanning());
        var chromium = PrintedPages.Chromium("a2-row-end-span");
        var engineAfter = Printed(Spanning(after: true));
        var chromiumAfter = PrintedPages.Chromium("a2-row-end-span-after");
        double After(PrintedPages pages) => pages.Pages[2].Words.Single(word => word.Text == "after").Baseline;

        engine.PageTexts.Should().Equal(chromium.PageTexts);
        engine.Pages[1].Words.Select(word => word.Text).Should().Equal(Enumerable.Range(11, 12).Select(i => $"b{i}"));
        engine.Pages[1].Rectangles.Max(box => box.Y1).Should().Be(192.375);
        engine.Pages[2].Rectangles.Where(box => box.X1 - box.X0 > 100).Min(box => box.Y0).Should().Be(12.375);
        engine.Pages[2].Rectangles.Max(box => box.Y1).Should().Be(13.125);
        chromium.Pages[1].Rectangles.Max(box => box.Y1).Should().Be(192.75);
        After(engineAfter).Should().Be(24 + 1.125);
        After(chromiumAfter).Should().Be(24 + 1.5);
    }

    /// <summary>Seeds 160, 245 and 255 of the sweep of generated documents (mode 0), which Allow refused or, under Slice,
    /// lost labels. Under Refuse and Avoid, 245 and 255 are refused (a listed limit, see
    /// <see cref="A_continued_row_whose_lines_fill_the_next_page_is_refused_under_Avoid"/>).</summary>
    public static TheoryData<int> Seeds => [160, 245, 255];

    private static Element[] Seed(int seed)
    {
        ParagraphElement T(int from, int to) => Paragraph([.. Enumerable.Range(from, to - from + 1).SelectMany(i => i == from ? new[] { Text($"t{i}") } : [LineBreak(), Text($"t{i}")])]);
        BlockElement Pad() => Block([]).PaddingTop(5.Pt()).PaddingBottom(5.Pt());
        Element[] Pads(int count) => [.. Enumerable.Range(0, count).Select(_ => (Element)Pad())];
        TableElement G(int columns, params TablePart[] rows) => Grid(columns, rows);
        return seed switch
        {
            160 => [
                Block([T(0, 3)]).PaddingTop(63.Pt()).PaddingBottom(12.Pt()),
                G(1,
                    TableRow([Cell([T(4, 5)])]),
                    TableRow([Cell([G(1,
                        TableRow([Cell([T(6, 7)]).PaddingTop(9.Pt())]),
                        TableRow([Cell([T(8, 15)])]),
                        TableRow([Cell([.. Pads(10)])]),
                        TableRow([Cell([T(16, 20)])]),
                        TableRow([Cell([.. Pads(9)])])).CellPadding(3.Pt())])]),
                    TableRow([Cell([T(21, 26)])]),
                    TableRow([Cell([G(1,
                        TableRow([Cell([T(27, 32)])]),
                        TableRow([Cell([T(33, 34)])]),
                        TableRow([Cell([T(35, 44)]).PaddingBottom(30.Pt())]),
                        TableRow([Cell([T(45, 58)]).PaddingBottom(18.Pt())]),
                        TableRow([Cell([T(59, 67)])]))])])),
                T(68, 68),
            ],
            245 => [
                G(2,
                    TableRow([Cell([T(0, 4)]), Cell([T(5, 15)]).PaddingTop(30.Pt())]),
                    TableRow([Cell([T(16, 28)]).RowSpan(2), Cell([T(29, 30)])]),
                    TableRow([Cell("")]),
                    TableRow([Cell(""), Cell([T(31, 35)])]),
                    TableRow([Cell([T(36, 49)]), Cell([T(50, 53)])]),
                    TableRow([Cell([T(54, 58)]), Cell([T(59, 60)])])),
                T(61, 67), T(68, 74), T(75, 75),
            ],
            _ => [
                G(2,
                    TableRow([Cell([T(0, 2)]), Cell([T(3, 7)])]),
                    TableRow([Cell([T(8, 8)]).PaddingTop(18.Pt()).PaddingBottom(24.Pt()), Cell("")])),
                T(9, 15),
                G(2,
                    TableRow([Cell([T(16, 27)]), Cell([T(28, 33)])]),
                    TableRow([Cell([T(34, 36)]).RowSpan(2), Cell("")]),
                    TableRow([Cell([T(37, 40)])]),
                    TableRow([Cell([T(41, 41)]).RowSpan(2), Cell([T(42, 55)])]),
                    TableRow([Cell("")]),
                    TableRow([Cell([.. Pads(7)]), Cell([T(56, 59)])]),
                    TableRow([Cell([T(60, 67)]).RowSpan(2), Cell([T(68, 75)])]),
                    TableRow([Cell([.. Pads(8)])])),
                Block([T(76, 76)]).PaddingTop(72.Pt()).PaddingBottom(21.Pt()),
                T(77, 77),
            ],
        };
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Seeds_of_the_sweep_lay_out_under_Allow_with_every_label_once_and_only_grid_lines_past_the_page_area(int seed)
    {
        var labels = Enumerable.Range(0, seed switch { 160 => 69, 245 => 76, _ => 78 }).Select(i => $"t{i}").ToList();
        foreach (var overflow in Enum.GetValues<MonolithicOverflow>())
        {
            foreach (var parts in Enum.GetValues<EmptyRowParts>())
            {
                var result = Run(Seed(seed), parts, overflow, FontMetricRounding.Exact);
                if (parts == EmptyRowParts.Avoid && overflow == MonolithicOverflow.Refuse && seed != 160)
                {
                    // Refused, not lost: the shape of A_continued_row_whose_lines_fill_the_next_page_is_refused_under_Avoid.
                    result.IsFailure.Should().BeTrue($"{overflow}/{parts}");
                    result.Error.Code.Should().Be(LayoutErrorCodes.DoesNotFit);
                    continue;
                }

                result.IsSuccess.Should().BeTrue($"{overflow}/{parts}: {(result.IsFailure ? result.Error.ToString() : string.Empty)}");
                Words(result.Value).Order(StringComparer.Ordinal).Should().Equal(labels.Order(StringComparer.Ordinal), $"{overflow}/{parts}");
                PastTheArea(result.Value, parts, 0.75).Should().BeEmpty($"{overflow}/{parts}");
            }
        }
    }

    [Fact]
    public void A_continued_row_whose_lines_fill_the_next_page_is_refused_under_Avoid()
    {
        // A cell of 23 lines, then a second row: page 1 holds 11 lines, and the rest of the row, 12 lines, fills the page
        // area of page 2 exactly, with only the half of the grid line below it past the page end. Under Allow the row ends
        // there and its line passes the page end. Under Avoid and Refuse, the default, the document is refused
        // (docs/architecture.md, "Known limits"); under Avoid and Slice the rest is cut at the deepest place, before its
        // last two lines. Without the second row it is cut so under Refuse too.
        Element[] body = [Grid(1, TableRow([Lines("x", 23, 1)]), TableRow([Empty]))];
        string[] cut = ["x0 x1 x2 x3 x4 x5 x6 x7 x8 x9 x10", "x11 x12 x13 x14 x15 x16 x17 x18 x19 x20", "x21 x22"];
        string[] whole = ["x0 x1 x2 x3 x4 x5 x6 x7 x8 x9 x10", "x11 x12 x13 x14 x15 x16 x17 x18 x19 x20 x21 x22", ""];
        string[] Pages(Element[] document, EmptyRowParts parts, MonolithicOverflow overflow)
        {
            var result = Run(document, parts, overflow, FontMetricRounding.Exact);
            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
            return [.. PrintedPages.Engine(result.Value).PageTexts];
        }

        var refused = Run(body, EmptyRowParts.Avoid, MonolithicOverflow.Refuse, FontMetricRounding.Exact);
        refused.IsFailure.Should().BeTrue();
        refused.Error.Code.Should().Be(LayoutErrorCodes.DoesNotFit);
        Pages(body, EmptyRowParts.Avoid, MonolithicOverflow.Slice).Should().Equal(cut);
        Pages(body, EmptyRowParts.Allow, MonolithicOverflow.Refuse).Should().Equal(whole);
        Pages(body, EmptyRowParts.Allow, MonolithicOverflow.Slice).Should().Equal(whole);
        Pages([Grid(1, TableRow([Lines("x", 23, 1)]))], EmptyRowParts.Avoid, MonolithicOverflow.Refuse).Should().Equal(cut);
    }
}
