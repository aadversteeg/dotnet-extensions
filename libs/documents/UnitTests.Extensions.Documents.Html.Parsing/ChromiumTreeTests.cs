using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.Functional;

namespace UnitTests.Extensions.Documents.Html.Parsing;

/// <summary>
/// The parser against Chromium 159 on the cases of review 39 beyond the vendored suite
/// (<c>tools/reference/chromium/html-tree/cases.tsv</c>, Chromium's trees measured into <c>measured/cases.json</c>):
/// where Chromium differs from the standard, the parser builds Chromium's tree (a NUL in the data state, after
/// <c>&lt;</c> and in CDATA; U+FFFD and frameset-ok; search not special; formatting elements with <c>is</c>; a NUL in a
/// processing instruction; script, object, iframe and embed cloned into selectedcontent; a form context of a fragment).
/// The cases named <c>limit-…</c> are listed limits (docs/architecture.md): the parser keeps the standard, and the test
/// pins that the tree still differs, so a change that removes a limit is seen.
/// </summary>
public class ChromiumTreeTests
{
    private sealed record Case(string Id, string? Fragment, string Data, string Chromium);

    private static readonly Lazy<List<Case>> Cases = new(() =>
    {
        var (cases, trees) = Suites.ChromiumCases();
        using var json = JsonDocument.Parse(File.ReadAllText(trees));
        var measured = json.RootElement.GetProperty("cases").EnumerateArray().ToDictionary(item => item.GetProperty("id").GetString()!, item => item.GetProperty("tree").GetString()!);
        return [.. File.ReadAllLines(cases).Where(line => line.Length > 0 && !line.StartsWith('#')).Select(line => line.Split('\t')).Select(parts =>
            new Case(parts[0], parts[1].Length > 0 ? parts[1] : null, Unescape(parts[2]), measured[parts[0]]))];
    });

    public static TheoryData<string> Ids => [.. Cases.Value.Select(item => item.Id)];

    [Theory]
    [MemberData(nameof(Ids))]
    public void The_tree_is_Chromiums_except_in_a_listed_limit(string id)
    {
        var item = Cases.Value.Single(entry => entry.Id == id);
        var tree = item.Fragment is null
            ? HtmlParser.Parse(item.Data).Match(result => HtmlTreeText.Format(result.Document), error => $"error: {error}\n")
            : HtmlParser.ParseFragment(item.Data, new HtmlFragmentContext(ElementNamespace.Html, item.Fragment)).Match(result => HtmlTreeText.Format(result.Fragment), error => $"error: {error}\n");

        if (id.StartsWith("limit-", StringComparison.Ordinal))
        {
            tree.Should().NotBe(item.Chromium, "a listed limit: when the parser builds Chromium's tree here, remove the limit");
        }
        else
        {
            tree.Should().Be(item.Chromium);
        }
    }

    [Fact]
    public void Every_case_is_measured_and_eight_are_listed_limits()
    {
        Cases.Value.Should().HaveCount(30);
        Cases.Value.Count(item => item.Id.StartsWith("limit-", StringComparison.Ordinal)).Should().Be(8);
    }

    [Fact]
    public void A_nul_before_the_doctype_keeps_the_doctype_and_the_mode()
    {
        // Review 39 HD-1: one stray NUL byte turned an accepted document into a quirks-mode one.
        var parsed = HtmlParser.Parse("\0<!DOCTYPE html><p>x").Match(result => result, error => throw new InvalidOperationException(error.ToString()));

        parsed.Document.Mode.Should().Be(DocumentMode.NoQuirks);
        parsed.Document.Children.OfType<HtmlDocumentType>().Should().ContainSingle();
    }

