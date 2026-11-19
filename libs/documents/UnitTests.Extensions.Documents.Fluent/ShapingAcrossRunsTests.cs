using System.Runtime.CompilerServices;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Functional;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// Text is shaped across the borders of the runs that share a font (CSS Text 3 §7.3), as Chromium shapes adjacent text
/// items with equal fonts as one text: a kerning pair or a ligature whose two halves lie in two runs is kept when only the
/// colour or the line height differs, and not when the weight, style, size or family does. A wrapped line's ends are shaped
/// again within their run. Cases <c>sr-*</c>: Lato 12 pt on 18 pt lines (Chromium keeps 12 pt exact), the same documents
/// written with the code front-end.
/// </summary>
/// <remarks>
/// THE COMPARISON. The print splits a word where its colour or font changes, the engine draws each run as a glyph run of
/// its own; on both sides the pieces of a word are joined again (<see cref="PrintedPage.JoinedWords"/>). On each line the first word starts
/// at the same place on both sides, and the distance from each word's start to the next is compared, and each word's
/// width: within one layout unit for each text item that ends in that stretch, since Chromium ceils each item's width to
/// a layout unit (README.md, "The comparison rule"), plus one for the print's rounding of glyph positions. A kerning pair
/// across a border is 1.467 pt here ("To": the o starts 5.625 pt after the T kerned, 7.092 pt alone), 125 layout units.
/// </remarks>
public class ShapingAcrossRunsTests
{
    private static readonly Color Red = new(204, 0, 0);

    private static PageSetup Area(double height) => Page(300.Pt(), height.Pt()).Margin(12.Pt());

    private static FontSet Fonts => new([TestFonts.Regular, TestFonts.Bold, TestFonts.Italic, TestFonts.TheanoDidot]);

    private static PrintedPages Engine(double pageHeight, params Element[] body)
    {
        var printed = DocumentTranslator.ToModel(Document([.. body], Area(pageHeight), title: "Title", language: "en").FontFamily("Lato").FontSize(12.Pt()).LineHeight(18.Pt()))
            .OnSuccessBind(model => DocumentLayout.Run(model, Fonts))
            .OnSuccessMap(PrintedPages.Engine);
        printed.IsSuccess.Should().BeTrue(printed.IsFailure ? printed.Error.ToString() : string.Empty);
        return printed.Value;
    }

    /// <summary>The words where the engine and Chromium differ: the distance from each word's start to the next word's
    /// start on its line (the first word of a line: its start), and its width, each in layout units, beyond one layout
    /// unit for each text item that ends in that stretch and one for the print's rounding. The documents are written with
    /// one run per text item of the case, so the engine's pieces of a word are its items. The texts must be equal, except
    /// where Chromium's print loses characters (<paramref name="printLoses"/>, Chromium's text → the engine's).</summary>
    private static List<string> Breaches(PrintedPage engine, PrintedPage chromium, IReadOnlyDictionary<string, string> printLoses)
    {
        var (mine, theirs) = (engine.JoinedWords, chromium.JoinedWords);
        // The engine's words leave out the invisible characters (PrintedPages.Engine), the print keeps a non-joiner.
        mine.Select(word => word.Word.Text).Should().Equal(theirs.Select(word => printLoses.GetValueOrDefault(word.Word.Text, word.Word.Text).Replace("\u200C", string.Empty, StringComparison.Ordinal)));
        var breaches = new List<string>();
        for (var index = 0; index < mine.Length; index++)
        {
            var (engineWord, chromiumWord) = (mine[index].Word, theirs[index].Word);
            var lineStart = index == 0 || Math.Abs(mine[index - 1].Word.Baseline - engineWord.Baseline) > 0.01;
            var (start, startBound) = lineStart
                ? (ChromiumLayout.DifferenceInLayoutUnits(engineWord.X0, chromiumWord.X0), 1)
                : (ChromiumLayout.DifferenceInLayoutUnits(engineWord.X0 - mine[index - 1].Word.X0, chromiumWord.X0 - theirs[index - 1].Word.X0), mine[index - 1].Pieces + 1);
            var width = ChromiumLayout.DifferenceInLayoutUnits(engineWord.X1 - engineWord.X0, chromiumWord.X1 - chromiumWord.X0);
            if (start > startBound || width > mine[index].Pieces)
            {
                breaches.Add($"{index} '{engineWord.Text}' at {chromiumWord.Baseline}: start {start:0.00} LU (engine {engineWord.X0:0.000}, Chromium {chromiumWord.X0:0.000}), width {width:0.00} LU (engine {engineWord.X1 - engineWord.X0:0.000}, Chromium {chromiumWord.X1 - chromiumWord.X0:0.000})");
            }
        }

        return breaches;
    }

