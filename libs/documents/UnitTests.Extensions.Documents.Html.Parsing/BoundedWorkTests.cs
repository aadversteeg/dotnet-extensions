using System.Diagnostics;
using System.Text;
using System.Threading;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace UnitTests.Extensions.Documents.Html.Parsing;

/// <summary>
/// The inputs of review 39 that made the parser's work grow faster than its input (B7–B12, M2, M3), at the size the
/// review used and at the size limit (2^24 characters). Before the fix each took from seconds to hours; the bound
/// asserted here is generous (it guards against the quadratic and worse, not against a slow machine).
/// </summary>
public class BoundedWorkTests
{
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(60);

    private static string Attributes(int n, string prefix = "a")
    {
        var text = new StringBuilder(n * 8);
        for (var i = 0; i < n; i++)
        {
            text.Append(' ').Append(prefix).Append(i.ToString("x"));
        }

        return text.ToString();
    }

    private static string Repeat(string text, int n) => new StringBuilder(text.Length * n).Insert(0, text, n).ToString();

    private static HtmlParseResult ParsedWithin(string text)
    {
        text.Length.Should().BeLessThanOrEqualTo(HtmlInputLimits.MaxCharacters);
        var watch = Stopwatch.StartNew();
        var result = HtmlParser.Parse(text);
        watch.Elapsed.Should().BeLessThan(Generous);
        return result.Match(parsed => parsed, error => throw new InvalidOperationException(error.ToString()));
    }

    [Theory]
    [InlineData(100_000)]
    [InlineData(1_900_000)]
    public void Many_attributes_on_one_tag_cost_their_number(int n) =>
        ParsedWithin("<p" + Attributes(n) + ">").Document.DocumentElement!.Children.Should().NotBeEmpty();

    [Theory]
    [InlineData(80_000)]
    [InlineData(1_000_000)]
    public void An_element_with_many_attributes_is_not_searched_again_for_every_token(int n) =>
        ParsedWithin("<math><annotation-xml" + Attributes(n) + ">" + Repeat("x", n)).Diagnostics.Should().NotBeNull();

    [Theory]
    [InlineData(30_000)]
    [InlineData(250_000)]
    public void Many_selectedcontent_elements_and_selected_options_cost_one_fill_each(int n)
    {
        var parsed = ParsedWithin("<select>" + Repeat("<selectedcontent></selectedcontent>", n) + Repeat("<option selected>", n));

        parsed.Document.DocumentElement!.Should().NotBeNull();
    }

    [Theory]
    [InlineData(400_000)]
    [InlineData(830_000)]
    public void Markers_left_on_the_list_of_active_formatting_elements_are_not_scanned_per_tag(int n) =>
        ParsedWithin("<table><tr>" + Repeat("<td><applet>", n) + Repeat("<b></b>", n)).Document.Should().NotBeNull();

    [Theory]
    [InlineData(80_000)]
    [InlineData(900_000)]
    public void The_Noahs_Ark_comparison_costs_the_attributes_once(int n) =>
        ParsedWithin("<b" + Attributes(n) + "><b" + Attributes(n) + ">").Document.Should().NotBeNull();

    [Theory]
    [InlineData(80_000)]
    [InlineData(1_200_000)]
    public void Repeated_html_and_body_attributes_are_merged_through_a_set(int n)
    {
        var text = new StringBuilder();
        for (var i = 0; i < n; i++)
        {
            text.Append("<html a").Append(i.ToString("x")).Append('>');
        }

        ParsedWithin(text.ToString()).Document.DocumentElement!.Attributes.Count.Should().Be(n);
    }

