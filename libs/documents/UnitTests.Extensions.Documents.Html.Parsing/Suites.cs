using System.IO;
using System.Runtime.CompilerServices;

namespace UnitTests.Extensions.Documents.Html.Parsing;

/// <summary>
/// Where the vendored public test suites are: <c>assets/test-suites/</c> of the family, each in its own folder with
/// its upstream licence (see the README there and NOTICE.md).
/// </summary>
internal static class Suites
{
    /// <summary>The folder of one suite.</summary>
    /// <param name="parts">The path below <c>assets/test-suites</c>.</param>
    /// <param name="thisFile">Filled in by the compiler.</param>
    /// <returns>The full path.</returns>
    public static string Folder(string[] parts, [CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine([Path.GetDirectoryName(thisFile)!, "..", "assets", "test-suites", .. parts]));

    /// <summary>The html5lib tokenizer tests.</summary>
    public static string Html5libTokenizer => Folder(["html5lib-tests", "tokenizer"]);

    /// <summary>The tree-construction tests (web-platform-tests, <c>html/syntax/parsing/resources</c>).</summary>
    public static string TreeConstruction => Folder(["wpt", "html", "syntax", "parsing", "resources"]);

    /// <summary>The own tree cases (<c>tools/reference/chromium/html-tree/cases.tsv</c>) and Chromium 159's tree of each
    /// (<c>measured/cases.json</c>).</summary>
    public static (string Cases, string Trees) ChromiumCases([CallerFilePath] string thisFile = "")
    {
        var folder = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "tools", "reference", "chromium", "html-tree"));
        return (Path.Combine(folder, "cases.tsv"), Path.Combine(folder, "measured", "cases.json"));
    }

    /// <summary>Chromium 159's trees of the tree-construction tests where they differ from the suite
    /// (<c>tools/reference/chromium/html-tree/measured/tree-construction.json</c>).</summary>
    public static string ChromiumTrees([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "tools", "reference", "chromium", "html-tree", "measured", "tree-construction.json"));
}
