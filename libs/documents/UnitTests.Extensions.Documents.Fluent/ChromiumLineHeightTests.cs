using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Functional;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// Line heights given as a length, a percentage or a number, against Chromium's print (<c>tools/reference/chromium</c>,
/// cases <c>f-length-*</c>, <c>f-letter-*</c>, <c>f-number-1501</c>). Chromium keeps every length of its layout in its
/// layout unit of 1/64 CSS pixel: a length or a percentage is rounded to it, a number times the font size is floored to
/// it. The engine takes a length as it is given, exact, under both values of <see cref="FontMetricRounding"/>. So a
/// value given as an author writes it can put a line on another page than Chromium does, where the page area is close
/// to a whole number of lines; the same value given in Chromium's unit (<see cref="InChromiumUnits"/>, what a front-end
/// that follows Chromium hands the engine) gives Chromium's pages. All with <see cref="FontMetricRounding.WholePixels"/>.
/// </summary>
public class ChromiumLineHeightTests
{
    /// <summary>A line height as an author gives it.</summary>
    public enum Kind
    {
        /// <summary>A length in points.</summary>
        Length,

        /// <summary>A percentage of the font size.</summary>
        Percent,

        /// <summary>A number, times the font size.</summary>
        Number,
    }

    /// <summary>
    /// The line height in Chromium's layout unit, 1/64 CSS pixel (1/96 inch): a length or a percentage (computed to a
    /// length) rounded to the nearest unit, half away from zero (<c>LayoutUnit::FromFloatRound</c>); a number times the
    /// font size floored to a unit (the line height is computed in floats and truncated to a <c>LayoutUnit</c>). Given to
    /// the engine as a length.
    /// </summary>
    private static Length InChromiumUnits(Kind kind, double value, double fontSize)
    {
        const double PointsPerUnit = 0.75 / 64;
        var units = kind switch
        {
            Kind.Length => Math.Round(value / PointsPerUnit, MidpointRounding.AwayFromZero),
            Kind.Percent => Math.Round(fontSize * value / 100 / PointsPerUnit, MidpointRounding.AwayFromZero),
            _ => Math.Floor(value * fontSize / PointsPerUnit),
        };
        return (units * PointsPerUnit).Pt();
    }

    private sealed record Case(PageSetup Page, double FontSize, Kind Kind, double Value, int Lines, bool Paragraphs = false);

    private static readonly PageSetup Area = Page(300.Pt(), 204.Pt()).Margin(12.Pt());

    private static readonly PageSetup Letter = Page(612.Pt(), 792.Pt()).Margin(36.Pt());

    private static readonly Dictionary<string, Case> Cases = new()
    {
        // Three one-line paragraphs on a page area of 60 pt: 20 pt is 26.6667 px, Chromium's 26.671875 px; three lines are
        // 80.016 px where the area is 80 px.
        ["f-length-three"] = new(Page(300.Pt(), 84.Pt()).Margin(12.Pt()), 10, Kind.Length, 20, 3, Paragraphs: true),
        ["f-length-nine"] = new(Area, 10, Kind.Length, 20, 9),
        ["f-letter-length"] = new(Letter, 12, Kind.Length, 14.4, 60),
        ["f-letter-percent"] = new(Letter, 12, Kind.Percent, 120, 60),
        ["f-letter-number"] = new(Letter, 12, Kind.Number, 1.2, 60),
        ["f-length-15004"] = new(Area, 10, Kind.Length, 15.004, 12),
        ["f-number-1501"] = new(Area, 10, Kind.Number, 1.501, 12),
        // 14.245 pt is 18.9933 px, which Chromium rounds to 19 px: its leading is 2 px and the half above it 1 px.
        ["f-length-floor"] = new(Area, 11, Kind.Length, 14.245, 12),
    };

