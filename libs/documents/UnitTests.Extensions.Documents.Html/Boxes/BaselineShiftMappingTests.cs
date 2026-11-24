using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.Documents.Html.Boxes;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace UnitTests.Extensions.Documents.Html.Boxes;

/// <summary>Box generation of <c>vertical-align</c> on inline text and of the <c>sup</c> and <c>sub</c> elements: the
/// baseline shift in each run's text style (<see cref="TextStyle.BaselineShift"/>), in Chromium 159's layout unit of
/// 1/64 px, or refused; the reference cases <c>va-*</c> hold Chromium's prints, which the case comparison lays out.</summary>
public class BaselineShiftMappingTests
{
    /// <summary>1/64 CSS px in points.</summary>
    private const double Unit = 0.75 / 64;

    private static string Doc(string body, string css = "") =>
        $"<!doctype html><html lang=\"en\"><head><title>t</title><style>body {{ font-family: Lato; margin: 0 }} {css}</style></head><body>{body}</body></html>";

    private static Result<HtmlBoxes, Error> Boxes(string html, UnsupportedPolicy policy = UnsupportedPolicy.Refuse) =>
        HtmlParser.Parse(html, new HtmlParseOptions("t.html"))
            .OnSuccessBind(parsed => HtmlStyleResolver.Resolve(parsed, new HtmlStyleOptions { Unsupported = policy }))
            .OnSuccessBind(styled => HtmlBoxGenerator.Generate(styled, new HtmlBoxOptions { Fonts = TestFonts.SetWithItalic, Unsupported = policy }));

