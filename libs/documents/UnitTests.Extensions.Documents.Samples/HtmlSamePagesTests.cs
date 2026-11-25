using System.Globalization;
using System.Text.RegularExpressions;
using Ave.Extensions.Documents;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.Samples;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Samples;

/// <summary>
/// The timesheet from HTML has the pages of the timesheet the code front-end builds from the same data. Not the same
/// page model: the HTML profile puts rows, line boxes and inline boxes on whole CSS pixels, as Chromium prints them,
/// and the code front-end keeps exact points. So, per data set: the same number of pages; the same entries on each
/// page, every exception pinned below by name; every text of the body the same and in the same order, each within
/// 1 pt horizontally of its twin; the step from one row to the next within 0.75 pt (1 px) of the code-built one, per
/// row, as the difference adds up down the page; and the page furniture (header, footer, and the table head repeated on a
/// later page) the same by its words only, in any order (built differently: margin boxes in HTML, header and footer
/// areas in code; and the repeated head on page 5 of the long month follows the moved entry).
/// </summary>
public class HtmlSamePagesTests
{
    /// <summary>
    /// The pages where the HTML timesheet holds other entries than the code-built one: (data set, page) → entries from
    /// HTML and from code. A text row is 21 px = 15.75 pt from HTML against 16.0 pt from code, a row with a description
    /// 39 px = 29.25 pt against 29.6 pt, and a row of two lines 53.33 px = 40.0 pt against 40.71 pt: each HTML row is
    /// 0.25 to 0.72 pt shorter. On page 4 of the long month that leaves room for one more entry, so page 4 holds 26
    /// entries from HTML and 25 from code, and page 5 the rest: 17 against 18.
    /// </summary>
    private static readonly Dictionary<(string DataSet, int Page), (int Html, int Code)> Moved = new()
    {
        [("long", 4)] = (26, 25),
        [("long", 5)] = (17, 18),
    };

    private const double Horizontal = 1.0;

    private const double RowStep = 0.75;

    [Theory]
    [InlineData("edge")]
    [InlineData("normal")]
    [InlineData("long")]
    public void The_HTML_timesheet_has_the_pages_of_the_code_built_one(string dataSet) =>
        string.Join("\n", Differences(dataSet, SampleFiles.Html($"html-timesheet-{dataSet}.nl").Html)).Should().BeEmpty();

    [Fact]
    public void An_entry_on_another_page_than_pinned_fails_the_comparison()
    {
        // Less cell padding makes every HTML row shorter, so entries move to earlier pages.
        var html = SampleFiles.Html("html-timesheet-normal.nl").Html.Replace("padding: 1.5pt 2pt;", "padding: 0.5pt 2pt;", StringComparison.Ordinal);

        Differences("normal", html).Should().Contain(difference => difference.StartsWith("page 1: ", StringComparison.Ordinal) && difference.Contains("entries", StringComparison.Ordinal));
    }

    [Fact]
    public void A_text_that_differs_fails_the_comparison()
    {
        var html = SampleFiles.Html("html-timesheet-normal.nl").Html.Replace("<th scope=\"col\">Taak</th>", "<th scope=\"col\">Taken</th>", StringComparison.Ordinal);

        Differences("normal", html).Should().Contain(difference => difference.Contains("\"Taken\"", StringComparison.Ordinal) && difference.Contains("\"Taak\"", StringComparison.Ordinal));
    }

