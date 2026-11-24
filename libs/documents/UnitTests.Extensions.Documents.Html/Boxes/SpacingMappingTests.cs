using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.Documents.Html.Boxes;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace UnitTests.Extensions.Documents.Html.Boxes;

/// <summary>Box generation of <c>letter-spacing</c> and <c>word-spacing</c>: the computed lengths in each run's text style
/// (<see cref="TextStyle.LetterSpacing"/>, <see cref="TextStyle.WordSpacing"/>), or refused; the reference cases
/// <c>ls-*</c> hold Chromium's prints, which the case comparison lays out.</summary>
public class SpacingMappingTests
{
    private static string Doc(string body, string css = "") =>
        $"<!doctype html><html lang=\"en\"><head><title>t</title><style>body {{ font-family: Lato; margin: 0 }} {css}</style></head><body>{body}</body></html>";

    private static Result<HtmlBoxes, Error> Boxes(string html) =>
        HtmlParser.Parse(html, new HtmlParseOptions("t.html"))
            .OnSuccessBind(parsed => HtmlStyleResolver.Resolve(parsed, new HtmlStyleOptions()))
            .OnSuccessBind(styled => HtmlBoxGenerator.Generate(styled, new HtmlBoxOptions { Fonts = TestFonts.SetWithItalic }));

    private static TextRun Run(string html, string text)
    {
        var result = Boxes(html);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        var pending = new Stack<Node>([result.Value.Document.Root]);
        while (pending.Count > 0)
        {
            switch (pending.Pop())
            {
                case TextRun run when run.Text == text:
                    return run;
                case Box box:
                    foreach (var child in box.Children)
                    {
                        pending.Push(child);
                    }

                    break;
            }
        }

        throw new InvalidOperationException($"no run '{text}'");
    }

    private static Error Refusal(string html) => Boxes(html).Error.Parts().Single();

    [Theory]
    [InlineData("letter-spacing: 3px", 2.25, 0)]
    [InlineData("letter-spacing: -0.5pt", -0.5, 0)]
    [InlineData("letter-spacing: 0.1em; font-size: 10.5pt", 1.05, 0)]
    [InlineData("letter-spacing: normal", 0, 0)]
    [InlineData("word-spacing: 4px", 0, 3)]
    [InlineData("word-spacing: 0.25em; font-size: 12pt", 0, 3)]
    [InlineData("word-spacing: 0%", 0, 0)]
    public void A_spacing_is_the_computed_length_in_the_text_style(string css, double letter, double word)
    {
        var style = Run(Doc($"<p style=\"{css}\">a b</p>"), "a b").Style;

        style.LetterSpacing.Points.Should().BeApproximately(letter, 1e-9);
        style.WordSpacing.Points.Should().BeApproximately(word, 1e-9);
    }

    [Fact]
    public void An_em_spacing_inherits_as_its_length_from_the_element_that_sets_it() =>
        Run(Doc("<p style=\"letter-spacing: 0.1em; font-size: 10pt\">a <span style=\"font-size: 20pt\">b</span></p>"), "b").Style.LetterSpacing.Points
            .Should().BeApproximately(1, 1e-9);

    [Theory]
    [InlineData("letter-spacing: 10%")]
    [InlineData("word-spacing: 10%")]
    public void A_percentage_is_refused_by_value(string css) =>
        Refusal(Doc($"<p style=\"{css}\">a</p>")).Message.Should().Contain("percentage");

    [Theory]
    [InlineData("a\u0301b", 1)]
    [InlineData("ab\u00ADc", 2)]
    public void A_mark_or_a_format_character_in_letter_spaced_text_is_refused_at_its_text(string text, int offset)
    {
        var refusal = Refusal(Doc($"<p style=\"letter-spacing: 1px\">{text}</p>"));

        refusal.Code.Should().Be(HtmlStyleErrorCodes.Unsupported.Value);
        refusal.Metadata![ErrorMetadataKeys.Property].Should().Be("letter-spacing");
        refusal.Metadata[ErrorMetadataKeys.Offset].Should().Be(offset);
    }

    [Fact]
    public void A_spacing_on_a_page_margin_box_is_refused_there() =>
        Refusal(Doc("<p>a</p>", "@page { margin: 36pt; @top-left { content: \"x\"; letter-spacing: 1px } } html { font-family: Lato }"))
            .Message.Should().Contain("page-margin box");
}
