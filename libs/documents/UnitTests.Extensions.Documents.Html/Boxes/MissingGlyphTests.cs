using Ave.Extensions.Documents;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.Documents.Html.Boxes;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace UnitTests.Extensions.Documents.Html.Boxes;

/// <summary>
/// A character the face of its text has no glyph for is refused in both modes, each pair of face and character once, at
/// the character's own line and column (the declaration for generated text), with what to do; the characters that need
/// no glyph are never missing; the no-break hyphen is drawn with the face's hyphen U+2010 (case <c>gm-nbhyphen-lato</c>).
/// </summary>
public class MissingGlyphTests
{
    private static readonly HtmlRenderOptions Options = new()
    {
        Fonts = new FontSet([TestFonts.Regular, TestFonts.TheanoDidot, TestFonts.RobotoSubset]),
        SourceName = "doc.html",
    };

    private static string Document(string body, string style = "body { font-family: Lato }") =>
        $"<!doctype html><html><head><style>@page {{ size: 300pt 200pt; margin: 12pt }} body {{ margin: 0 }} {style}</style></head><body>{body}</body></html>";

    /// <summary>The 1-based line and column of the first <paramref name="needle"/> in <paramref name="text"/>.</summary>
    private static (int Line, int Column) PlaceOf(string text, string needle, int skip = 0)
    {
        var index = -1;
        for (var i = 0; i <= skip; i++)
        {
            index = text.IndexOf(needle, index + 1, StringComparison.Ordinal);
        }

        var before = text[..index];
        var lineStart = before.LastIndexOf('\n') + 1;
        return (before.Count(c => c == '\n') + 1, index - lineStart + 1);
    }

    private static (object? Line, object? Column) Place(Error error) =>
        (error.Metadata![ErrorMetadataKeys.SourceLine], error.Metadata![ErrorMetadataKeys.SourceColumn]);

    private static List<Error> Refusals(string html, UnsupportedPolicy policy = UnsupportedPolicy.Refuse)
    {
        var result = HtmlRendering.ToPages(html, Options with { Style = HtmlStyleOptions.Default with { Unsupported = policy } });
        result.IsFailure.Should().BeTrue();
        return [.. result.Error.Parts()];
    }

    [Theory]
    [InlineData(UnsupportedPolicy.Refuse)]
    [InlineData(UnsupportedPolicy.Warn)]
    public void Each_missing_character_is_refused_at_its_own_line_and_column_once_per_face(UnsupportedPolicy policy)
    {
        // A character written as a reference is at its ampersand; one after a line feed of the text on the next line; a
        // second ✔ in the same face is not refused again, one in another face is.
        var html = Document("<p>ab\ncd&#x2714;e \u2714 \u2610</p><p style=\"font-family: 'Theano Didot'\">x\u2714</p>");

        var refusals = Refusals(html, policy);

        refusals.Select(error => error.Code).Should().AllBeEquivalentTo(FontErrorCodes.GlyphMissing);
        refusals.Select(error => (error.Metadata![ErrorMetadataKeys.CodePoint], error.Metadata![ErrorMetadataKeys.FontName]))
            .Should().Equal(("U+2714", "Lato-Regular"), ("U+2610", "Lato-Regular"), ("U+2714", "TheanoDidot-Regular"));
        refusals.Select(Place).Should().Equal(
            ((object?)PlaceOf(html, "&#x2714;").Line, (object?)PlaceOf(html, "&#x2714;").Column),
            (PlaceOf(html, "\u2610").Line, PlaceOf(html, "\u2610").Column),
            (PlaceOf(html, "\u2714", skip: 1).Line, PlaceOf(html, "\u2714", skip: 1).Column));
        refusals.Should().OnlyContain(error => (string)error.Metadata![ErrorMetadataKeys.SourceFile]! == "doc.html");
    }

    [Fact]
    public void The_refusal_names_the_character_the_face_the_list_and_what_to_do()
    {
        var refusal = Refusals(Document("<p>a\u2714b</p>")).Single();

        refusal.Message.Should().Be("The character '\u2714' (U+2714) has no glyph in the face 'Lato-Regular' that 'font-family: Lato' gives here, " +
            "and the engine takes no glyph from another font: put the character in an element whose font has it, or use an image.");
    }

    [Fact]
    public void A_later_family_of_the_list_that_has_the_character_is_named()
    {
        // U+203B is in Theano Didot, not in Lato: a browser takes it from the later family, the engine names it.
        var refusal = Refusals(Document("<p>a\u203Bb</p>", "body { font-family: Lato, \"Theano Didot\" }")).Single();

        refusal.Message.Should().Be("The character '\u203B' (U+203B) has no glyph in the face 'Lato-Regular' that 'font-family: Lato, Theano Didot' gives here, " +
            "and the engine takes no glyph from another font: put the character in an element whose font has it, or use an image. " +
            "The family 'Theano Didot' of the list has it: set 'Theano Didot' on an element around the character.");
    }