    private static void ShouldMatch(PrintedPages engine, string caseName, IReadOnlyDictionary<string, string>? printLoses = null)
    {
        var chromium = PrintedPages.Chromium(caseName);
        engine.Pages.Should().HaveCount(chromium.Pages.Length);
        var breaches = Breaches(engine.Pages[0], chromium.Pages[0], printLoses ?? new Dictionary<string, string>());
        breaches.Should().BeEmpty(string.Join("\n", breaches));
    }

    [Fact]
    public void Runs_that_differ_only_in_colour_are_kerned_across_their_border_like_Chromium()
    {
        ShouldMatch(Engine(480,
            Paragraph("To"),
            Paragraph([Text("T"), Text("o").Color(Red)]),
            Paragraph("AVA"),
            Paragraph([Text("A"), Text("V").Color(Red), Text("A")]),
            Paragraph("fix"),
            Paragraph([Text("f"), Text("i").Color(Red), Text("x")]),
            Paragraph("office"),
            Paragraph([Text("of"), Text("fi").Color(Red), Text("ce")]),
            Paragraph("x y"),
            Paragraph([Text("x "), Text("y").Color(Red)]),
            Paragraph([Text("x"), Text(" y").Color(Red)]),
            Paragraph([Bold("x y")]),
            Paragraph([Bold("x "), Bold("y")]),
            Paragraph([Bold("x"), Bold(" y")]),
            Paragraph([Text(" "), Bold(" x "), Text(" "), Bold(" y "), Text(" ")]),
            Paragraph([Text("T"), Text("o").Color(Red), Text("T"), Text("o").Color(Red), Text("T")]),
            Paragraph("Yo"),
            Paragraph([Text("Y"), Text("o").Color(Red)])), "sr-same-font",
            // A ligature across the border: Chromium forms it and its print maps the glyph to the first run's characters
            // only; the engine's glyph keeps every character it shows, so the text can be copied and searched.
            new Dictionary<string, string> { ["fx"] = "fix", ["ofce"] = "office" });
    }

    [Fact]
    public void Runs_in_another_weight_style_size_or_family_are_not_kerned_across_their_border_like_Chromium()
    {
        ShouldMatch(Engine(300,
            Paragraph("To"),
            Paragraph([Text("T"), Bold("o")]),
            Paragraph([Bold("To")]),
            Paragraph([Text("T"), Text("o").Italic()]),
            Paragraph([Text("T"), Text("o").FontSize(9.Pt())]),
            Paragraph([Text("T"), Text("o").FontFamily("Theano Didot")]),
            Paragraph([Text("T"), Text("o").LineHeight(30.Pt())]),
            Paragraph([Text("AV"), Bold("A")])), "sr-font-changes");
    }

    private static Element Alternating(TextAlign align) =>
        Paragraph([.. Enumerable.Range(0, 20).SelectMany(word => word % 2 == 0
            ? new[] { Text(word == 0 ? "T" : " T"), Text("o ").Color(Red) }
            : [Text("T"), Text("o").Color(Red)])]).TextAlign(align).PaddingRight(159.Pt());

    [Theory]
    [InlineData("sr-line-left", TextAlign.Left)]
    [InlineData("sr-line-justify", TextAlign.Justify)]
    public void A_wrapped_line_shapes_its_first_word_again_within_its_run_like_Chromium(string caseName, TextAlign align)
    {
        ShouldMatch(Engine(200,
            Alternating(align),
            Paragraph(string.Join(" ", Enumerable.Repeat("To", 20))).TextAlign(align).PaddingRight(159.Pt())), caseName);
    }

    [Fact]
    public void A_zero_width_non_joiner_that_starts_a_run_breaks_the_kerning_like_Chromium()
    {
        // Inside a run, and at a run's end, the non-joiner changes nothing: Chromium kerns "T" U+200C "o" as "To".
        ShouldMatch(Engine(200,
            Paragraph($"T{ZeroWidthNonJoiner}o"),
            Paragraph([Text("T"), Text($"{ZeroWidthNonJoiner}o").Color(Red)]),
            Paragraph([Text("T"), Text($"o{ZeroWidthNonJoiner}").Color(Red), Text("T")])), "sr-zwnj");
    }

    [Fact]
    public void A_page_number_is_shaped_with_the_text_beside_it()
    {
        // Chromium prints counter(page) in the page body as 0, so its third paragraph, generated "1" beside letters, is the
        // reference for the page number: an item of generated text is shaped with its neighbours like any other. Lato kerns
        // none of these pairs (the one-run "T1" is as wide as T and 1 apart), so this pins that a page number in a group of
        // runs changes nothing.
        ShouldMatch(Engine(200,
            Paragraph("T1 Y1 V1 71 17 11 1T 1Y 1V W1 A1 1A"),
            Paragraph([Text("T"), Text("0"), Text(" Y"), Text("0"), Text(" 7"), Text("0"), Text(" "), Text("0"), Text("T "), Text("0"), Text("Y")]),
            Paragraph([Text("T"), PageNumber(), Text(" Y"), PageNumber(), Text(" 7"), PageNumber(), Text(" "), PageNumber(), Text("T "), PageNumber(), Text("Y")])), "sr-counter");
    }