    /// <summary>Every way the HTML timesheet's pages differ from the code-built ones beyond what is allowed above.</summary>
    private static List<string> Differences(string dataSet, string html)
    {
        var htmlPages = SampleFiles.HtmlPages(new HtmlSample($"html-timesheet-{dataSet}.nl", "timesheet", html)).Output;
        var codePages = SampleFiles.Timesheet(dataSet, "nl-NL").ToPages(TestFonts.SetWithItalic).Value;
        var (fromHtml, fromCode) = (Runs(htmlPages), Runs(codePages));
        var differences = new List<string>();
        if (htmlPages.Pages.Count != codePages.Pages.Count)
        {
            differences.Add($"{htmlPages.Pages.Count} pages from HTML, {codePages.Pages.Count} from code");
            return differences;
        }

        for (var page = 1; page <= htmlPages.Pages.Count; page++)
        {
            var (htmlEntries, codeEntries) = (fromHtml.Count(run => run.Page == page && run.IsEntry), fromCode.Count(run => run.Page == page && run.IsEntry));
            var expected = Moved.TryGetValue((dataSet, page), out var moved) ? moved : (Html: codeEntries, Code: codeEntries);
            if ((htmlEntries, codeEntries) != expected)
            {
                differences.Add($"page {page}: {htmlEntries} entries from HTML, {codeEntries} from code; expected {expected.Html} and {expected.Code}");
            }

            var (htmlWords, codeWords) = (Words(fromHtml, page), Words(fromCode, page));
            if (htmlWords != codeWords)
            {
                differences.Add($"page {page}: header, footer and repeated table head \"{htmlWords}\" from HTML, \"{codeWords}\" from code");
            }
        }

        var (htmlBody, codeBody) = (fromHtml.Where(run => !run.IsFurniture).ToList(), fromCode.Where(run => !run.IsFurniture).ToList());
        for (var index = 0; index < Math.Max(htmlBody.Count, codeBody.Count); index++)
        {
            if (index >= htmlBody.Count || index >= codeBody.Count || htmlBody[index].Text != codeBody[index].Text)
            {
                differences.Add($"body text {index + 1}: {Describe(htmlBody, index)} from HTML, {Describe(codeBody, index)} from code");
                return differences;
            }

            if (Math.Abs(htmlBody[index].X - codeBody[index].X) > Horizontal)
            {
                differences.Add(Invariant($"body text {index + 1} \"{htmlBody[index].Text}\" at x {htmlBody[index].X:F3} from HTML, {codeBody[index].X:F3} from code"));
            }
        }

        // The rows, by their first cell: each entry's date, then the total.
        var (htmlRows, codeRows) = (htmlBody.Where(run => run.IsRow).ToList(), codeBody.Where(run => run.IsRow).ToList());
        for (var row = 1; row < htmlRows.Count; row++)
        {
            if (htmlRows[row].Page == htmlRows[row - 1].Page && codeRows[row].Page == codeRows[row - 1].Page)
            {
                var (htmlStep, codeStep) = (htmlRows[row].Y - htmlRows[row - 1].Y, codeRows[row].Y - codeRows[row - 1].Y);
                if (Math.Abs(htmlStep - codeStep) > RowStep)
                {
                    differences.Add(Invariant($"row {row + 1} on page {htmlRows[row].Page}: {htmlStep:F3} pt below the row before from HTML, {codeStep:F3} pt from code"));
                }
            }
        }

        return differences;
    }

    /// <summary>A text drawn on a page: where its first glyph starts, and whether it is page furniture: the page header
    /// or footer, or the table head repeated at the top of a page.</summary>
    private sealed record Run(int Page, string Text, double FontSize, double X, double Y, bool IsFurniture)
    {
        /// <summary>The date of an entry: the first cell of its row.</summary>
        public bool IsEntry => !IsFurniture && FontSize == 10 && Date.IsMatch(Text);

        /// <summary>The first cell of a row: an entry's date or the total.</summary>
        public bool IsRow => IsEntry || (!IsFurniture && Text == "Totaal:");
    }

    private static readonly Regex Date = new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.CultureInvariant);

    private static List<Run> Runs(PageDocument document)
    {
        var runs = new List<Run>();
        for (var page = 0; page < document.Pages.Count; page++)
        {
            var furniture = new Stack<bool>();
            foreach (var item in document.Pages[page].Items)
            {
                switch (item)
                {
                    case ArtifactStart start:
                        furniture.Push(start.Kind is ArtifactKind.PageHeader or ArtifactKind.PageFooter or ArtifactKind.Repeated || furniture.Contains(true));
                        break;
                    case ArtifactEnd:
                        furniture.Pop();
                        break;
                    case GlyphRun run when run.Glyphs.Count > 0:
                        runs.Add(new Run(page + 1, string.Concat(run.Glyphs.Select(glyph => glyph.Text)), run.FontSize, run.Origin.X + run.Glyphs[0].X, run.Origin.Y, furniture.Contains(true)));
                        break;
                }
            }
        }

        return runs;
    }

    /// <summary>The words of a page's furniture, sorted: the margin boxes are drawn in another order than the code
    /// front-end's header and footer areas.</summary>
    private static string Words(List<Run> runs, int page) =>
        string.Join(" ", runs.Where(run => run.Page == page && run.IsFurniture)
            .SelectMany(run => run.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Order(StringComparer.Ordinal));

    private static string Describe(List<Run> runs, int index) => index < runs.Count ? $"\"{runs[index].Text}\" on page {runs[index].Page}" : "nothing";

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