    [Fact]
    public void A_generated_character_is_refused_at_its_declaration()
    {
        var style = "body { font-family: Lato } @page { @top-center { font-family: Lato; content: \"x \\2714\" } }";
        var html = Document("<p>a</p>", style);

        var refusal = Refusals(html).Single();

        refusal.Metadata![ErrorMetadataKeys.CodePoint].Should().Be("U+2714");
        Place(refusal).Should().Be(((object?)PlaceOf(html, "content").Line, (object?)PlaceOf(html, "content").Column));
    }

    [Fact]
    public void A_character_of_a_pseudo_element_s_text_is_refused_at_its_content_declaration()
    {
        var html = Document("<p class=\"g\">a</p>", "body { font-family: Lato } .g::before { content: \"y \\2714\" }");

        var refusal = Refusals(html).Single();

        refusal.Code.Should().Be(FontErrorCodes.GlyphMissing);
        refusal.Metadata![ErrorMetadataKeys.CodePoint].Should().Be("U+2714");
        Place(refusal).Should().Be(((object?)PlaceOf(html, "content").Line, (object?)PlaceOf(html, "content").Column));
    }

    [Fact]
    public void Every_item_of_a_pseudo_element_s_content_and_a_counter_s_text_are_checked_at_its_content_declaration()
    {
        // One text run per item: the check reads the items and the counter's text the counting found, as they are drawn.
        var html = Document("<p class=\"g\">a</p>", "body { font-family: Lato } .g { counter-reset: c 2 } .g::before { content: \"x\" counter(c, square) \" \\2714\" }");

        var refusals = Refusals(html).ToList();

        refusals.Select(refusal => (refusal.Code, refusal.Metadata![ErrorMetadataKeys.CodePoint])).Should().Equal((FontErrorCodes.GlyphMissing, "U+25A0"), (FontErrorCodes.GlyphMissing, "U+2714"));
        refusals.Select(Place).Should().AllBeEquivalentTo(((object?)PlaceOf(html, "content").Line, (object?)PlaceOf(html, "content").Column));
    }

    [Fact]
    public void A_document_is_refused_for_at_most_the_bound_of_characters_in_source_order()
    {
        // 80 distinct CJK characters Lato does not have.
        var characters = string.Concat(Enumerable.Range(0x4E00, 80).Select(code => (char)code));

        var refusals = Refusals(Document($"<p>{characters}</p>"));

        refusals.Should().HaveCount(HtmlBoxGenerator.MaxMissingGlyphs);
        refusals.Select(error => (string)error.Metadata![ErrorMetadataKeys.CodePoint]!)
            .Should().Equal(Enumerable.Range(0x4E00, HtmlBoxGenerator.MaxMissingGlyphs).Select(code => $"U+{code:X4}"));
    }

    /// <summary>The characters that need no glyph, in the Roboto subset, which has a glyph for none of them (only I and
    /// U+0197): the soft hyphen where no line breaks at it, and default-ignorable characters (Default_Ignorable_Code_Point:
    /// zero width space, the joiners, word joiner, U+FEFF, a variation selector, the Mongolian vowel separator).</summary>
    /// <summary>A character that only text-transform makes is checked as it is drawn: Lato has the Latin alpha ɑ but not
    /// its capital Ɑ (U+2C6D), so uppercase refuses the capital at the place of the ɑ, naming both.</summary>
    [Fact]
    public void A_capital_that_only_uppercase_makes_is_refused_at_the_letter_it_is_made_of()
    {
        var html = Document("<p style=\"text-transform: uppercase\">word a\u0251b</p>");

        var refusal = Refusals(html).Single();

        refusal.Code.Should().Be(FontErrorCodes.GlyphMissing);
        refusal.Metadata![ErrorMetadataKeys.CodePoint].Should().Be("U+2C6D");
        Place(refusal).Should().Be(((object?)PlaceOf(html, "\u0251").Line, (object?)PlaceOf(html, "\u0251").Column));
        refusal.Message.Should().StartWith("The character '\u2C6D' (U+2C6D), which 'text-transform: uppercase' makes of '\u0251' (U+0251) written here, has no glyph in the face 'Lato-Regular'");
    }

