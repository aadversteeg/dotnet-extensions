using Ave.Documents.Fluent;
using Ave.Extensions.Functional;
using Ave.Documents.Layout;
using Ave.Documents.PageModel;
using Ave.Documents.TestSupport;
using static Ave.Documents.Fluent.Elements;

namespace UnitTests.Ave.Documents.Fluent;

/// <summary>
/// <c>LineBreak()</c>: a forced line break inside a paragraph, as HTML's <c>br</c>. It is the line separator U+2028, a
/// mandatory break (UAX #14, class BK: "Cause a line break (after)"), which draws nothing and needs no glyph.
/// </summary>
public class LineBreakTests
{
    private static PageDocument Pages(params Span[] spans) =>
        DocumentTranslator.ToModel(Document([Paragraph([.. spans])]).FontFamily("Lato"))
            .OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.Set)).Value;

    [Fact]
    public void A_line_break_ends_the_line_even_when_the_rest_would_fit()
    {
        var page = Pages(Text("first"), LineBreak(), Text("second")).Pages.Single();

        var runs = page.Items.OfType<GlyphRun>().ToList();
        runs.Select(run => string.Concat(run.Glyphs.Select(glyph => glyph.Text))).Should().Equal("first", "second");
        runs[1].Origin.Y.Should().BeGreaterThan(runs[0].Origin.Y, "the second word starts a new line");
        runs[1].Origin.X.Should().Be(runs[0].Origin.X);
    }

    [Fact]
    public void Two_line_breaks_leave_an_empty_line()
    {
        var once = Pages(Text("a"), LineBreak(), Text("b")).Pages.Single().Items.OfType<GlyphRun>().ToList();
        var twice = Pages(Text("a"), LineBreak(), LineBreak(), Text("b")).Pages.Single().Items.OfType<GlyphRun>().ToList();

        (twice[1].Origin.Y - twice[0].Origin.Y).Should().BeGreaterThan(once[1].Origin.Y - once[0].Origin.Y);
    }
}
