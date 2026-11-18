using System.Text;
using System.Threading;
using Ave.Extensions.Documents.Css;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;
using static UnitTests.Extensions.Documents.Html.Styling;

namespace UnitTests.Extensions.Documents.Html;

/// <summary>
/// No input value makes <see cref="HtmlStyleResolver.Resolve"/> throw or end the process: An+B at the edge of 64 bits,
/// the work limit crossed while <c>:nth-child(of S)</c> counts the siblings, values that var() substitution nests
/// deeper than any parsed value, the longest allowed chain of imports and the deepest allowed document, each on a
/// thread with a 256 KB stack (a stack overflow there ends the test process).
/// </summary>
public class DeepInputTests
{
    private const int SmallStack = 256 * 1024;

    private static string Doc(string style, string body) => $"<!DOCTYPE html><html><head><style>{style}</style></head><body>{body}</body></html>";

    private static string Repeat(string text, int n) => new StringBuilder(text.Length * n).Insert(0, text, n).ToString();

    /// <summary>Resolves on a thread with a 256 KB stack; an exception out of Resolve fails the test.</summary>
    private static Result<StyledHtmlDocument, Error> OnSmallStack(string html, HtmlStyleOptions? options = null)
    {
        Result<StyledHtmlDocument, Error>? result = null;
        Exception? thrown = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    result = Resolve(html, options);
                }
                catch (Exception exception)
                {
                    thrown = exception;
                }
            },
            SmallStack);
        thread.Start();
        thread.Join();
        thrown.Should().BeNull();
        return result!.Value;
    }

    private static StyledHtmlDocument StyledOnSmallStack(string html, HtmlStyleOptions? options = null) =>
        OnSmallStack(html, options).Match(styled => styled, error => throw new InvalidOperationException(error.ToString()));

    private static string Last(StyledHtmlDocument styled, string property) => styled.ById("last").Value(property);

    [Theory]
    [InlineData("p:nth-child(-n-9223372036854775807)")]
    [InlineData("p:nth-last-child(-n - 9223372036854775807)")]
    [InlineData("p:nth-of-type(-1n-9223372036854775807)")]
    [InlineData("p:nth-last-of-type(-n-9223372036854775807)")]
    [InlineData("p:is(:nth-child(-n-9223372036854775807))")]
    [InlineData("p:nth-child(-n-9223372036854775807 of p)")]
    public void An_plus_B_at_the_edge_of_64_bits_matches_without_overflow(string selector)
    {
        var styled = StyledOnSmallStack(Doc(selector + "{color:red}", "<p id=last>x"));

        // No index is -n-9223372036854775807 for an n >= 0; Chromium 159 matches nothing either.
        Last(styled, "color").Should().Be("rgb(0, 0, 0)");
    }

    [Fact]
    public void The_work_limit_crossed_while_nth_child_of_S_counts_the_siblings_is_a_result_not_an_exception()
    {
        // The second look's smallest input: an "of S" list of 9 001 selectors tried on 7 501 siblings crosses the
        // matching work limit inside the count, where the element was missing from the table and a lookup threw.
        var of = string.Join(",", Enumerable.Range(0, 9_000).Select(i => "x" + i)) + ",.z";
        var html = Doc($".z:nth-child(1 of {of}){{margin-top:37px}}", Repeat("<p>", 7_500) + "<p id=last class=z>");

        var result = OnSmallStack(html);

        // Past the work limit resolution stops: the limit is the result, in warn mode too.
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(CssErrorCodes.TooLarge);
    }

    [Theory]
    [InlineData(CssInputLimits.MaxDepth - 1, "1px")]
    [InlineData(CssInputLimits.MaxDepth, "auto")]
    [InlineData(700, "auto")]
    public void A_value_nested_by_var_substitution_is_refused_past_the_depth_limit(int links, string width)
    {
        // --pK: (var(--p(K-1))) nests the value one block deeper per link; calc() adds one more level.
        var css = new StringBuilder("#last{--p0:1px;");
        for (var k = 1; k <= links; k++)
        {
            css.Append($"--p{k}:(var(--p{k - 1}));");
        }

        css.Append($"width:calc(var(--p{links}))}}");
        var result = OnSmallStack(Doc(css.ToString(), "<p id=last>x"), Warn);

        if (width == "auto")
        {
            // A limit: resolution stops, in warn mode too.
            result.Error.Code.Should().Be(CssErrorCodes.TooDeep);
        }
        else
        {
            Last(result.Value, "width").Should().Be(width);
            result.Value.Diagnostics.Should().NotContain(error => error.Code == CssErrorCodes.TooDeep);
        }
    }

    [Fact]
    public void Substitution_that_nests_62_levels_per_link_is_refused_without_recursion()
    {
        // 200 links of 62 parentheses each overflowed the 8 MB main thread in calc(): depth 12 400.
        var wrap = 62;
        var css = new StringBuilder("#last{--p0:1px;");
        for (var k = 1; k <= 200; k++)
        {
            css.Append($"--p{k}:").Append('(', wrap).Append($"var(--p{k - 1})").Append(')', wrap).Append(';');
        }

        css.Append("width:calc(var(--p200))}");
        OnSmallStack(Doc(css.ToString(), "<p id=last>x"), Warn).Error.Code.Should().Be(CssErrorCodes.TooDeep);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void The_longest_allowed_chain_of_imports_is_followed_on_a_256_KB_stack_and_one_more_is_refused(int beyond)
    {
        // The user-agent sheet and the style element count too.
        var chain = CssInputLimits.MaxStyleSheets - 2 + beyond;
        HtmlResource? Sheet(string url)
        {
            var i = int.Parse(url[(url.LastIndexOf('/') + 2)..^4], System.Globalization.CultureInfo.InvariantCulture);
            var next = i + 1 < chain ? $"@import 's{i + 1}.css';" : string.Empty;
            return new HtmlResource(Encoding.UTF8.GetBytes(next + $"#last{{margin-top:{i}px}}"));
        }

        var options = Warn with { Resources = Sheet, BaseUrl = "https://example.org/" };
        var result = OnSmallStack(Doc("@import 's0.css';#last{color:red}", "<p id=last>x"), options);

        if (beyond > 0)
        {
            result.Error.Code.Should().Be(CssErrorCodes.TooLarge);
            return;
        }

        var styled = result.Value;
        Last(styled, "color").Should().Be("rgb(255, 0, 0)");
        // Each sheet's import comes before its own rule, so the deepest sheet's rule comes first in the order of
        // appearance and the first sheet's last: margin-top is the first link's.
        Last(styled, "margin-top").Should().Be("0px");
    }

    [Fact]
    public void The_deepest_allowed_document_with_the_deepest_allowed_style_sheet_resolves_on_a_256_KB_stack()
    {
        // The second look's deep-combo: every construct at its deepest, on an element at the parser's depth limit.
        const int Deep = CssInputLimits.MaxDepth - 2;
        const int Nesting = CssInputLimits.MaxSelectorNesting - 1;
        var css = new StringBuilder();
        css.Append(Repeat(":is(", Nesting)).Append("#last").Append(Repeat(")", Nesting)).Append("{color:red}");
        css.Append("#last{margin-top:").Append(Repeat("calc(", Deep)).Append("1px").Append(Repeat(")", Deep)).Append('}');
        css.Append("@media ").Append(Repeat("(", Deep - 1)).Append("min-width: 1px").Append(Repeat(")", Deep - 1)).Append("{#last{margin-left:2px}}");
        css.Append("@supports ").Append(Repeat("(", Deep / 2)).Append("display:block").Append(Repeat(")", Deep / 2)).Append("{#last{margin-right:3px}}");
        css.Append(Repeat("@media print{", Deep - 2)).Append("#last{padding-top:4px}").Append(Repeat("}", Deep - 2));
        css.Append("div{").Append(Repeat("&{", Nesting)).Append("padding-left:5px").Append(Repeat("}", Nesting)).Append('}');
        css.Append("#last{margin-bottom:").Append(Repeat("var(--x,", Deep - 2)).Append("6px").Append(Repeat(")", Deep - 2)).Append('}');
        css.Append("#last{--d:").Append(Repeat("(", Deep - 1)).Append("var(--x,7px)").Append(Repeat(")", Deep - 1)).Append(";padding-right:var(--d)}");
        css.Append(Repeat(":not(", Nesting)).Append("#last").Append(Repeat(")", Nesting)).Append("{border-top-style:solid}");
        css.Append(Repeat(":is(", Nesting)).Append("div").Append(Repeat(")", Nesting)).Append("{padding-bottom:8px}");
        // html and body are the first two levels, the p the last.
        var html = Doc(css.ToString(), Repeat("<div>", HtmlInputLimits.MaxDepth - 3) + "<p id=last>x");

        var styled = StyledOnSmallStack(html, Warn);

        Last(styled, "color").Should().Be("rgb(255, 0, 0)");
        Last(styled, "margin-top").Should().Be("1px");
        Last(styled, "margin-left").Should().Be("2px");
        Last(styled, "padding-top").Should().Be("4px");
        Last(styled, "margin-bottom").Should().Be("6px");
        styled.ByName("div").Value("padding-left").Should().Be("5px");
        styled.ByName("div").Value("padding-bottom").Should().Be("8px");
    }
}
