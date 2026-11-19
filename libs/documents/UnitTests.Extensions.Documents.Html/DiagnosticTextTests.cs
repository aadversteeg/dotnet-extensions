using System.Text;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;
using static UnitTests.Extensions.Documents.Html.Styling;

namespace UnitTests.Extensions.Documents.Html;

/// <summary>
/// What a diagnostic says: the count of those left out past the limit, the refusal among them, the substitution
/// whose length cannot be told, and the names the author wrote.
/// </summary>
public class DiagnosticTextTests
{
    private static string Repeat(string text, int n) => new StringBuilder(text.Length * n).Insert(0, text, n).ToString();

    private static IReadOnlyList<Error> Diagnostics(string html) =>
        HtmlStyleResolver.Resolve(Parsed(html), Warn).Match(styled => (IReadOnlyList<Error>)styled.Diagnostics, error => [error]);

    [Fact]
    public void Past_the_limit_the_summary_counts_every_diagnostic_left_out_and_names_the_first_refusal()
    {
        // 20 000 stray end tags: the parser keeps 10 000 parse errors and counts 10 000; the refusal of zoom comes
        // after them. The resolution's summary adds the parser's count to its own.
        var diagnostics = Diagnostics("<!DOCTYPE html><title>t</title>" + Repeat("</b>", 20_000) + "<p style='zoom:2'>x</p>");

        diagnostics.Should().HaveCount(HtmlInputLimits.MaxDiagnostics + 1);
        var summary = diagnostics[^1];
        summary.Code.Should().Be(DocumentErrorCodes.TooManyDiagnostics);
        (summary.Metadata![ErrorMetadataKeys.Value], summary.Metadata[ErrorMetadataKeys.Refusals]).Should().Be((10_001, 1));
        summary.Message.Should().StartWith("10001 more diagnostics are left out, 1 of them refusals (the first: 'zoom: 2'");
        HtmlStyleDiagnostics.ClassOf(summary.Code).Should().Be(DiagnosticClass.Refusal);
    }

    [Fact]
    public void Past_the_limit_the_summary_counts_the_refusals_a_style_sheet_left_out_and_names_the_first_refusal_dropped()
    {
        // A style sheet with 10 001 unknown at-rules: its reader keeps 10 000 and counts one more, a refusal; and two
        // refusals after 20 000 stray end tags: the summary names the first of them.
        var sheet = Diagnostics("<!DOCTYPE html><title>t</title><style>" + Repeat("@foo{}", 10_001) + "</style>")[^1];
        var two = Diagnostics("<!DOCTYPE html><title>t</title>" + Repeat("</b>", 20_000) + "<p style='zoom:2'>x</p><p style='zoom:3'>y</p>")[^1];

        sheet.Metadata![ErrorMetadataKeys.Refusals].Should().Be(1);
        sheet.Message.Should().StartWith("1 more diagnostics are left out, 1 of them refusals");
        two.Metadata![ErrorMetadataKeys.Refusals].Should().Be(2);
        two.Message.Should().Contain("(the first: 'zoom: 2'");
    }

    [Fact]
    public void Past_the_limit_an_event_handler_is_counted_as_a_refusal()
    {
        // The event-handler attributes of an element are refused together; past the limit they are counted as
        // diagnostics left out and as refusals.
        var diagnostics = Diagnostics("<!DOCTYPE html><title>t</title>" + Repeat("</b>", 20_000) + "<p onclick=\"x()\">a</p>");

        var summary = diagnostics[^1];
        summary.Code.Should().Be(DocumentErrorCodes.TooManyDiagnostics);
        (summary.Metadata![ErrorMetadataKeys.Value], summary.Metadata[ErrorMetadataKeys.Refusals]).Should().Be((10_001, 1));
        HtmlStyleDiagnostics.ClassOf(summary.Code).Should().Be(DiagnosticClass.Refusal);
    }