    [Fact]
    public void The_deepest_document_parses_on_a_thread_with_a_256_KB_stack()
    {
        Result<HtmlParseResult, Error>? result = null;
        Result<HtmlParseResult, Error>? cloned = null;
        var thread = new Thread(() =>
        {
            result = HtmlParser.Parse(Repeat("<div>", HtmlInputLimits.MaxDepth - 2));
            cloned = HtmlParser.Parse("<select><button><selectedcontent></selectedcontent></button><option selected>" + Repeat("<div>", HtmlInputLimits.MaxDepth - 6) + "x");
        }, 256 * 1024);
        thread.Start();
        thread.Join();

        result!.Value.IsSuccess.Should().BeTrue();
        cloned!.Value.IsSuccess.Should().BeTrue();
    }

    /// <summary>Runs on a thread with a 256 KB stack (a stack overflow there ends the test process); an exception fails
    /// the test.</summary>
    private static T OnSmallStack<T>(Func<T> work)
    {
        T? result = default;
        Exception? thrown = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    result = work();
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
        return result!;
    }

    [Theory]
    [InlineData("<template><tr>", 255)]
    [InlineData("<template><tbody>", 255)]
    [InlineData("<template><td>", 255)]
    [InlineData(" <template><col>", 509)]
    [InlineData("<template>", 510)]
    public void The_end_of_the_file_inside_nested_templates_is_processed_on_a_256_KB_stack(string unit, int n)
    {
        // The end of the file closes one template at a time and is processed again after each (§13.2.6.4.16): this
        // recursed once per open template.
        var parsed = OnSmallStack(() => HtmlParser.Parse(Repeat(unit, n) + "x"));

        parsed.IsSuccess.Should().BeTrue();
        var templates = 0;
        var pending = new Stack<HtmlNode>(parsed.Value.Document.Children);
        while (pending.Count > 0)
        {
            if (pending.Pop() is HtmlElement element)
            {
                templates += element.TemplateContent is null ? 0 : 1;
                foreach (var child in element.Children.Concat(element.TemplateContent?.Children ?? []))
                {
                    pending.Push(child);
                }
            }
        }

        templates.Should().Be(n);
    }

    [Fact]
    public void The_text_form_of_the_deepest_document_is_written_on_a_256_KB_stack()
    {
        // html, body, the divs, a template and its b: the parser's depth limit.
        const int Divs = HtmlInputLimits.MaxDepth - 4;
        var parsed = HtmlParser.Parse(Repeat("<div>", Divs) + "<template><b>x").Value;

        var text = OnSmallStack(() => HtmlTreeText.Format(parsed.Document));
        var described = OnSmallStack(() => parsed.Document.ToString() + parsed.Document.DocumentElement!.ToString());

        text.Split('\n').Count(line => line.TrimStart('|', ' ') == "<div>").Should().Be(Divs);
        text.Should().Contain("content\n");
        described.Should().Contain("Children = ");
    }

    [Fact]
    public void The_text_form_writes_a_template_and_its_contents_in_the_suites_order()
    {
        var parsed = HtmlParser.Parse("<!DOCTYPE html><template a=1><p>x</p></template><i>y").Value;

        HtmlTreeText.Format(parsed.Document).Should().Be(
            "| <!DOCTYPE html>\n| <html>\n|   <head>\n|     <template>\n|       a=\"1\"\n|       content\n|         <p>\n|           \"x\"\n|   <body>\n|     <i>\n|       \"y\"\n");
    }

    [Fact]
    public void The_number_of_diagnostics_is_bounded_and_the_rest_is_counted()
    {
        var parsed = ParsedWithin(new string('\0', HtmlInputLimits.MaxCharacters));

        parsed.Diagnostics.Count.Should().Be(HtmlInputLimits.MaxDiagnostics + 1);
        var last = parsed.Diagnostics[^1];
        last.Code.Should().Be(Ave.Extensions.Documents.Core.DocumentErrorCodes.TooManyDiagnostics);
        // One unexpected-null-character per NUL (and a few for the missing doctype and elements), all counted.
        ((int)last.Metadata![Ave.Extensions.Documents.Core.ErrorMetadataKeys.Value]! + HtmlInputLimits.MaxDiagnostics).Should().BeGreaterThanOrEqualTo(HtmlInputLimits.MaxCharacters);
    }
}
