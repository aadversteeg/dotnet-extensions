using System.Text;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace UnitTests.Extensions.Documents.Html.Parsing;

public class HtmlParserTests
{
    private static HtmlParseResult Parsed(string text, string name = "test.html") =>
        HtmlParser.Parse(text, new HtmlParseOptions(name)).Match(result => result, error => throw new InvalidOperationException(error.ToString()));

    private static Error Failed<T>(Result<T, Error> result) =>
        result.Match(value => throw new InvalidOperationException($"Expected a failure, got {value}."), error => error);

    private static HtmlElement Body(HtmlDocument document) =>
        document.DocumentElement.Children.OfType<HtmlElement>().Single(element => element.IsHtml("body"));

    [Fact]
    public void Every_node_carries_the_line_and_column_where_it_was_written()
    {
        var document = Parsed("<!DOCTYPE html>\r\n<p class=x>ab<!--c--></p>\n  <span>d</span>").Document;
        var body = Body(document);
        var paragraph = (HtmlElement)body.Children[0];

        document.Children.OfType<HtmlDocumentType>().Single().Position.Should().Be(new TextPosition(1, 1));
        paragraph.Position.Should().Be(new TextPosition(2, 1));
        paragraph.Attributes.Single().Position.Should().Be(new TextPosition(2, 4));
        paragraph.Children[0].Should().Be(new HtmlText("ab", new TextPosition(2, 12)));
        paragraph.Children[1].Should().Be(new HtmlComment("c", new TextPosition(2, 14)));
        body.Children.OfType<HtmlElement>().Single(element => element.IsHtml("span")).Position.Should().Be(new TextPosition(3, 3));
    }

    [Fact]
    public void An_implied_element_takes_the_position_of_the_token_that_made_the_parser_insert_it()
    {
        var document = Parsed("\n\n  <td>x").Document;
        var body = Body(document);
        var cell = body.Children.OfType<HtmlElement>();

        document.DocumentElement.Position.Should().Be(new TextPosition(3, 3));
        body.Position.Should().Be(new TextPosition(3, 3));
        cell.Should().BeEmpty("a td start tag in body is ignored, and its text goes into the body");
        body.Children.Single().Should().Be(new HtmlText("x", new TextPosition(3, 7)));
    }

    [Fact]
    public void A_reopened_or_cloned_formatting_element_takes_the_position_of_the_token_that_reopened_it()
    {
        // Review 39 HR-P1: the second b is made for the line feed that ends line 1 (it holds "\ny"), not at the <b>.
        var body = Body(Parsed("<p><b>x</p>\ny").Document);
        var reopened = body.Children.OfType<HtmlElement>().Single(element => element.IsHtml("b"));
        reopened.Position.Should().Be(new TextPosition(1, 12));

        // The adoption agency algorithm's new element is placed at the end tag that runs it.
        var adopted = Body(Parsed("<b>1<p>2</b>3").Document).Children.OfType<HtmlElement>().Single(element => element.IsHtml("p"))
            .Children.OfType<HtmlElement>().Single(element => element.IsHtml("b"));
        adopted.Position.Should().Be(new TextPosition(1, 9));
    }

    [Fact]
    public void A_column_counts_utf16_code_units()
    {
        // U+1F600 takes two code units, so the b after it is at column 4 of line 1 (after "<p>" it is column 6).
        var paragraph = (HtmlElement)Body(Parsed("<p>\U0001F600<b>x</b>").Document).Children[0];

        paragraph.Children[1].Position.Should().Be(new TextPosition(1, 6));
    }

    [Fact]
    public void A_parse_error_is_a_diagnostic_with_its_standard_name_and_place()
    {
        var result = Parsed("<!DOCTYPE html>\n<p>I'm &notit; here", "letter.html");

        var diagnostic = result.Diagnostics.Single();
        diagnostic.Code.Should().Be(HtmlErrorCodes.Parse.Syntax);
        diagnostic.Metadata![ErrorMetadataKeys.Value].Should().Be("missing-semicolon-after-character-reference");
        diagnostic.Metadata![ErrorMetadataKeys.SourceFile].Should().Be("letter.html");
        diagnostic.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(2);
        diagnostic.Metadata![ErrorMetadataKeys.SourceColumn].Should().Be(12);
        result.SourceName.Should().Be("letter.html");
    }

    [Fact]
    public void Tree_construction_errors_are_diagnostics_too_and_the_parse_goes_on()
    {
        var result = Parsed("<p>a</div>b");

        result.Diagnostics.Select(error => error.Metadata![ErrorMetadataKeys.Value]).Should().Equal("missing-doctype", "unexpected-end-tag");
        ((HtmlText)((HtmlElement)Body(result.Document).Children[0]).Children[0]).Data.Should().Be("ab");
        result.Document.Mode.Should().Be(DocumentMode.Quirks);
    }

