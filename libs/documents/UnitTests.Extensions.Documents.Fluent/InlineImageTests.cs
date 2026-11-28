using System.Collections.Immutable;
using System.IO;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Functional;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// An image in a line of text (an atomic inline, CSS Display 3, Appendix A) as Chromium lays it out: its margin box on the
/// baseline (CSS 2.1 §10.8, §10.8.1), a break opportunity before and after it whatever is beside it (CSS Text 3 §5.5), no
/// expansion opportunity, and its outer width in the paragraph's intrinsic widths. Cases <c>ii-*</c>: Lato 12 pt on 18 pt
/// lines, the same documents written with the code front-end; the images keep their aspect ratio.
/// </summary>
/// <remarks>
/// THE COMPARISON. Each word starts where Chromium prints it within one layout unit for each item before it on its line
/// (a text run or an image: Chromium ceils a text item's width and truncates an image's length to a layout unit, README.md,
/// "The comparison rule") plus one for the print's rounding; each baseline within one layout unit for each line above it on
/// the page. Chromium paints an image with its edges rounded to whole pixels (its paint rectangle is snapped), so each edge
/// of an image is compared within half a pixel (0.375 pt) and the same layout units as a word at its place.
/// </remarks>
public class InlineImageTests
{
    private static byte[] Figure => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "chromium", "figure.png"));

    private static byte[] Wide => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "chromium", "wide.png"));

    /// <summary>The 7 × 10 px figure at a width (its height follows from the ratio).</summary>
    private static ImageElement Fig(double width) => Image(Figure).Alt("figure").Width(width.Pt());

    private static ImageElement Wid(double width) => Image(Wide).Alt("figure").Width(width.Pt());

    private static Span In(ImageElement image) => InlineImage(image);

    private static PageSetup Area(double height) => Page(300.Pt(), height.Pt()).Margin(12.Pt());

    private static PrintedPages Engine(double pageHeight, params Element[] body)
    {
        var printed = Laid(pageHeight, body).OnSuccessMap(PrintedPages.Engine);
        printed.IsSuccess.Should().BeTrue(printed.IsFailure ? printed.Error.ToString() : string.Empty);
        return printed.Value;
    }

    private static Result<PageDocument, Ave.Extensions.ErrorPaths.Error> Laid(double pageHeight, params Element[] body) =>
        DocumentTranslator.ToModel(Document([.. body], Area(pageHeight), title: "Title", language: "en").FontFamily("Lato").FontSize(12.Pt()).LineHeight(18.Pt())
                .FontMetricRounding(FontMetricRounding.WholePixels))
            .OnSuccessBind(model => DocumentLayout.Run(model, new FontSet([TestFonts.Regular, TestFonts.Bold])));

    /// <summary>A paragraph whose content box is <paramref name="width"/> wide on the 276 pt page area.</summary>
    private static ParagraphElement Narrow(double width, ValueList<Span> spans) => Paragraph(spans).PaddingRight((276 - width).Pt());

    /// <summary>The breaches of THE COMPARISON on every page; the words' texts and the image count must be equal. A word
    /// may lie <paramref name="extraUnits"/> layout units further (a caller's reason).</summary>
    internal static List<string> Breaches(PrintedPages engine, PrintedPages chromium, bool halfPixelBaselines, double extraUnits = 0)
    {
        engine.Pages.Should().HaveCount(chromium.Pages.Length);
        var breaches = new List<string>();
        for (var page = 0; page < engine.Pages.Length; page++)
        {
            var (mine, theirs) = (engine.Pages[page], chromium.Pages[page]);
            mine.Words.Select(word => word.Text).Should().Equal(theirs.Words.Select(word => word.Text), $"page {page + 1}");
            mine.Images.Should().HaveCount(theirs.Images.Length, $"page {page + 1}");
            var baselines = mine.Words.Select(word => Math.Round(word.Baseline, 3)).Concat(mine.Images.Select(image => Math.Round(image.Y1, 3))).Distinct().Order().ToList();
            int LinesAbove(double y) => baselines.Count(baseline => baseline < y - 0.01);

            // The items before a place on a line: the words (each one text item, or two around a space) and images left of it.
            int ItemsBefore(double x, double baseline) =>
                mine.Words.Count(word => Math.Abs(word.Baseline - baseline) < 0.01 && word.X1 <= x + 0.01) * 2
                + mine.Images.Count(image => image.X1 <= x + 0.01 && image.Y1 <= baseline + 0.01 && image.Y1 >= baseline - 60);
            for (var index = 0; index < mine.Words.Length; index++)
            {
                var (word, measured) = (mine.Words[index], theirs.Words[index]);
                var x = ChromiumLayout.DifferenceInLayoutUnits(word.X0, measured.X0);
                var y = ChromiumLayout.DifferenceInLayoutUnits(word.Baseline, measured.Baseline);
                var baselineBound = LinesAbove(word.Baseline) + 1 + (halfPixelBaselines ? 0.375 / ChromiumLayout.LayoutUnit : 0);
                if (x > ItemsBefore(word.X0, word.Baseline) + 1 + extraUnits || y > baselineBound)
                {
                    breaches.Add($"page {page + 1} word '{word.Text}': x {x:0.00} LU (engine {word.X0:0.000}, Chromium {measured.X0:0.000}), baseline {y:0.00} LU (engine {word.Baseline:0.000}, Chromium {measured.Baseline:0.000})");
                }
            }

            var (images, measuredImages) = (mine.Images.OrderBy(image => image.Y1).ThenBy(image => image.X0).ToList(), theirs.Images.OrderBy(image => image.Y1).ThenBy(image => image.X0).ToList());
            for (var index = 0; index < images.Count; index++)
            {
                var (image, measured) = (images[index], measuredImages[index]);
                var bound = 0.375 + (ItemsBefore(image.X0, image.Y1) + LinesAbove(image.Y1) + 2) * ChromiumLayout.LayoutUnit;
                var edges = new[] { (image.X0, measured.X0), (image.X1, measured.X1), (image.Y0, measured.Y0), (image.Y1, measured.Y1) };
                if (edges.Any(edge => Math.Abs(edge.Item1 - edge.Item2) > bound))
                {
                    breaches.Add($"page {page + 1} image {index}: engine {image.X0:0.000},{image.Y0:0.000}–{image.X1:0.000},{image.Y1:0.000}, Chromium {measured.X0:0.000},{measured.Y0:0.000}–{measured.X1:0.000},{measured.Y1:0.000}");
                }
            }
        }

        return breaches;
    }

    /// <param name="engine">The engine's pages.</param>
    /// <param name="caseName">The case.</param>
    /// <param name="halfPixelBaselines">True when a line above is not a whole number of pixels high: Chromium's print puts
    /// the text of a line at its line box's top rounded to a whole pixel (case <c>ii-height</c>: the text of each line lies
    /// that far from where the image's bottom edge, its own rounding aside, puts the baseline), so a baseline is compared
    /// within half a pixel more.</param>
    private static void ShouldMatch(PrintedPages engine, string caseName, bool halfPixelBaselines = false)
    {
        var breaches = Breaches(engine, PrintedPages.Chromium(caseName), halfPixelBaselines);
        breaches.Should().BeEmpty(string.Join("\n", breaches));
    }

    [Fact]
    public void An_image_sits_on_the_baseline_with_its_margin_box_like_Chromium()
    {
        ShouldMatch(Engine(420,
            Paragraph([Text("ab "), In(Fig(8.4)), Text(" cd")]),
            Paragraph([Text("ab "), In(Fig(21)), Text(" cd")]),
            Paragraph([In(Fig(21))]),
            Paragraph([In(Fig(2.1))]),
            Paragraph([Text("ab"), In(Fig(8.4)), Text("cd")]),
            Paragraph([Text("ab "), In(Fig(21).MarginLeft(6.Pt()).MarginRight(6.Pt())), Text(" cd")]),
            Paragraph([Text("ab "), In(Fig(21).MarginTop(6.Pt()).MarginBottom(3.Pt())), Text(" cd")]),
            Paragraph([Text("ab "), In(Fig(21).Padding(3.Pt())), Text(" cd")]),
            Paragraph([Text("ab "), In(Fig(21).Border(1.5.Pt(), Color.Black)), Text(" cd")]),
            Paragraph("x")), "ii-baseline");
    }

    [Fact]
    public void A_line_may_break_before_and_after_an_image_whatever_is_beside_it_like_Chromium()
    {
        ShouldMatch(Engine(600,
            Narrow(6, [Text("abc"), In(Fig(8.4)), Text("def")]),
            Narrow(6, [Text("abc "), In(Fig(8.4)), Text(" def")]),
            Narrow(6, [Text("abc "), In(Fig(8.4)), Text(" def")]),
            Narrow(6, [In(Fig(8.4)), In(Fig(8.4)), In(Fig(8.4))]),
            Narrow(6, [In(Fig(8.4)), Text(" "), In(Fig(8.4))]),
            Narrow(6, [Text("(a"), In(Fig(8.4)), Text(") "), In(Fig(8.4)), Text(".")]),
            Paragraph("x")), "ii-breaks");
    }

    [Fact]
    public void An_image_first_last_wrapping_or_wider_than_the_line_is_placed_like_Chromium()
    {
        ShouldMatch(Engine(499.92,
            Narrow(100, [In(Fig(21)), Text(" first on the line and then some words")]),
            Narrow(100, [Text("words to the end "), In(Fig(21))]),
            Narrow(100, [Text("some words here and "), In(Wid(48)), Text(" then")]),
            Narrow(100, [Text("some words here and"), In(Wid(48)), Text("then")]),
            Narrow(100, [Text("wide "), In(Wid(120)), Text(" after")]),
            Narrow(100, [Text("a "), In(Fig(21).MarginLeft(30.Pt()).MarginRight(30.Pt())), Text(" b "), In(Fig(21).MarginLeft(30.Pt()).MarginRight(30.Pt())), Text(" c")]),
            Paragraph("x")), "ii-wrap");
    }

    [Fact]
    public void An_image_in_an_aligned_line_moves_with_it_and_is_no_expansion_opportunity_like_Chromium()
    {
        ShouldMatch(Engine(400.08,
            Narrow(100, [Text("one two "), In(Fig(8.4)), Text(" three four five six")]).TextAlign(TextAlign.Justify),
            Narrow(100, [Text("one"), In(Fig(8.4)), Text("two three four five six")]).TextAlign(TextAlign.Justify),
            Narrow(100, [Text("one two "), In(Fig(8.4)), Text(" three four five six")]).TextAlign(TextAlign.Right),
            Narrow(100, [Text("one "), In(Fig(8.4)), Text(" three four five six")]).TextAlign(TextAlign.Center),
            Paragraph("x")), "ii-align");
    }

    [Fact]
    public void A_hyphen_after_an_image_keeps_its_letter_as_after_a_space_where_Chromium_breaks_after_it()
    {
        // The code front-end's line-break rules are the standard's (LineBreakRules.Standard): UAX #14 LB20a, "( sot | BK | CR |
        // LF | NL | SP | ZW | CB | GL ) ( HY | HH ) × ( AL | HL )", allows no break between a hyphen and the letter after it
        // when a space or an atomic inline (CB, U+FFFC) comes before the hyphen; Chromium breaks there all the same, after a
        // space as after an image (ii-hyphen: "-" and "b" on lines of their own), and so does the HTML front-end, whose rules
        // are Chromium's (LineBreakRules.Chromium; the case is equal there). The image behaves as the space does.
        var engine = Engine(300,
            Narrow(6, [Text("a"), In(Fig(8.4)), Text("-b")]),
            Narrow(6, [Text("a -b")]),
            Narrow(6, [Text("a-b")]),
            Paragraph("x"));

        engine.Pages[0].Lines.Select(line => line.Text).Should().Equal("a", "-b", "a", "-b", "a-", "b", "x");
        PrintedPages.Chromium("ii-hyphen").Pages[0].Lines.Select(line => line.Text).Should().Equal("a", "-", "b", "a", "-", "b", "a-", "b", "x");
    }

    [Fact]
    public void A_line_breaks_beside_an_image_also_next_to_a_joiner_or_another_no_break_space_like_Chromium()
    {
        // A DELIBERATE DEVIATION from CSS Text 3 §5.5 ("with the exception of U+00A0 NO-BREAK SPACE, there must be no soft
        // wrap opportunity between atomic inlines and adjacent characters belonging to the Unicode GL, WJ, or ZWJ line
        // breaking classes"), as Chromium breaks there (ii-joiners). The engine's words leave the invisible joiners out.
        static string Visible(string text) => text.Replace("\u2060", string.Empty, StringComparison.Ordinal).Replace("\u200D", string.Empty, StringComparison.Ordinal);
        var engine = Engine(300, [.. new[] { "\u2060", "\u200D", "\u202F", "\u2007" }.Select(joiner => (Element)Narrow(6, [Text("abc" + joiner), In(Fig(8.4)), Text(joiner + "def")])), Paragraph("x")]);
        var chromium = PrintedPages.Chromium("ii-joiners").Pages[0];

        engine.Pages[0].Lines.Select(line => (line.Baseline, Visible(line.Text))).Should().Equal(chromium.Lines.Select(line => (line.Baseline, Visible(line.Text))));
        engine.Pages[0].Images.Select(image => image.Y1).Should().Equal(chromium.Images.Select(image => image.Y1));
    }

    private static TableElement Cells(ImageElement image) =>
        Table(Columns(Auto, Auto), [TableRow([Cell([Paragraph([Text("ab "), In(image), Text(" cd")])]), Cell("x")])]).GridLines(0.75.Pt(), Color.Black).CellPadding(0.Pt());

    /// <summary>The page's words and images above <paramref name="y"/>.</summary>
    private static PrintedPages Above(PrintedPages pages, double y) =>
        new([.. pages.Pages.Select(page => page with { Words = [.. page.Words.Where(word => word.Baseline < y)], Images = [.. page.Images.Where(image => image.Y1 < y)] })]);

    /// <summary>The words of the engine's pages joined where the next cell's word follows on the same baseline across a
    /// grid line only (at most 0.75 pt and a unit apart), as the print's read-back joins characters without white space
    /// between them into one word (README.md, "Print positions": "cd" and the next cell's "x" are "cdx").</summary>
    private static PrintedPages JoinedAcrossGridLines(PrintedPages pages) =>
        new([.. pages.Pages.Select(page => page with
        {
            Words = page.Words.Aggregate(ImmutableArray<PrintedWord>.Empty, (words, word) =>
                words.Length > 0 && Math.Abs(words[^1].Baseline - word.Baseline) < 0.01 && word.X0 - words[^1].X1 is >= 0 and < 0.75 + ChromiumLayout.LayoutUnit
                    ? words.SetItem(words.Length - 1, words[^1] with { X1 = word.X1, Text = words[^1].Text + word.Text })
                    : words.Add(word)),
        })]);

    [Fact]
    public void An_image_counts_for_the_minimum_and_maximum_width_of_an_automatic_column_like_Chromium()
    {
        // The first two tables of the case: of automatic width (as wide as its maximum), and 60 pt wide, written as a
        // table of automatic width in a block of 60 pt, which its maximum width exceeds.
        var engine = Engine(400.08, Cells(Wid(32)), Block([Cells(Wid(32))]).PaddingRight(216.Pt()), Paragraph("x"));
        var breaches = Breaches(JoinedAcrossGridLines(Above(engine, 69)), Above(PrintedPages.Chromium("ii-table"), 69), halfPixelBaselines: false);
        breaches.Should().BeEmpty(string.Join("\n", breaches));
    }

    [Fact]
    public void An_image_and_its_margins_count_for_the_minimum_and_maximum_width_of_its_paragraph_like_Chromium()
    {
        // The third table of ii-table is 20 pt wide in Chromium, narrower than its content, so Chromium makes it as wide as
        // its minimum (the engine refuses a table narrower than its content): the first cell is 38.742 pt wide
        // (ii-table.boxes.txt, the first cell of the third table), the image (32 pt) and its margins (2 × 3 pt) plus 0.742 pt of grid lines, the halves
        // of two 0.75 pt lines each snapped to a layout unit.
        IntrinsicWidths Measured(ImageElement image)
        {
            var model = DocumentTranslator.ToModel(Document([Paragraph([Text("ab "), In(image), Text(" cd")])], Area(400), title: "Title", language: "en")
                .FontFamily("Lato").FontSize(12.Pt()).LineHeight(18.Pt())).Value;
            return LayoutEnvironment.Create(new FontSet([TestFonts.Regular])).Measure((Box)model.Root.Children[0]).Value;
        }

        var widths = Measured(Wid(32).MarginLeft(3.Pt()).MarginRight(3.Pt()));

        ChromiumLayout.DifferenceInLayoutUnits(widths.MinContent + 0.75, 38.742).Should().BeLessThanOrEqualTo(2, "the image and the two halves of grid lines, each a layout unit");
        // The maximum width is the line's: the margins count there too (the first table of the case has none).
        widths.MaxContent.Should().BeApproximately(Measured(Wid(32)).MaxContent + 6, 1e-9);
    }

    [Fact]
    public void A_line_made_taller_by_an_image_moves_to_the_next_page_whole_like_Chromium()
    {
        ShouldMatch(Engine(204,
            Paragraph("one"), Paragraph("two"), Paragraph("three"), Paragraph("four"), Paragraph("five"), Paragraph("six"), Paragraph("seven"), Paragraph("eight"),
            Paragraph([Text("nine "), In(Fig(28)), Text(" ten")]),
            Paragraph("eleven")), "ii-page", halfPixelBaselines: true);
    }

    [Fact]
    public void An_image_whose_height_is_not_whole_pixels_makes_its_line_as_tall_as_in_Chromium()
    {
        // The image edges pin each line's height (Chromium rounds them to whole pixels when it paints).
        ShouldMatch(Engine(420,
            [.. new[] { 40, 30.2, 30.5, 30.8, 30.1, 33.33 }.Select(height => (Element)Paragraph([Text("ab "), In(Image(Figure).Alt("figure").Height(height.Pt())), Text(" cd")])),
            Paragraph("x")]), "ii-height", halfPixelBaselines: true);
    }
}
