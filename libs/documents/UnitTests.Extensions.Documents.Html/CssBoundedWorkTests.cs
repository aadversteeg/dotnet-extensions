using System.Diagnostics;
using System.Text;
using System.Threading;
using Ave.Extensions.Documents.Css;
using Ave.Extensions.Documents.Html;
using static UnitTests.Extensions.Documents.Html.Styling;

namespace UnitTests.Extensions.Documents.Html;

/// <summary>
/// Style sheets that crashed the process or ran for minutes, through the whole front-end, at the size they were
/// first found at and larger. Each must finish well inside the generous bound, and on a
/// thread with a 256 KB stack where recursion was the problem. The class runs alone, not beside the other test
/// classes, so the bound measures this work and not the load around it.
/// </summary>
[Collection(nameof(CssBoundedWorkTests))]
public class CssBoundedWorkTests
{
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(60);

    private static string Doc(string style, string body) => $"<!DOCTYPE html><html><head><style>{style}</style></head><body>{body}</body></html>";

    private static string Repeat(string text, int n) => new StringBuilder(text.Length * n).Insert(0, text, n).ToString();

    private static string Join(string separator, string item, int n) => string.Join(separator, Enumerable.Repeat(item, n));

    /// <summary>Styles a document on a thread with a 256 KB stack (a stack overflow there ends the test process) within
    /// the bound.</summary>
    private static StyledHtmlDocument StyledOnSmallStack(string html)
    {
        StyledHtmlDocument? styled = null;
        Exception? thrown = null;
        var watch = Stopwatch.StartNew();
        var thread = new Thread(
            () =>
            {
                try
                {
                    styled = Styled(html);
                }
                catch (Exception exception)
                {
                    thrown = exception;
                }
            },
            256 * 1024);
        thread.Start();
        thread.Join();
        thrown.Should().BeNull();
        watch.Elapsed.Should().BeLessThan(Generous);
        return styled!;
    }

    /// <summary>The limit a document reaches, in warn mode, on a thread with a 256 KB stack: resolution stops there and
    /// fails with it, whatever the policy.</summary>
    private static Ave.Extensions.ErrorPaths.Error LimitOnSmallStack(string html)
    {
        Ave.Extensions.Functional.Result<StyledHtmlDocument, Ave.Extensions.ErrorPaths.Error>? result = null;
        var watch = Stopwatch.StartNew();
        var thread = new Thread(() => result = Resolve(html, Warn), 256 * 1024);
        thread.Start();
        thread.Join();
        watch.Elapsed.Should().BeLessThan(Generous);
        result!.Value.IsFailure.Should().BeTrue();
        return result.Value.Error;
    }

    private static string Last(StyledHtmlDocument styled, string property) => styled.ById("last").Value(property);

    [Theory]
    [InlineData(4_000)]
    [InlineData(100_000)]
    [InlineData(1_000_000)]
    public void A_selector_of_many_steps_is_matched_without_recursion(int n)
    {
        var styled = StyledOnSmallStack(Doc(Repeat("*+", n) + "#last{color:red}", Repeat("<p>", Math.Min(n, 100_000)) + "<p id=last>"));

        Last(styled, "color").Should().Be(n <= 100_000 ? "rgb(255, 0, 0)" : "rgb(0, 0, 0)");
    }

    [Theory]
    [InlineData(2_000)]
    [InlineData(20_000)]
    public void Subsequent_sibling_chains_are_linear(int n)
    {
        var styled = StyledOnSmallStack(Doc("x~p~p{color:red} p~p~p{margin-top:1px}", Repeat("<p class=x>", n) + "<p id=last>"));

        Last(styled, "color").Should().Be("rgb(0, 0, 0)");
        Last(styled, "margin-top").Should().Be("1px");
    }

    [Theory]
    [InlineData(20_000)]
    [InlineData(60_000)]
    public void Next_sibling_chains_longer_than_the_siblings_reject_at_once(int n)
    {
        var styled = StyledOnSmallStack(Doc(Join("+", "p", n) + "{color:red}", Repeat("<p>", n) + "<p id=last>"));

        Last(styled, "color").Should().Be("rgb(255, 0, 0)");
    }

    // First found at 12 000; 150 000 is about 1.9 s alone, which leaves the bound room on a loaded machine.
    [Theory]
    [InlineData(12_000)]
    [InlineData(150_000)]
    public void A_chain_of_custom_properties_resolves_without_recursion(int n)
    {
        var declarations = new StringBuilder();
        for (var i = 0; i < n; i++)
        {
            declarations.Append("--p").Append(i).Append(":var(--p").Append(i + 1).Append(");");
        }

        var styled = StyledOnSmallStack(Doc("p{" + declarations + $"--p{n}:red;color:var(--p0)}}", "<p id=last>x"));

        Last(styled, "color").Should().Be("rgb(255, 0, 0)");
    }

    [Theory]
    [InlineData(22)]
    [InlineData(31)]
    public void Nested_rules_that_name_their_parent_twice_do_not_double_the_work(int levels)
    {
        var styled = StyledOnSmallStack(Doc("p{" + Repeat("&&{", levels) + "color:red" + Repeat("}", levels) + "}", "<p id=last>x"));

        Last(styled, "color").Should().Be("rgb(255, 0, 0)");
    }

    [Fact]
    public void Selectors_nested_past_the_limit_are_refused_not_matched()
    {
        var pre = Repeat(":is(", 200) + "&" + Repeat(")", 200);
        var html = Doc("p{" + Repeat(pre + "{", 30) + "color:red" + Repeat("}", 30) + "}", "<p id=last>x");

        LimitOnSmallStack(html).Code.Should().Be(CssErrorCodes.TooDeep);
    }