    [Fact]
    public void A_text_longer_than_the_limit_is_refused()
    {
        var error = Failed(HtmlParser.Parse(new string('a', HtmlInputLimits.MaxCharacters + 1)));

        error.Code.Should().Be(HtmlErrorCodes.TooLarge);
    }

    [Fact]
    public void Elements_nested_deeper_than_the_limit_are_refused_at_the_first_one_past_it()
    {
        var text = new StringBuilder("<!DOCTYPE html>");
        for (var i = 0; i < 600; i++)
        {
            text.Append("<div>\n");
        }

        // html is the first open element, body the second: the 511th div is the 513th element on the stack.
        var error = Failed(HtmlParser.Parse(text.ToString(), new HtmlParseOptions("deep.html")));

        error.Code.Should().Be(HtmlErrorCodes.TooDeep);
        error.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(511);
        error.Metadata![ErrorMetadataKeys.Value].Should().Be(HtmlInputLimits.MaxDepth);
    }

    [Fact]
    public void Nesting_up_to_the_limit_is_parsed()
    {
        var text = string.Concat(Enumerable.Repeat("<div>", HtmlInputLimits.MaxDepth - 2));

        HtmlParser.Parse(text).Match(_ => true, _ => false).Should().BeTrue();
    }

    [Fact]
    public void A_template_has_contents_and_other_elements_have_none()
    {
        var head = Parsed("<template><p>x</template>").Document.DocumentElement.Children.OfType<HtmlElement>().First();
        var template = (HtmlElement)head.Children[0];

        template.TemplateContent!.Children.OfType<HtmlElement>().Single().IsHtml("p").Should().BeTrue();
        template.Children.Should().BeEmpty();
        head.TemplateContent.Should().BeNull();
    }

    [Fact]
    public void Foreign_elements_keep_their_namespace_and_adjusted_names()
    {
        var svg = (HtmlElement)Body(Parsed("<svg viewbox='0 0 1 1' xlink:href=a><foreignobject/></svg>").Document).Children[0];

        svg.Namespace.Should().Be(ElementNamespace.Svg);
        svg.Attributes.Select(attribute => (attribute.Namespace, attribute.LocalName)).Should().Equal((AttributeNamespace.None, "viewBox"), (AttributeNamespace.XLink, "href"));
        ((HtmlElement)svg.Children[0]).LocalName.Should().Be("foreignObject");
    }

    [Fact]
    public void A_processing_instruction_becomes_a_node()
    {
        var body = Body(Parsed("<body><?php echo 1?>").Document);

        body.Children.Single().Should().Be(new HtmlProcessingInstruction("php", "echo 1", new TextPosition(1, 7)));
    }

    [Fact]
    public void A_fragment_is_parsed_as_the_content_of_its_context()
    {
        var fragment = HtmlParser.ParseFragment("<td>x</td>", new HtmlFragmentContext(ElementNamespace.Html, "tr"))
            .Match(result => result.Fragment, error => throw new InvalidOperationException(error.ToString()));

        HtmlTreeText.Format(fragment).Should().Be("| <td>\n|   \"x\"\n");
    }

    [Fact]
    public void Two_parses_of_the_same_text_give_equal_trees() =>
        Parsed("<table><tr><td>a<b>b</table>c").Should().Be(Parsed("<table><tr><td>a<b>b</table>c"));

    // ---- selectedcontent (forms §4.10.17) and its budget ----

    [Fact]
    public void The_selected_options_content_is_cloned_into_selectedcontent_scripts_and_frames_too()
    {
        // Chromium 159 clones every element (review 39 HD-12, measured): tools/reference/chromium/html-tree, hd12-clone.
        var text = "<select><button><selectedcontent></selectedcontent></button><option>a<script>s</script><b>b</b><option selected>c<iframe></iframe>d</select>";

        var select = (HtmlElement)Body(Parsed(text).Document).Children[0];
        var selectedContent = (HtmlElement)((HtmlElement)select.Children[0]).Children[0];

        selectedContent.Children.Select(node => node switch { HtmlText t => t.Data, HtmlElement e => $"<{e.LocalName}>", _ => "?" }).Should().Equal("c", "<iframe>", "d");
    }

    [Fact]
    public void Cloning_into_many_selectedcontent_elements_is_refused_past_the_budget()
    {
        var text = "<select><button>" + string.Concat(Enumerable.Repeat("<selectedcontent></selectedcontent>", 1000)) + "</button><option selected>" + new string('x', 1000) + "</select>";

        var error = Failed(HtmlParser.Parse(text));

        error.Code.Should().Be(HtmlErrorCodes.TooLarge);
    }
}