    [Fact]
    public void Past_the_limit_harmless_diagnostics_still_refuse_the_document_and_say_that_none_of_them_refuses()
    {
        var error = Refused("<!DOCTYPE html><title>t</title>" + Repeat("</b>", 10_001) + "<p>x</p>");

        error.Code.Should().Be(DocumentErrorCodes.TooManyDiagnostics);
        error.Message.Should().Be("1 more diagnostics are left out, 0 of them refusals; 10000 are kept.");
    }

    [Theory]
    // One substitution of a value of n characters: up to 8 characters under Chromium's limit the engine's measure
    // cannot tell whether Chromium keeps it, so it is refused; over the limit it is invalid at computed-value time.
    [InlineData(CssInputLimits.MaxSubstitutionLength - 100, null)]
    [InlineData(CssInputLimits.MaxSubstitutionLength - 2, "within a few characters")]
    [InlineData(CssInputLimits.MaxSubstitutionLength + 10, "is longer than")]
    public void A_substitution_near_the_length_limit_is_refused_where_its_measure_cannot_tell(int length, string? message)
    {
        var diagnostics = Diagnostics($"<!DOCTYPE html><style>p{{--a:{new string('a', length)};font-family:var(--a)}}</style><p>x");

        var said = diagnostics.Where(error => error.Message.Contains("var() substitution", StringComparison.Ordinal)).ToList();
        if (message is null)
        {
            said.Should().BeEmpty();
        }
        else
        {
            said.Should().ContainSingle().Which.Message.Should().Contain(message);
        }
    }

    [Fact]
    public void A_user_agent_rule_an_attribute_selects_names_the_attribute()
    {
        var error = Refused("<!DOCTYPE html><html dir=rtl><p>x");

        error.Message.Should().Contain("<html dir=\"rtl\">").And.Contain("direction: rtl");
    }

    /// <summary>The characters a diagnostic carries: its message and the text of its metadata.</summary>
    private static long Characters(Error error) =>
        error.Message.Length + (error.Metadata?.Values.Sum(value => (long)(value.ToString()?.Length ?? 0)) ?? 0);

    [Fact]
    public void A_long_value_on_an_element_the_parser_opens_again_is_quoted_shortened_once_per_copy()
    {
        // The parser opens b again in each of the 1 000 paragraphs and shares the attribute value between the copies:
        // 1 001 refusals of autofocus. Quoting the 50 000 characters whole made 50 314 261 characters of diagnostics.
        var diagnostics = Diagnostics("<!DOCTYPE html><p><b autofocus=\"" + new string('A', 50_000) + "\">x" + Repeat("<p>x", 1_000));

        diagnostics.Count(error => error.Message.Contains("autofocus", StringComparison.Ordinal)).Should().Be(1_001);
        diagnostics.Should().OnlyContain(error => Characters(error) <= 400);
        diagnostics.Sum(Characters).Should().BeLessThan(1_000_000);
        diagnostics.First(error => error.Message.Contains("autofocus", StringComparison.Ordinal)).Message
            .Should().Contain("autofocus=\"" + new string('A', 100) + "…\"");
    }

    [Theory]
    // The source of an image that is not there, the integrity of a linked sheet, a style sheet that is not found, a
    // declaration that is not valid in this profile, applied to every element it matches.
    [InlineData("<p><img alt=x width=10 src=\"VALUE\">")]
    [InlineData("<link rel=stylesheet href=s.css integrity=\"VALUE\"><p>x")]
    [InlineData("<link rel=stylesheet href=\"VALUE\"><p>x")]
    [InlineData("<style>p { width: 1px VALUE }</style><p>x<p>x<p>x")]
    public void A_diagnostic_quotes_at_most_100_characters_of_a_value(string body)
    {
        var diagnostics = Diagnostics("<!DOCTYPE html><title>t</title>" + body.Replace("VALUE", new string('a', 50_000), StringComparison.Ordinal));

        diagnostics.Should().NotBeEmpty();
        diagnostics.Should().OnlyContain(error => Characters(error) <= 400);
    }

