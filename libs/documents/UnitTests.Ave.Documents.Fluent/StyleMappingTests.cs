using Ave.Documents.DocumentModel;
using Ave.Documents.Fluent;
using static Ave.Documents.Fluent.Elements;

namespace UnitTests.Ave.Documents.Fluent;

/// <summary>How the front-end's modifiers and defaults become computed values in the document model.</summary>
public class StyleMappingTests
{
    private static Box Only(Element element) =>
        (Box)DocumentTranslator.ToModel(Document([element]).FontFamily("Lato")).Value.Root.Children.Single();

    [Fact]
    public void Keep_with_next_avoids_a_break_after_and_keep_with_previous_a_break_before()
    {
        // CSS Fragmentation 3 §3.1: break-after: avoid / break-before: avoid.
        Only(Paragraph("x").KeepWithNext()).Style.Should().Match<BoxStyle>(style => style.BreakAfter == BreakBetween.Avoid && style.BreakBefore == BreakBetween.Auto);
        Only(Paragraph("x").KeepWithPrevious()).Style.Should().Match<BoxStyle>(style => style.BreakBefore == BreakBetween.Avoid && style.BreakAfter == BreakBetween.Auto);
    }

    [Theory]
    [InlineData(1, 2.0)]
    [InlineData(2, 1.5)]
    [InlineData(3, 1.17)]
    [InlineData(4, 1.0)]
    [InlineData(5, 0.83)]
    [InlineData(6, 0.67)]
    public void Headings_have_the_HTML_user_agent_sizes(int level, double factor)
    {
        // HTML Standard, rendering, "Sections and headings": h1 2em, h2 1.5em, h3 1.17em, h4 1em, h5 0.83em, h6 0.67em
        // of the 12pt initial size.
        var heading = Only(Heading(level, "x"));

        var run = (TextRun)heading.Children.Single();
        run.Style.FontSize.Points.Should().BeApproximately(12 * factor, 1e-9);
    }
}