    private static TextRun Run(string html, string text, UnsupportedPolicy policy = UnsupportedPolicy.Refuse)
    {
        var result = Boxes(html, policy);
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
                case InlineGroup group:
                    foreach (var child in group.Children)
                    {
                        pending.Push(child);
                    }

                    break;
            }
        }

        throw new InvalidOperationException($"no run '{text}'");
    }

    private static Error Refusal(string html) => Boxes(html).Error.Parts().Single();

    private static double Units(TextRun run) => run.Style.BaselineShift.Points / Unit;

    [Theory]
    // super: the parent's size in 1/64 px, divided by 3 and cut, plus 64; sub: divided by 5, cut, plus 64, down.
    [InlineData("10.5pt", "super", 362)]
    [InlineData("10.5pt", "sub", -243)]
    [InlineData("11pt", "super", 377)]
    [InlineData("11pt", "sub", -251)]
    [InlineData("8pt", "super", 291)]
    [InlineData("14pt", "sub", -303)]
    [InlineData("16pt", "super", 519)]
    [InlineData("16pt", "sub", -337)]
    public void Super_and_sub_shift_by_the_parent_s_font_size_in_the_layout_unit(string size, string align, int units) =>
        Units(Run(Doc($"<p style=\"font-size: {size}\">x <span style=\"vertical-align: {align}\">2</span></p>"), "2")).Should().Be(units);

    [Fact]
    public void A_sup_at_smaller_shifts_by_its_parent_s_size_not_its_own()
    {
        var run = Run(Doc("<p style=\"font-size: 10.5pt\">x<sup>2</sup></p>"), "2");

        Units(run).Should().Be(362);
        run.Style.FontSize.Points.Should().BeApproximately(10.5 / 1.2, 1e-9);
        run.Style.LineHeight.Should().Be(LineHeight.Normal.Instance);
    }

    [Fact]
    public void A_sup_keeps_the_line_height_it_inherits() =>
        Run(Doc("<p style=\"line-height: 18pt\">x<sup>2</sup></p>"), "2").Style.LineHeight.Should().Be(new LineHeight.Fixed(Length.FromPoints(18)));

    [Theory]
    // A length cut toward zero to 1/64 px; a percentage of the element's own line height, cut so.
    [InlineData("2.35pt", "", 200)]
    [InlineData("-2.35pt", "", -200)]
    [InlineData("0.4em", "", 358)]
    [InlineData("30%", "line-height: 18pt", 460)]
    [InlineData("-30%", "line-height: 18pt", -460)]
    [InlineData("30%", "", 326)]
    [InlineData("baseline", "", 0)]
    public void A_length_or_a_percentage_is_cut_toward_zero_to_the_layout_unit(string align, string css, int units) =>
        Units(Run(Doc($"<p style=\"font-size: 10.5pt; {css}\">x <span style=\"vertical-align: {align}\">2</span></p>"), "2")).Should().Be(units);

    [Fact]
    public void A_percentage_is_of_the_element_s_own_line_height() =>
        Units(Run(Doc("<p style=\"font-size: 10.5pt; line-height: 18pt\">x <span style=\"vertical-align: 30%; line-height: 30pt\">2</span></p>"), "2"))
            .Should().Be(768);

    [Fact]
    public void The_text_inside_a_shifted_element_and_after_it_is_shifted_as_its_element_is()
    {
        var html = Doc("<p style=\"font-size: 10.5pt\">x<sup>a<a href=\"https://example.com/\">b</a></sup> y</p>");

        Units(Run(html, "a")).Should().Be(298 + 64);
        Units(Run(html, "b")).Should().Be(298 + 64);
        Units(Run(html, " y")).Should().Be(0);
    }

    [Fact]
    public void A_block_is_never_shifted() =>
        Run(Doc("<p style=\"vertical-align: super\">x</p>"), "x").Style.BaselineShift.Should().Be(Length.Zero);

    [Fact]
    public void A_shifted_element_inside_another_inline_element_is_refused_and_drawn_with_both_shifts_under_warnings()
    {
        var html = Doc("<p style=\"font-size: 10.5pt\">e<sup>x<sup>2</sup></sup></p>");

        var refusal = Refusal(html);
        refusal.Message.Should().Contain("inside the inline <sup>").And.Contain("whole pixel");
        refusal.Metadata![ErrorMetadataKeys.Property].Should().Be("vertical-align");
        // 14 px is 896/64: 298 + 64; the outer sup's 11.667 px is 747/64: 249 + 64.
        Units(Run(html, "2", UnsupportedPolicy.Warn)).Should().Be(362 + 313);
    }

    [Fact]
    public void A_shifted_element_under_a_decoration_from_around_it_is_refused() =>
        Refusal(Doc("<p style=\"text-decoration: underline\">x<sup>2</sup></p>")).Message.Should().Contain("decoration from around it");

    [Fact]
    public void A_decoration_of_the_shifted_element_itself_or_inside_it_is_drawn()
    {
        var html = Doc("<p>x<sup style=\"text-decoration: underline\">2</sup> y<sup><a href=\"https://example.com/\">3</a></sup></p>");

        Run(html, "2").Style.Decorations.Should().ContainSingle();
        Run(html, "3").Style.Decorations.Should().ContainSingle();
    }

    [Theory]
    [InlineData("<p>x<sup>a<br>b</sup></p>", "<br>")]
    [InlineData("<p>x<sup>a<img src=\"data:,\" alt=\"\" width=\"4\" height=\"4\"></sup></p>", "<img>")]
    public void A_line_break_or_an_image_inside_a_shifted_element_is_refused(string body, string what) =>
        Boxes(Doc(body)).Error.Parts().Should().Contain(error => error.Message.Contains(what) && error.Message.Contains("raised or lowered"));

    [Theory]
    [InlineData("top")]
    [InlineData("middle")]
    [InlineData("bottom")]
    [InlineData("text-top")]
    [InlineData("text-bottom")]
    public void Another_alignment_of_inline_text_is_refused(string align) =>
        Refusal(Doc($"<p>x <span style=\"vertical-align: {align}\">2</span></p>")).Metadata![ErrorMetadataKeys.Property].Should().Be("vertical-align");
}
