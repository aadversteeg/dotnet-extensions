using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Selectors;
using Ave.Extensions.Documents.Css.Syntax;
using static UnitTests.Extensions.Documents.Css.TestTree;

namespace UnitTests.Extensions.Documents.Css;

/// <summary>Every selector form against one tree: which elements (by ID) it matches.</summary>
public class SelectorMatchingTests
{
    private static readonly TestElement Root = E("html", A("id", "html"),
        E("body", A("id", "body"),
            E("div", A("id", "d1"), A("class", "box first"), A("lang", "en-GB"),
                E("p", A("id", "p1"), A("class", "a")),
                E("p", A("id", "p2"), A("class", "b a"), A("title", "Hello World")),
                E("span", A("id", "s1")),
                E("p", A("id", "p3"), A("data-x", "one-two"))),
            E("div", A("id", "d2"), A("class", "Box"),
                E("a", A("id", "a1"), A("href", "x")),
                E("input", A("id", "i1"), A("type", "TEXT")),
                E("em", A("id", "e1")))));

    private static string Matching(string selector)
    {
        var values = SyntaxParser.ParseComponentValues(CssSource.Of(selector), (_, _) => { }).Values;
        var parsed = SelectorParser.Parse(values, relative: false, TextPosition.Start);
        parsed.List.Should().NotBeNull(parsed.Error);
        var matcher = new SelectorMatcher();
        return string.Join(" ", Root.TreeOrder().Where(element => matcher.Matches(parsed.List!, element)).Select(element => element.Id));
    }

    [Theory]
    [InlineData("p", "p1 p2 p3")]
    [InlineData("P", "p1 p2 p3")]
    [InlineData("*|p", "p1 p2 p3")]
    [InlineData("|p", "")]
    [InlineData("#p2", "p2")]
    [InlineData(".a", "p1 p2")]
    [InlineData(".box", "d1")]
    [InlineData("[title]", "p2")]
    [InlineData("[title='Hello World']", "p2")]
    [InlineData("[title='hello world' i]", "p2")]
    [InlineData("[title~=World]", "p2")]
    [InlineData("[data-x|=one]", "p3")]
    [InlineData("[title^=Hell]", "p2")]
    [InlineData("[title$=rld]", "p2")]
    [InlineData("[title*='o W']", "p2")]
    [InlineData("[type=text]", "i1")]
    [InlineData("[type=text s]", "")]
    [InlineData(":root", "html")]
    [InlineData(":empty", "p1 p2 s1 p3 a1 i1 e1")]
    [InlineData("div > :first-child", "p1 a1")]
    [InlineData("div > :last-child", "p3 e1")]
    [InlineData(":only-child", "html body")]
    [InlineData("div > :nth-child(2n+1)", "p1 s1 a1 e1")]
    [InlineData("div > :nth-last-child(1)", "p3 e1")]
    [InlineData(":nth-child(2 of .a)", "p2")]
    [InlineData("p:nth-of-type(2)", "p2")]
    [InlineData("p:nth-last-of-type(1)", "p3")]
    [InlineData("p:first-of-type", "p1")]
    [InlineData("p:last-of-type", "p3")]
    [InlineData("span:only-of-type", "s1")]
    [InlineData("div p", "p1 p2 p3")]
    [InlineData("body > p", "")]
    [InlineData("p + span", "s1")]
    [InlineData("p ~ p", "p2 p3")]
    [InlineData("p:not(.a)", "p3")]
    [InlineData(":is(span, em)", "s1 e1")]
    [InlineData(":where(span, em)", "s1 e1")]
    [InlineData(":is(span, :unknown)", "s1")]
    [InlineData(":link", "a1")]
    [InlineData(":any-link", "a1")]
    [InlineData("p:hover", "")]
    [InlineData("p:not(:hover)", "p1 p2 p3")]
    [InlineData("&", "html")]
    [InlineData("div:has(span)", "")]
    public void A_selector_matches_what_Selectors_4_says(string selector, string expected) =>
        Matching(selector).Should().Be(expected);

    [Theory]
    [InlineData("p", 0, 0, 1)]
    [InlineData("#a.b c", 1, 1, 1)]
    [InlineData(":is(#a, p) .x", 1, 1, 0)]
    [InlineData(":where(#a) p", 0, 0, 1)]
    [InlineData(":not(.a, #b)", 1, 0, 0)]
    [InlineData(":nth-child(2 of #a)", 1, 1, 0)]
    [InlineData("*", 0, 0, 0)]
    public void Specificity_is_counted_as_Selectors_4_says(string selector, int ids, int classes, int types)
    {
        var values = SyntaxParser.ParseComponentValues(CssSource.Of(selector), (_, _) => { }).Values;
        var parsed = SelectorParser.Parse(values, relative: false, TextPosition.Start);

        parsed.List!.Selectors.Single().Specificity.Should().Be(new Specificity(ids, classes, types));
    }

    [Fact]
    public void Unsupported_and_never_matching_selectors_are_reported_by_the_parser()
    {
        var values = SyntaxParser.ParseComponentValues(CssSource.Of("p:hover, div:has(p), a::before"), (_, _) => { }).Values;
        var parsed = SelectorParser.Parse(values, relative: false, TextPosition.Start);

        parsed.Findings.Select(finding => (finding.Support, finding.Name)).Should().Equal(
            (SelectorSupport.NeverMatches, ":hover"), (SelectorSupport.Unsupported, ":has()"), (SelectorSupport.Unsupported, "::before"));
    }
}