    private static PrintedPages Print(Case c, Func<ParagraphElement, ParagraphElement> lineHeight)
    {
        ParagraphElement Lines(IEnumerable<int> lines) =>
            lineHeight(Paragraph([.. lines.SelectMany(i => i == lines.First() ? new[] { Text($"x{i}") } : [LineBreak(), Text($"x{i}")])]).FontSize(c.FontSize.Pt()));
        Element[] body = c.Paragraphs
            ? [.. Enumerable.Range(0, c.Lines).Select(i => (Element)Lines([i]))]
            : [Lines(Enumerable.Range(0, c.Lines))];
        var result = DocumentTranslator.ToModel(Document([.. body], c.Page, title: "Title", language: "en").FontFamily("Lato").FontMetricRounding(FontMetricRounding.WholePixels))
            .OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic))
            .OnSuccessMap(PrintedPages.Engine);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    private static Func<ParagraphElement, ParagraphElement> AsWritten(Case c) => c.Kind switch
    {
        Kind.Length => paragraph => paragraph.LineHeight(c.Value.Pt()),
        Kind.Percent => paragraph => paragraph.LineHeightPercent(c.Value),
        _ => paragraph => paragraph.LineHeight(c.Value),
    };

    private static IEnumerable<int> LinesPerPage(PrintedPages pages) => pages.Pages.Select(page => page.Words.Length);

    [Theory]
    [InlineData("f-length-three", new[] { 2, 1 }, new[] { 3 })]
    [InlineData("f-length-nine", new[] { 7, 2 }, new[] { 9 })]
    [InlineData("f-letter-length", new[] { 49, 11 }, new[] { 50, 10 })]
    [InlineData("f-letter-percent", new[] { 49, 11 }, new[] { 50, 10 })]
    [InlineData("f-length-15004", new[] { 12 }, new[] { 10, 2 })]
    [InlineData("f-number-1501", new[] { 12 }, new[] { 10, 2 })]
    public void A_line_height_given_as_written_can_put_lines_on_other_pages_than_Chromium(string caseName, int[] chromium, int[] engine)
    {
        // The limit, pinned: the engine's line is the exact value, Chromium's the value in its unit, and over a page the
        // difference reaches a line where the page area is within it of a whole number of lines (lines per page as stated;
        // f-length-nine: Chromium fits eight and moves two for widows).
        var c = Cases[caseName];

        LinesPerPage(PrintedPages.Chromium(caseName)).Should().Equal(chromium);
        LinesPerPage(Print(c, AsWritten(c))).Should().Equal(engine);
    }

    [Theory]
    [InlineData("f-length-three")]
    [InlineData("f-length-nine")]
    [InlineData("f-letter-length")]
    [InlineData("f-letter-percent")]
    [InlineData("f-letter-number")]
    [InlineData("f-length-15004")]
    [InlineData("f-number-1501")]
    [InlineData("f-length-floor")]
    public void A_line_height_given_in_Chromium_s_unit_gives_Chromium_s_pages_and_baselines(string caseName)
    {
        // The same pages and words, and every baseline where Chromium paints it once the engine's baseline is rounded to a
        // whole pixel as Chromium paints text (from the page area's top); before that rounding they differ by at most the
        // half pixel the painting moves a baseline (0.375 pt; 0 in f-length-15004, f-number-1501 and f-length-floor).
        var c = Cases[caseName];
        var top = c.Page.Margins.Top.Points;
        var chromium = PrintedPages.Chromium(caseName).Pages.SelectMany(page => page.Words).ToList();
        var engine = Print(c, paragraph => paragraph.LineHeight(InChromiumUnits(c.Kind, c.Value, c.FontSize))).Pages.SelectMany(page => page.Words).ToList();

        engine.Select(word => (word.Page, word.Text)).Should().Equal(chromium.Select(word => (word.Page, word.Text)));
        engine.Select(word => Math.Round((word.Baseline - top) / 0.75, MidpointRounding.AwayFromZero) * 0.75 + top).Should().Equal(chromium.Select(word => word.Baseline));
        engine.Zip(chromium).Max(pair => Math.Abs(pair.First.Baseline - pair.Second.Baseline)).Should().BeLessThanOrEqualTo(0.375 + 1e-9);
    }
}
