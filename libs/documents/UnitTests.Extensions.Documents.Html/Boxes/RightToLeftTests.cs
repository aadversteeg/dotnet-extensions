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
/// Text that asks for right-to-left text (<see cref="RightToLeftCharacters"/>) is refused by box generation under both
/// policies, at the character's line and column, wherever the text comes from and whatever the font has; the left-to-right
/// formatting characters are accepted (cases <c>bidi-*</c>).
/// </summary>
public class RightToLeftTests
{
    private static readonly HtmlRenderOptions Options = new()
    {
        Fonts = new FontSet([TestFonts.Regular]),
        SourceName = "doc.html",
    };

    private static string Document(string body, string style = "") =>
        $"<!doctype html><html><head><style>@page {{ size: 300pt 200pt; margin: 12pt }} body {{ margin: 0; font-family: Lato }} {style}</style></head><body>{body}</body></html>";

    private static (int Line, int Column) PlaceOf(string text, string needle)
    {
        var index = text.IndexOf(needle, StringComparison.Ordinal);
        var before = text[..index];
        return (before.Count(c => c == '\n') + 1, index - (before.LastIndexOf('\n') + 1) + 1);
    }

    private static (object? Line, object? Column) Place(Error error) =>
        (error.Metadata![ErrorMetadataKeys.SourceLine], error.Metadata![ErrorMetadataKeys.SourceColumn]);

    private static List<Error> Errors(string html, UnsupportedPolicy policy = UnsupportedPolicy.Refuse)
    {
        var result = HtmlRendering.ToPages(html, Options with { Style = HtmlStyleOptions.Default with { Unsupported = policy } });
        result.IsFailure.Should().BeTrue();
        return [.. result.Error.Parts()];
    }

    [Theory]
    [InlineData("&#x202B;", "U+202B", UnsupportedPolicy.Refuse)]
    [InlineData("&#x202E;", "U+202E", UnsupportedPolicy.Refuse)]
    [InlineData("&#x2067;", "U+2067", UnsupportedPolicy.Refuse)]
    [InlineData("&#x200F;", "U+200F", UnsupportedPolicy.Refuse)]
    [InlineData("&#x61C;", "U+061C", UnsupportedPolicy.Refuse)]
    [InlineData("&#x5D0;", "U+05D0", UnsupportedPolicy.Refuse)]
    [InlineData("&#x627;", "U+0627", UnsupportedPolicy.Refuse)]
    [InlineData("&#x660;", "U+0660", UnsupportedPolicy.Refuse)]
    [InlineData("&#x202E;", "U+202E", UnsupportedPolicy.Warn)]
    [InlineData("&#x200F;", "U+200F", UnsupportedPolicy.Warn)]
    [InlineData("&#x5D0;", "U+05D0", UnsupportedPolicy.Warn)]
    public void A_character_that_asks_for_right_to_left_text_is_refused_at_its_line_and_column_under_both_policies(string written, string codePoint, UnsupportedPolicy policy)
    {
        var html = Document($"<p>one</p>\n<p>x {written}abc y</p>");

        var first = Errors(html, policy)[0];

        first.Code.Should().Be(DocumentErrorCodes.Input.Unsupported.RightToLeft);
        first.Metadata![ErrorMetadataKeys.Value].Should().Be(codePoint);
        first.Metadata![ErrorMetadataKeys.SourceFile].Should().Be("doc.html");
        Place(first).Should().Be(((object?)PlaceOf(html, written).Line, (object?)PlaceOf(html, written).Column));
        first.Message.Should().Contain(codePoint).And.Contain("left to right only").And.EndWith("remove it.");
    }

    [Theory]
    [InlineData("&#x202A;abc&#x202C;")]
    [InlineData("&#x202D;abc&#x202C;")]
    [InlineData("&#x2066;abc&#x2069;")]
    [InlineData("&#x2068;abc (1)&#x2069;")]
    [InlineData("abc&#x200E;def")]
    [InlineData("abc&#x202C; &#x2069;")]
    public void A_left_to_right_formatting_character_is_accepted(string written)
    {
        var result = HtmlRendering.ToPages(Document($"<p>x {written} y</p>"), Options);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        result.Value.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void The_refusal_does_not_depend_on_the_font()
    {
        // A caller's rule that says every face has every character: the Hebrew letter is refused all the same.
        var html = Document("<p>x &#x5D0; y</p>");
        var result = HtmlParser.Parse(html, new HtmlParseOptions("doc.html"))
            .OnSuccessBind(parsed => HtmlStyleResolver.Resolve(parsed, new HtmlStyleOptions()))
            .OnSuccessBind(styled => HtmlBoxGenerator.Generate(styled, new HtmlBoxOptions
            {
                Fonts = new FontSet([TestFonts.Regular]),
                MissingCharacters = (_, _) => [],
            }));

        result.IsFailure.Should().BeTrue();
        result.Error.Parts().Single().Code.Should().Be(DocumentErrorCodes.Input.Unsupported.RightToLeft);
    }

    [Fact]
    public void One_text_is_refused_once_with_how_many_such_characters_it_holds()
    {
        var html = Document("<p>abc &#x200F;!?&#x200F; def</p>");

        var refusal = Errors(html).Single();

        refusal.Metadata![ErrorMetadataKeys.Count].Should().Be(2);
        refusal.Message.Should().Contain("(2 such characters in this text)");
    }

    [Fact]
    public void Generated_text_is_refused_once_at_its_content_declaration()
    {
        var html = Document("<p class=\"g\">a</p><p class=\"g\">b</p>", ".g::before { content: \"x \\202E y\" }");

        var refusal = Errors(html).Single();

        refusal.Code.Should().Be(DocumentErrorCodes.Input.Unsupported.RightToLeft);
        Place(refusal).Should().Be(((object?)PlaceOf(html, "content").Line, (object?)PlaceOf(html, "content").Column));
    }

    [Fact]
    public void A_marker_s_content_is_refused_once_at_its_declaration()
    {
        var html = Document("<ul><li>one</li><li>two</li></ul>", "li::marker { content: \"\\2067 - \" }");

        var refusal = Errors(html).Single();

        refusal.Metadata![ErrorMetadataKeys.Value].Should().Be("U+2067");
        Place(refusal).Should().Be(((object?)PlaceOf(html, "content").Line, (object?)PlaceOf(html, "content").Column));
    }

    [Fact]
    public void A_page_margin_box_s_text_is_refused_at_its_declaration()
    {
        var html = Document("<p>a</p>", "@page { @top-center { font-family: Lato; content: \"x \\660\" } }");

        // Lato has no U+0660 either: the glyph's refusal comes after.
        var errors = Errors(html);
        errors.Select(error => error.Code).Should().Equal(DocumentErrorCodes.Input.Unsupported.RightToLeft, FontErrorCodes.GlyphMissing);
        var refusal = errors[0];

        refusal.Metadata![ErrorMetadataKeys.Value].Should().Be("U+0660");
        Place(refusal).Should().Be(((object?)PlaceOf(html, "content").Line, (object?)PlaceOf(html, "content").Column));
    }

    [Fact]
    public void A_title_that_is_not_laid_out_is_not_checked()
    {
        // The title goes to the document's metadata, which a reader shows in its own direction; it is not printed.
        var result = HtmlRendering.ToPages("<!doctype html><html><head><title>&#x5D0;&#x202E;</title><style>body { font-family: Lato }</style></head><body><p>a</p></body></html>", Options);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
    }
}
