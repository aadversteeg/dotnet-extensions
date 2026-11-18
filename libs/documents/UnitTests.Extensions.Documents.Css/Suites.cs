using System.IO;
using System.Runtime.CompilerServices;

namespace UnitTests.Extensions.Documents.Css;

/// <summary>Where the vendored public test suites are: <c>assets/test-suites/</c> of the family (see the README there).</summary>
internal static class Suites
{
    /// <summary>The folder of one suite.</summary>
    public static string Folder(string[] parts, [CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine([Path.GetDirectoryName(thisFile)!, "..", "assets", "test-suites", .. parts]));

    /// <summary>css-parsing-tests (SimonSapin), CC0.</summary>
    public static string CssParsingTests => Folder(["css-parsing-tests"]);

    /// <summary>The CSS cases extracted from web-platform-tests (tools/extract-wpt-css-cases.py), BSD-3.</summary>
    public static string WptCssCases => Path.Combine(Folder(["wpt", "css"]), "cases.json");

    /// <summary>Chromium's verdicts on those cases (tools/reference/chromium/computed-style/measured).</summary>
    public static string ChromiumWptVerdicts([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "tools", "reference", "chromium", "computed-style", "measured", "wpt-css-verdicts.json"));

    /// <summary>The colour Chromium 159 computes for each named colour (computed-style/measure.ps1 -Step colors).</summary>
    public static string ChromiumNamedColors([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "tools", "reference", "chromium", "computed-style", "measured", "named-colors.txt"));
}
