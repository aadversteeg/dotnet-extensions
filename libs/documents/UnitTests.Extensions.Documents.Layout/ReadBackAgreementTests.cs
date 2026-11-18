using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// Every measured Chromium case is read by a test (the <c>words</c>, <c>lines</c> and <c>boxes</c> files and
/// eleven old <c>print</c> files were read by none before). The three read-backs of one print must agree: the words that
/// poppler's <c>pdftotext -bbox</c> finds (<c>words.txt</c>) are the words PyMuPDF finds (<c>print.txt</c>), page by
/// page (pdftotext writes them as HTML text, so <c>&amp;apos;</c> is an apostrophe); the filled rectangles of <c>rectangles.py</c> (<c>lines.txt</c>) are the rectangles of <c>positions.py</c>; and
/// every box measured in the page (<c>boxes.txt</c>) has a size, and a cell that holds a word holds a word the print
/// shows. A file damaged in an edit, or measured from another print, fails here. pdftotext leaves out a word that passes the
/// page's edge, which only content taller than the page does.
/// </summary>
public class ReadBackAgreementTests
{
    private static string Measured([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "tools", "reference", "chromium", "measured"));

    private static double Number(string value) => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

    public static TheoryData<string> Cases()
    {
        var cases = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(Measured(), "*.print.txt").Order(StringComparer.Ordinal))
        {
            cases.Add(Path.GetFileName(file)[..^".print.txt".Length]);
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_read_backs_of_a_print_agree(string caseName)
    {
        var print = PrintedPages.Parse(File.ReadAllText(Path.Combine(Measured(), caseName + ".print.txt")));
        var lines = File.ReadAllLines(Path.Combine(Measured(), caseName + ".words.txt")).Where(line => line.Length > 0).Select(line => line.Split(' ', 6)).ToList();

        foreach (var page in print.Pages)
        {
            // pdftotext leaves out a word whose glyphs pass the page's edge (a slice of a line taller than the page, cases
            // a4-*): it finds every word inside the page, and no word PyMuPDF does not.
            var poppler = lines.Where(fields => int.Parse(fields[0], CultureInfo.InvariantCulture) == page.Number).Select(fields => System.Net.WebUtility.HtmlDecode(fields[5])).Order(StringComparer.Ordinal).ToList();
            var inside = page.Words.Where(word => word.Baseline <= page.Height && word.Baseline - word.FontSize >= 0).Select(word => word.Text).Order(StringComparer.Ordinal);
            poppler.Should().Equal(inside, $"{caseName}, page {page.Number}: the words of pdftotext and PyMuPDF inside the page");
            poppler.Except(page.Words.Select(word => word.Text), StringComparer.Ordinal).Should().BeEmpty($"{caseName}, page {page.Number}: pdftotext finds no word PyMuPDF does not");
        }

        lines.Select(fields => int.Parse(fields[0], CultureInfo.InvariantCulture)).Should().OnlyContain(number => number >= 1 && number <= print.Pages.Length, caseName);

        static string Key(int page, double x0, double y0, double x1, double y1) =>
            FormattableString.Invariant($"{page} {x0:0.00} {y0:0.00} {x1:0.00} {y1:0.00}");
        var rectangles = File.ReadAllLines(Path.Combine(Measured(), caseName + ".lines.txt")).Where(line => line.Length > 0).Select(line => line.Split(' '))
            .Select(fields => Key(int.Parse(fields[0], CultureInfo.InvariantCulture), Number(fields[1]), Number(fields[2]), Number(fields[3]), Number(fields[4]))).Order(StringComparer.Ordinal);
        rectangles.Should().Equal(print.Pages.SelectMany(page => page.Rectangles).Select(box => Key(box.Page, box.X0, box.Y0, box.X1, box.Y1)).Order(StringComparer.Ordinal),
            $"{caseName}: the rectangles of rectangles.py and positions.py");

        var words = print.Pages.SelectMany(page => page.Words).Select(word => word.Text).ToHashSet();
        foreach (var box in File.ReadAllLines(Path.Combine(Measured(), caseName + ".boxes.txt")).Where(line => line.Length > 0).Select(line => line.Split(' ', 4)))
        {
            box[0].Should().NotBeEmpty(caseName);
            Number(box[1]).Should().BeGreaterThanOrEqualTo(0, $"{caseName}: the width of {box[0]}");
            Number(box[2]).Should().BeGreaterThanOrEqualTo(0, $"{caseName}: the height of {box[0]}");
            // A cell's text is its text content, line breaks removed ("b0b1b2" for b0<br>b1<br>b2): it starts with a word
            // the print shows.
            if (box.Length == 4 && box[0] is "td" or "th" && box[3].Replace(" ", string.Empty, StringComparison.Ordinal) is { Length: > 0 } text)
            {
                words.Should().Contain(word => text.StartsWith(word, StringComparison.Ordinal), $"{caseName}: the cell {box[3]} starts with a word of the print");
            }
        }
    }
}