    [Theory]
    // Chromium 159 drops a NUL of the data state in the tokenizer, so the line feed after it is still the first
    // character after <pre> or <listing>, and is dropped (measured by DOMParser and by page load).
    [InlineData("<!DOCTYPE html><pre>\0\ny</pre>z", "pre", "y")]
    [InlineData("<!DOCTYPE html><listing>\0\ny</listing>z", "listing", "y")]
    [InlineData("<!DOCTYPE html><pre>\0\0\ny</pre>z", "pre", "y")]
    [InlineData("<!DOCTYPE html><pre>\0\r\ny</pre>z", "pre", "y")]
    [InlineData("<!DOCTYPE html><pre>\0&#10;y</pre>z", "pre", "y")]
    [InlineData("<!DOCTYPE html><pre>\0\n\0\ny</pre>z", "pre", "\ny")]
    [InlineData("<!DOCTYPE html><div><pre>\0\n y</pre></div>", "pre", " y")]
    public void A_nul_before_the_line_feed_after_pre_does_not_keep_the_line_feed(string html, string element, string text)
    {
        var parsed = HtmlParser.Parse(html).Match(result => result, error => throw new InvalidOperationException(error.ToString()));
        var pre = Descendants(parsed.Document).OfType<HtmlElement>().First(item => item.LocalName == element);

        string.Concat(pre.Children.OfType<HtmlText>().Select(item => item.Data)).Should().Be(text);
    }

    [Theory]
    // Listed limit (docs/architecture.md, HD-8): a fragment in a foreign context keeps the standard, where the adjusted
    // current node is the context element, so </strong> is ignored. Chromium 159 (innerHTML, measured) gives
    // "<b><strong></strong></b><strong></strong>" for the first three and puts "y" inside the svg for the fourth.
    [InlineData(ElementNamespace.MathMl, "math", "<b><strong></b></strong><strong>", "| <b>\n|   <strong>\n| <strong>\n|   <strong>\n")]
    [InlineData(ElementNamespace.Svg, "title", "<b><strong></b></strong><strong>", "| <b>\n|   <strong>\n| <strong>\n|   <strong>\n")]
    [InlineData(ElementNamespace.Svg, "desc", "<b><strong></b></strong><strong>", "| <b>\n|   <strong>\n| <strong>\n|   <strong>\n")]
    [InlineData(ElementNamespace.MathMl, "mi", "<FOREIGNOBJECT><svg></foreignObject>y", "| <foreignobject>\n|   <svg svg>\n| \"y\"\n")]
    public void A_fragment_in_a_foreign_context_keeps_the_standard_a_listed_limit(ElementNamespace context, string name, string html, string engine) =>
        HtmlParser.ParseFragment(html, new HtmlFragmentContext(context, name)).Match(result => HtmlTreeText.Format(result.Fragment), error => $"error: {error}\n")
            .Should().Be(engine);

    [Theory]
    // Listed limit (docs/architecture.md, HD-10): an <option> after a selectedcontent that is still open. The engine's
    // clones are its own: Chromium 159 (DOMParser, measured) leaves the first two selectedcontent elements empty and
    // puts no embed in the third. The style front-end refuses every such document (select).
    [InlineData("<select>\n<selectedcontent><option value=v></table><embed><embed>", "| <html>\n|   <head>\n|   <body>\n|     <select>\n|       \"\n\"\n|       <selectedcontent>\n|         <embed>\n|         <embed>\n")]
    [InlineData("<p><select><selectedcontent></selectedcontent><selectedcontent><optgroup><option><option selected><selectedcontent>", "| <html>\n|   <head>\n|   <body>\n|     <p>\n|       <select>\n|         <selectedcontent>\n|           <selectedcontent>\n|         <selectedcontent>\n|           <selectedcontent>\n")]
    public void A_selectedcontent_clone_inside_the_option_limit_is_the_engines_own(string html, string engine) =>
        HtmlParser.Parse(html).Match(result => HtmlTreeText.Format(result.Document), error => $"error: {error}\n").Should().Be(engine);

    private static IEnumerable<HtmlNode> Descendants(HtmlNode node)
    {
        var pending = new Stack<HtmlNode>();
        pending.Push(node);
        while (pending.Count > 0)
        {
            var next = pending.Pop();
            yield return next;
            var children = next switch
            {
                HtmlDocument document => document.Children,
                HtmlElement element => element.Children,
                _ => [],
            };
            for (var i = children.Count - 1; i >= 0; i--)
            {
                pending.Push(children[i]);
            }
        }
    }

    private static string Unescape(string text) =>
        Regex.Replace(text, @"\\(\\|u([0-9A-Fa-f]{4}))", match => match.Groups[1].Value == "\\" ? "\\" : ((char)Convert.ToInt32(match.Groups[2].Value, 16)).ToString());
}
