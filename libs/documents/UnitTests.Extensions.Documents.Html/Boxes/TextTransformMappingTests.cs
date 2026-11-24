using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.Documents.Html.Boxes;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace UnitTests.Extensions.Documents.Html.Boxes;

/// <summary>Box generation of <c>text-transform</c>: each text in the model as Chromium 159 prints it, or refused at the
/// text; the reference cases <c>tt-*</c> hold Chromium's prints.</summary>
public class TextTransformMappingTests
{
    private static string Doc(string body, string css = "") =>
        $"<!doctype html><html lang=\"en\"><head><title>t</title><style>body {{ font-family: Lato; margin: 0 }} {css}</style></head><body>{body}</body></html>";

    private static Result<HtmlBoxes, Error> Boxes(string html) =>
        HtmlParser.Parse(html, new HtmlParseOptions("t.html"))
            .OnSuccessBind(parsed => HtmlStyleResolver.Resolve(parsed, new HtmlStyleOptions()))
            .OnSuccessBind(styled => HtmlBoxGenerator.Generate(styled, new HtmlBoxOptions { Fonts = TestFonts.SetWithItalic }));

    /// <summary>The texts of the document's runs in order, inside inline groups too, the empty runs of inline elements left
    /// out.</summary>
    private static string Texts(string html)
    {
        var result = Boxes(html);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        var texts = new List<string>();
        var pending = new Stack<Node>([result.Value.Document.Root]);
        while (pending.Count > 0)
        {
            switch (pending.Pop())
            {
                case TextRun { Text.Length: > 0 } run:
                    texts.Add(run.Text);
                    break;
                case Box box:
                    foreach (var child in box.Children.Reverse())
                    {
                        pending.Push(child);
                    }

                    break;
                case InlineGroup group:
                    foreach (var child in group.Children.Reverse())
                    {
                        pending.Push(child);
                    }

                    break;
            }
        }

        return string.Join("|", texts);
    }

    private static Error Refusal(string html) => Boxes(html).Error.Parts().Single();

    [Theory]
    [InlineData("uppercase", "Wave today, quick", "WAVE TODAY, QUICK")]
    [InlineData("lowercase", "WAVE Today", "wave today")]
    [InlineData("capitalize", "wave today, quick", "Wave Today, Quick")]
    [InlineData("capitalize", "mIxed cAse", "MIxed CAse")]
    [InlineData("none", "Wave today", "Wave today")]
    public void Each_value_maps_the_text_as_Chromium_prints_it(string transform, string text, string expected) =>
        Texts(Doc($"<p style=\"text-transform: {transform}\">{text}</p>")).Should().Be(expected);

    [Theory]
    [InlineData("word ǆungla", "Word ǅungla")]
    [InlineData("word (paren) [square] /slash", "Word (Paren) [Square] /Slash")]
    [InlineData("word 3rd a1b 4x4", "Word 3rd A1b 4x4")]
    [InlineData("word don't 'quoted'", "Word Don't 'Quoted'")]
    [InlineData("word well-known e.g. a_b a:b a,b", "Word Well-Known E.G. A_b A:B A,B")]
    [InlineData("word «guillemet» ¿qué a\u00A0b", "Word «Guillemet» ¿Qué A\u00A0B")]
    public void Capitalize_titlecases_each_letter_after_anything_but_a_letter_a_digit_an_underscore_or_an_apostrophe_after_a_letter(string text, string expected) =>
        Texts(Doc($"<p style=\"text-transform: capitalize\">{text}</p>")).Should().Be(expected);

    [Fact]
    public void Capitalize_looks_at_the_last_character_of_the_text_before_across_inline_elements()
    {
        Texts(Doc("<p style=\"text-transform: capitalize\">word wo<span>rd</span> <span>next</span>word</p>"))
            .Should().Be("Word Wo|rd| |Next|word");
        Texts(Doc("<p>Word a<span style=\"text-transform: capitalize\">bc</span> <span style=\"text-transform: capitalize\">de</span>f</p>"))
            .Should().Be("Word a|bc| |De|f");
    }

