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
}