    private const char ZeroWidthNonJoiner = '\u200C';

    private static PageDocument Laid(params Element[] body)
    {
        var laid = DocumentTranslator.ToModel(Document([.. body], Area(200), title: "Title", language: "en").FontFamily("Lato").FontSize(12.Pt()))
            .OnSuccessBind(model => DocumentLayout.Run(model, Fonts));
        laid.IsSuccess.Should().BeTrue(laid.IsFailure ? laid.Error.ToString() : string.Empty);
        return laid.Value;
    }

    [Fact]
    public void A_ligature_across_a_border_belongs_to_the_run_with_its_first_character()
    {
        // Chromium: "the item that has its first code unit keeps the glyph" (InlineNode::ShapeText), so the fi of f + red i
        // is drawn black and the red run draws nothing; the glyph's text is both characters.
        var runs = Laid(Paragraph([Text("f"), Text("i").Color(Red), Text("x")])).Pages[0].Items.OfType<GlyphRun>()
            .Where(run => run.Glyphs.Count > 0).ToList();

        runs.Select(run => (run.Color, string.Concat(run.Glyphs.Select(glyph => glyph.Text)))).Should().Equal((Color.Black, "fi"), (Color.Black, "x"));
        runs[0].Glyphs.Should().ContainSingle();
    }

    private static int Line([CallerLineNumber] int line = 0) => line;

    [Fact]
    public void A_character_the_font_lacks_in_a_run_shaped_with_others_is_an_error_at_that_run()
    {
        var first = Line() + 1;
        var lacking = Text("kanji \u6F22");
        var second = Line() + 1;
        var alsoLacking = Text("yen \u5186").Color(Red);

        var error = DocumentTranslator.ToModel(Document([Paragraph([Text("To "), lacking, Text(" and "), alsoLacking])], Area(200), title: "Title", language: "en").FontFamily("Lato"))
            .OnSuccessBind(model => DocumentLayout.Run(model, Fonts)).Error;

        error.Parts().Select(part => (part.Code, part.Metadata![ErrorMetadataKeys.SourceLine])).Should().Equal(
            (FontErrorCodes.GlyphMissing, first), (FontErrorCodes.GlyphMissing, second));
    }

    public static TheoryData<string> OtherFonts() => ["bold", "Theano", "9pt"];

    private static Span InOtherFont(string font, string text) => font switch
    {
        "bold" => Bold(text),
        "Theano" => Text(text).FontFamily("Theano Didot"),
        _ => Text(text).FontSize(9.Pt()),
    };

    [Theory]
    [MemberData(nameof(OtherFonts))]
    public void A_run_without_text_that_opens_a_paragraph_does_not_set_the_font_of_the_text_after_it(string font)
    {
        // The empty run is in Lato regular 12 pt; the text after it is shaped and drawn in its own font, as without it.
        static List<GlyphRun> Drawn(PageDocument document) => [.. document.Pages[0].Items.OfType<GlyphRun>().Where(run => run.Glyphs.Count > 0)];
        var withEmpty = Drawn(Laid(Paragraph([Text(string.Empty), InOtherFont(font, "To")])));
        var alone = Drawn(Laid(Paragraph([InOtherFont(font, "To")])));

        withEmpty.Select(run => (run.Font, run.FontSize, string.Join(",", run.Glyphs.Select(glyph => $"{glyph.Glyph}@{glyph.X:0.000}"))))
            .Should().Equal(alone.Select(run => (run.Font, run.FontSize, string.Join(",", run.Glyphs.Select(glyph => $"{glyph.Glyph}@{glyph.X:0.000}")))));
    }

    [Fact]
    public void A_run_without_text_in_another_font_counts_for_the_line_height_with_its_own_font()
    {
        // Theano Didot's line at 12 pt is taller than Lato's. An empty inline box counts for the line's height as one with
        // text does (CSS 2.1 10.8), so "x" + an empty Theano run makes the line of "x" + a Theano "x": the next line's
        // baseline is as low.
        static double SecondBaseline(Span theano) =>
            PrintedPages.Engine(Laid(Paragraph([Text("x "), theano]), Paragraph("y"))).Pages[0].Words.Single(word => word.Text == "y").Baseline;

        SecondBaseline(Text(" ").FontFamily("Theano Didot")).Should().BeApproximately(SecondBaseline(Text("x").FontFamily("Theano Didot")), 1e-9);
    }
}