    [Fact]
    public void A_shorthand_the_engine_refuses_is_named_as_written()
    {
        var error = Refused("<!DOCTYPE html><p style='border-radius: 4px'>x");

        error.Message.Should().Contain("border-radius").And.NotContain("border-top-left-radius");
    }
    [Fact]
    public void A_declaration_the_engine_ignores_is_reported_once_however_many_elements_it_applies_to()
    {
        // 10,001 paragraphs and one ignored declaration: one diagnostic, so the document is not refused for having
        // more than 10,000 of them (the default mode refuses past the limit).
        var styled = Styled("<!DOCTYPE html><title>t</title><style>p{-webkit-font-smoothing:antialiased;cursor:pointer}</style>" + Repeat("<p>x</p>", 10_001), new HtmlStyleOptions());

        styled.Diagnostics.Count(error => error.Metadata?.GetValueOrDefault(ErrorMetadataKeys.Property) is "-webkit-font-smoothing").Should().Be(1);
        styled.Diagnostics.Count(error => error.Metadata?.GetValueOrDefault(ErrorMetadataKeys.Property) is "cursor").Should().Be(1);
    }

    [Fact]
    public void A_property_the_engine_refuses_is_one_error_at_its_place_however_many_elements_it_applies_to()
    {
        var error = Refused("<!DOCTYPE html><title>t</title><style>\np{-webkit-box-sizing:border-box}</style><p>x<p>x<p>x");

        error.Code.Should().Be(CssErrorCodes.Unsupported.Property);
        error.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(2);
        error.Metadata[ErrorMetadataKeys.Property].Should().Be("-webkit-box-sizing");
    }

    [Fact]
    public void The_same_declaration_written_in_two_places_is_reported_at_each()
    {
        var diagnostics = Diagnostics("<!DOCTYPE html><title>t</title><style>p{cursor:pointer}\ndiv{cursor:pointer}</style><p>x<div>y</div><p>x<div>y</div>");

        diagnostics.Where(error => error.Metadata?.GetValueOrDefault(ErrorMetadataKeys.Property) is "cursor")
            .Select(error => error.Metadata![ErrorMetadataKeys.SourceLine]).Should().Equal(1, 2);
    }
    [Theory]
    // A name of 500 000 characters where the CSS reader or the cascade names it: a property, a custom property in a
    // cycle, a keyword, a unit, a unit in calc(), a pseudo-class, a pseudo-element, a namespace prefix, an at-rule, a
    // media feature, a font-face descriptor, an event handler attribute.
    [InlineData("<style>p{NAME:1}</style><p>x")]
    [InlineData("<style>p{--NAME:var(--NAME)}</style><p>x")]
    [InlineData("<style>p{display:NAME}</style><p>x")]
    [InlineData("<style>p{width:1NAME}</style><p>x")]
    [InlineData("<style>p{width:calc(1NAME + 1px)}</style><p>x")]
    [InlineData("<style>p:NAME{color:red}</style><p>x")]
    [InlineData("<style>p::NAME{color:red}</style><p>x")]
    [InlineData("<style>NAME|p{color:red}</style><p>x")]
    [InlineData("<style>@NAME{p{color:red}}</style><p>x")]
    [InlineData("<style>@media (NAME: 1px){p{color:red}}</style><p>x")]
    [InlineData("<style>@font-face{NAME:1}</style><p>x")]
    [InlineData("<p onNAME=x>x")]
    public void A_diagnostic_quotes_at_most_100_characters_of_a_name(string body)
    {
        var name = new string('n', 500_000);
        var diagnostics = Diagnostics("<!DOCTYPE html><title>t</title>" + body.Replace("NAME", name, StringComparison.Ordinal));

        diagnostics.Should().Contain(error => error.Message.Contains(new string('n', 90) + "…", StringComparison.Ordinal));
        diagnostics.Should().OnlyContain(error => error.Message.Length < 400);
    }
}