    /// <summary>The other direction, on the small subset of Roboto the tests have (only I and Ɨ): lowercase makes ɨ
    /// (U+0268), which the subset has not, refused at the Ɨ. No face of the family's assets has a capital without its small
    /// letter apart from this subset.</summary>
    [Fact]
    public void A_small_letter_that_only_lowercase_makes_is_refused_at_the_letter_it_is_made_of()
    {
        var html = Document("<p style=\"font-family: Roboto; text-transform: lowercase\">\u0197</p>", string.Empty);

        var refusal = Refusals(html).Single();

        refusal.Metadata![ErrorMetadataKeys.CodePoint].Should().Be("U+0268");
        Place(refusal).Should().Be(((object?)PlaceOf(html, "\u0197").Line, (object?)PlaceOf(html, "\u0197").Column));
        refusal.Message.Should().Contain("which 'text-transform: lowercase' makes of '\u0197' (U+0197) written here");
    }

    /// <summary>A capital sigma at a word's end lowercases to the final sigma ς, and the check is given that text: every face
    /// of the family's assets that has Σ and σ has ς too, so no refusal can show it, and the check's input is read here.</summary>
    [Fact]
    public void The_glyph_check_reads_the_final_sigma_that_lowercase_makes()
    {
        var seen = new List<string>();
        var html = Document("<p style=\"text-transform: lowercase\">\u039F\u0394\u039F\u03A3 word</p>");
        var result = HtmlParser.Parse(html, new HtmlParseOptions("doc.html"))
            .OnSuccessBind(parsed => HtmlStyleResolver.Resolve(parsed, new HtmlStyleOptions()))
            .OnSuccessBind(styled => HtmlBoxGenerator.Generate(styled, new HtmlBoxOptions
            {
                Fonts = new FontSet([TestFonts.Regular]),
                MissingCharacters = (text, face) =>
                {
                    seen.Add(text);
                    return [];
                },
            }));

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        seen.Should().Contain(text => text.Contains("\u03BF\u03B4\u03BF\u03C2", StringComparison.Ordinal), "the check reads οδος, its last letter the final sigma");
    }

    [Theory]
    [InlineData("&#xAD;")]
    [InlineData("&#x200B;")]
    [InlineData("&#x200C;")]
    [InlineData("&#x200D;")]
    [InlineData("&#x2060;")]
    [InlineData("&#xFEFF;")]
    [InlineData("&#xFE0F;")]
    [InlineData("&#x180E;")]
    public void A_character_that_needs_no_glyph_is_never_missing(string character)
    {
        var html = Document($"<p>I{character}I</p>", "body { font-family: Roboto }");

        var pages = HtmlRendering.ToPages(html, Options);

        pages.IsSuccess.Should().BeTrue(pages.IsFailure ? pages.Error.ToString() : string.Empty);
        PrintedPages.Engine(pages.Value.Output).PageTexts.Single().Should().StartWith("I").And.EndWith("I");
    }

    /// <summary>Lato has no glyph for the tab (nor for the line feed); under white-space collapsing they are spaces.</summary>
    [Theory]
    [InlineData("a\tb")]
    [InlineData("a\nb")]
    [InlineData("a&#x9;b")]
    [InlineData("a\r\nb")]
    public void Tab_and_line_feed_under_collapsing_are_never_missing(string text)
    {
        TestFonts.Regular.GlyphFor(new System.Text.Rune('\t')).HasValue.Should().BeFalse("the case needs a face without the tab");

        var pages = HtmlRendering.ToPages(Document($"<p>{text}</p>"), Options);

        pages.IsSuccess.Should().BeTrue(pages.IsFailure ? pages.Error.ToString() : string.Empty);
        PrintedPages.Engine(pages.Value.Output).PageTexts.Should().Equal("a b");
    }

    [Fact]
    public void The_no_break_hyphen_is_drawn_with_the_face_s_hyphen()
    {
        TestFonts.Regular.GlyphFor(new System.Text.Rune(0x2011)).HasValue.Should().BeFalse("the case needs a face without U+2011");
        var pages = HtmlRendering.ToPages(Document("<p>a\u2011b a\u2010b</p>"), Options);

        pages.IsSuccess.Should().BeTrue(pages.IsFailure ? pages.Error.ToString() : string.Empty);
        PrintedPages.Engine(pages.Value.Output).PageTexts.Should().Equal("a\u2011b a\u2010b");
    }

    [Theory]
    [InlineData("\u2011")]
    [InlineData("\u2010")]
    public void A_hyphen_a_face_cannot_draw_is_refused(string hyphen)
    {
        // Theano Didot has U+002D only: Chromium takes U+2010 and U+2011 from a font of the system (case gm-nbhyphen-theano).
        var refusal = Refusals(Document($"<p>a{hyphen}b</p>", "body { font-family: \"Theano Didot\" }")).Single();

        refusal.Metadata![ErrorMetadataKeys.CodePoint].Should().Be($"U+{(int)hyphen[0]:X4}");
    }
}