    /// <summary>The edges of case <c>tt-groups</c>, where Chromium 159 printed the letter after each edge as the rest of the
    /// word: no edge of an inline element starts a word, a link's, a language's, a box's with padding, a border or a margin
    /// (where the edge takes room: "Wo rd") and a sup's included, nor does the end of one; generated text is the text before
    /// the element's own.</summary>
    [Theory]
    [InlineData("link wo<a href=\"https://example.com/\">rd</a>", "Link Wo|rd")]
    [InlineData("lang wo<span lang=\"nl\">rd</span>", "Lang Wo|rd")]
    [InlineData("back wo<span style=\"background-color: #ffd700\">rd</span>", "Back Wo|rd")]
    [InlineData("pad wo<span style=\"padding: 0 2pt\">rd</span>", "Pad Wo|rd")]
    [InlineData("edge wo<span style=\"border-left: 0.75pt solid #0000ff\">rd</span>", "Edge Wo|rd")]
    [InlineData("margin wo<span style=\"margin-left: 2pt\">rd</span>", "Margin Wo|rd")]
    [InlineData("sup wo<sup>rd</sup>", "Sup Wo|rd")]
    [InlineData("out <a href=\"https://example.com/\">wo</a>rd", "Out |Wo|rd")]
    [InlineData("out <span style=\"padding: 0 2pt\">wo</span>rd", "Out |Wo|rd")]
    [InlineData("out <span lang=\"nl\">wo</span>rd", "Out |Wo|rd")]
    [InlineData("gen <span class=\"g\" lang=\"nl\">ab</span>", "Gen |Xy|ab")]
    [InlineData("gen <a class=\"g\" href=\"https://example.com/\">ab</a>", "Gen |Xy|ab")]
    public void Capitalize_looks_back_across_every_edge_of_an_inline_element(string body, string expected) =>
        Texts(Doc($"<p style=\"text-transform: capitalize\">{body}</p>", ".g::before { content: \"xy\"; }")).Should().Be(expected);

    [Theory]
    [InlineData("tr", "uppercase", "istanbul ığne", "İSTANBUL IĞNE")]
    [InlineData("tr", "lowercase", "ISTANBUL İZMIR", "ıstanbul izmır")]
    [InlineData("en", "uppercase", "istanbul ığne", "ISTANBUL IĞNE")]
    public void Turkish_maps_the_dotted_and_dotless_i_as_its_language_does(string language, string transform, string text, string expected) =>
        Texts(Doc($"<p lang=\"{language}\" style=\"text-transform: {transform}\">{text}</p>")).Should().Be(expected);

    [Fact]
    public void A_capital_sigma_lowercases_to_final_sigma_where_it_ends_a_word() =>
        Texts(Doc("<p style=\"text-transform: lowercase\">ΟΔΟΣ ΣΟΦΟΣ. Σ</p>")).Should().Be("οδος σοφος. σ");

    [Fact]
    public void Capitalize_titlecases_in_the_language_of_the_text() =>
        Texts(Doc("<p lang=\"tr\" style=\"text-transform: capitalize\">word istanbul ıa</p>")).Should().Be("Word İstanbul Ia");

    [Theory]
    [InlineData("uppercase", "en", "Straße", 4)]
    [InlineData("uppercase", "en", "ﬁne", 0)]
    [InlineData("lowercase", "en", "İZMIR", 0)]
    [InlineData("uppercase", "el", "άδεια", 0)]
    [InlineData("capitalize", "en", "word ßa", 5)]
    [InlineData("capitalize", "en", "word ﬁne", 5)]
    [InlineData("capitalize", "nl", "word ijsland", 5)]
    [InlineData("capitalize", "en", "word a·b", 6)]
    [InlineData("lowercase", "en", "ΟΔΟ\u0301Σ", 4)]
    public void A_letter_Chromium_maps_to_more_characters_or_by_rules_of_its_language_is_refused_at_its_text(string transform, string language, string text, int offset)
    {
        var refusal = Refusal(Doc($"<p lang=\"{language}\" style=\"text-transform: {transform}\">{text}</p>"));

        refusal.Code.Should().Be(HtmlStyleErrorCodes.Unsupported.Value);
        refusal.Metadata![ErrorMetadataKeys.Property].Should().Be("text-transform");
        refusal.Metadata[ErrorMetadataKeys.Offset].Should().Be(offset);
    }

    [Theory]
    [InlineData("full-width")]
    [InlineData("uppercase full-size-kana")]
    [InlineData("math-auto")]
    public void The_transforms_to_other_character_forms_are_refused_by_value(string transform) =>
        Refusal(Doc($"<p style=\"text-transform: {transform}\">a</p>")).Message.Should().Contain("text-transform");

    [Fact]
    public void A_transform_that_reaches_a_page_margin_box_from_the_root_element_is_refused_there()
    {
        var refusal = Refusal(Doc("<p>a</p>", "@page { margin: 36pt; @top-left { content: \"x\" } } html { font-family: Lato; text-transform: uppercase }"));

        refusal.Code.Should().Be(HtmlStyleErrorCodes.Unsupported.Value);
        refusal.Message.Should().Contain("page-margin box");
    }
}
