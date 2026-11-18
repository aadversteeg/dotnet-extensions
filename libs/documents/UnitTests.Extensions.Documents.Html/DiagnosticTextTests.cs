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

    [Fact]
    public void A_shorthand_the_engine_refuses_is_named_as_written()
    {
        var error = Refused("<!DOCTYPE html><p style='border-radius: 4px'>x");

        error.Message.Should().Contain("border-radius").And.NotContain("border-top-left-radius");
    }
}
