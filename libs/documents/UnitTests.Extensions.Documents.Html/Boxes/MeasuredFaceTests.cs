using System.IO;
using System.Text.RegularExpressions;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Html.Boxes;

/// <summary>
/// The faces in Chromium's measured prints are the fonts the cases load. Chromium prints with a fallback font, without a
/// word, when a font file of a case does not load (measured: a copy of the reference tool without the family's
/// <c>assets/fonts</c> beside it printed seven cases in Liberation Serif), and a print made that way holds other widths and
/// baselines than the case asks for. So every face a print names must be the PostScript name of a font file in the
/// repository that the case loads: a <c>url(…)</c> in the case or in a style sheet it links, resolved from the file it is
/// written in. The one exception is a fallback face that is itself the measured fact: Chromium prints the text of a
/// face that only a page-margin box uses in its fallback face (<see cref="Fallback"/>).
/// </summary>
public partial class MeasuredFaceTests
{
    /// <summary>The cases whose print holds a fallback face on purpose, with that face: a face that only a page-margin
    /// box uses, which Chromium has not loaded when it prints the margin boxes (listed,
    /// <see cref="HtmlListedDifferenceTests.A_face_that_only_a_margin_box_uses_is_drawn_where_Chromium_prints_its_fallback_face"/>).</summary>
    private static readonly IReadOnlyDictionary<string, string> Fallback = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["pm-family-unused"] = "LiberationSerif-Bold",
    };

    [Fact]
    public void Every_face_in_a_measured_print_is_a_font_file_the_case_loads()
    {
        var wrong = HtmlCases.Names.SelectMany(name =>
            Wrong(name, PrintedPages.Parse(File.ReadAllText(Path.Combine(HtmlCases.Folder(), "..", "measured", name + ".print.txt"))))).ToList();

        wrong.Should().BeEmpty("a print made with a fallback font is not the print of the case");
    }

    [Fact]
    public void A_print_in_a_fallback_face_fails_the_check()
    {
        // The word as the print of this case held it when Lato did not load: Liberation Serif's advances, 21.984 pt.
        var print = PrintedPages.Parse("page 1 300.000 204.000\nword 1 12.000 33.984 24.000 12.000 after #000000 LiberationSerif\n");

        Wrong("a4-cell-image-179-5", print).Should().Equal("a4-cell-image-179-5: LiberationSerif (the case loads Lato-Bold, Lato-Regular)");
    }

    /// <summary>The faces of a print that are not a font file the case loads, one entry each.</summary>
    private static IEnumerable<string> Wrong(string name, PrintedPages print)
    {
        var loaded = Loaded(name);
        return print.Pages.SelectMany(page => page.Words).Select(word => word.Face!).Distinct(StringComparer.Ordinal)
            .Where(face => !loaded.Contains(face) && !(Fallback.TryGetValue(name, out var fallback) && fallback == face))
            .Select(face => $"{name}: {face} (the case loads {string.Join(", ", loaded.Order(StringComparer.Ordinal))})");
    }

    /// <summary>The PostScript names of the font files a case loads: every font <c>url(…)</c> in the case and in the style
    /// sheets it links, resolved from the file that names it; a file that is not there fails the test.</summary>
    private static HashSet<string> Loaded(string name)
    {
        var html = Path.Combine(HtmlCases.Folder(), name + ".html");
        var text = File.ReadAllText(html);
        var sheets = Link().Matches(text).Select(match => Path.Combine(HtmlCases.Folder(), match.Groups[1].Value)).Prepend(html);
        return [.. sheets.SelectMany(sheet => FontUrl().Matches(File.ReadAllText(sheet))
            .Select(match => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sheet)!, match.Groups[1].Value))))
            .Distinct()
            .Select(file => FontParser.Parse(File.ReadAllBytes(file)).Value.Names.PostScriptName)];
    }

    [GeneratedRegex("""<link rel="stylesheet" href="([^"]+)">""")]
    private static partial Regex Link();

    [GeneratedRegex("""url\(([^)'"]+\.(?:ttf|otf))\)""")]
    private static partial Regex FontUrl();
}