    [Theory]
    [InlineData(22)]
    [InlineData(40)]
    public void A_doubling_var_chain_inside_calc_stops_at_Chromiums_limit(int levels)
    {
        var css = new StringBuilder("#last{--l0:1px;");
        for (var k = 1; k <= levels; k++)
        {
            css.Append($"--l{k}:calc(var(--l{k - 1}) + var(--l{k - 1}));");
        }

        css.Append($"width:var(--l{levels})}}");
        var styled = StyledOnSmallStack(Doc(css.ToString(), "<p id=last>x"));

        // Chromium 159: auto (the declaration is invalid at computed-value time; measured).
        Last(styled, "width").Should().Be("auto");
    }

    [Fact]
    public void A_long_word_list_is_split_once_per_element()
    {
        // 4 000 rules against one 500 KB word list: before the fix each rule split the list again (36 s for 1 000).
        var rules = string.Concat(Enumerable.Range(0, 4_000).Select(i => $"[title~='b{i}']{{color:red}}"));
        var styled = StyledOnSmallStack(Doc(rules, $"<p id=last title='{Repeat("a ", 250_000)}'>"));

        Last(styled, "color").Should().Be("rgb(0, 0, 0)");
    }

    [Fact]
    public void Many_classes_against_many_class_rules_cost_their_sum()
    {
        // The input as first found had 100 000 of each (36 s before the fix); 150 000 is about 4 s now.
        var rules = string.Concat(Enumerable.Range(0, 150_000).Select(i => $".c{i}{{margin-top:1px}}"));
        var styled = StyledOnSmallStack(Doc(rules, $"<p id=last class='{string.Join(' ', Enumerable.Range(0, 150_000).Select(i => "c" + i))}'>"));

        Last(styled, "margin-top").Should().Be("1px");
    }

    [Fact]
    public void Many_rules_with_is_against_many_elements_stay_inside_the_work_limit()
    {
        var styled = StyledOnSmallStack(Doc(Repeat("p:is(.x,.y,.z){color:red}", 1_000), Repeat("<p class=x>", 5_000) + "<p id=last>"));

        Last(styled, "color").Should().Be("rgb(0, 0, 0)");
        styled.Diagnostics.Should().NotContain(error => error.Code == CssErrorCodes.TooLarge);
    }

    [Fact]
    public void Many_rules_with_is_against_more_elements_end_at_the_work_limit_in_a_failure()
    {
        // 1 000 rules against 20 000 elements took 45 s before the work limit; past it resolution stops, in warn mode too.
        var html = Doc(Repeat("p:is(.x,.y,.z){color:red}", 1_000), Repeat("<p class=x>", 20_000) + "<p id=last>");

        LimitOnSmallStack(html).Code.Should().Be(CssErrorCodes.TooLarge);
    }

    [Fact]
    public void A_style_sheet_nested_past_the_limit_is_refused_on_a_256_KB_stack()
    {
        // 252 nested @media overflowed a 256 KB stack at the old limit of 256 (the process died); past 64 it is refused.
        var css = Repeat("@media print{", 252) + "#last{padding-top:4px}" + Repeat("}", 252);

        LimitOnSmallStack(Doc(css, "<p id=last>x")).Code.Should().Be(CssErrorCodes.TooDeep);
    }

    [Fact]
    public void The_deepest_allowed_style_sheet_constructs_run_on_a_256_KB_stack()
    {
        const int Deep = CssInputLimits.MaxDepth - 2;
        var css = new StringBuilder();
        css.Append(Repeat(":is(", CssInputLimits.MaxSelectorNesting - 1)).Append("#last").Append(Repeat(")", CssInputLimits.MaxSelectorNesting - 1)).Append("{color:red}");
        css.Append("#last{margin-top:").Append(Repeat("calc(", Deep)).Append("1px").Append(Repeat(")", Deep)).Append('}');
        css.Append("@media ").Append(Repeat("(", Deep - 1)).Append("min-width: 1px").Append(Repeat(")", Deep - 1)).Append("{#last{margin-left:2px}}");
        css.Append("@supports ").Append(Repeat("(", Deep / 2)).Append("display:block").Append(Repeat(")", Deep / 2)).Append("{#last{margin-right:3px}}");
        css.Append(Repeat("@media print{", Deep - 2)).Append("#last{padding-top:4px}").Append(Repeat("}", Deep - 2));
        css.Append("div{").Append(Repeat("&{", CssInputLimits.MaxSelectorNesting - 1)).Append("padding-left:5px").Append(Repeat("}", CssInputLimits.MaxSelectorNesting - 1)).Append('}');
        css.Append("#last{margin-bottom:").Append(Repeat("var(--x,", Deep - 2)).Append("6px").Append(Repeat(")", Deep - 2)).Append('}');
        var styled = StyledOnSmallStack(Doc(css.ToString(), "<div><p id=last>x</p></div>"));

        Last(styled, "color").Should().Be("rgb(255, 0, 0)");
        Last(styled, "margin-top").Should().Be("1px");
        Last(styled, "margin-left").Should().Be("2px");
        Last(styled, "padding-top").Should().Be("4px");
        Last(styled, "margin-bottom").Should().Be("6px");
        styled.ByName("div").Value("padding-left").Should().Be("5px");
    }
}

/// <summary>The collection of <see cref="CssBoundedWorkTests"/>: it runs after the parallel collections, on its own.</summary>
[CollectionDefinition(nameof(CssBoundedWorkTests), DisableParallelization = true)]
public class CssBoundedWorkCollection
{
}
