using System;
using System.IO;
using Ave.Extensions.Documents.Css;
using Ave.Extensions.Documents.Css.Rules;

namespace Ave.Extensions.Documents.Html;

/// <summary>The user-agent style sheet (UserAgent/html.css, an embedded resource), read once by the engine's CSS
/// reader. Its diagnostics are the engine's own, not the document's: a test keeps them empty.</summary>
internal static class UserAgentStyleSheet
{
    /// <summary>The name the sheet's rules carry in diagnostics.</summary>
    public const string SourceName = "html.css (user agent)";

    private static readonly Lazy<CssResult<StyleSheet>> Parsed = new(() =>
    {
        using var stream = typeof(UserAgentStyleSheet).Assembly.GetManifestResourceStream("Ave.Extensions.Documents.Html.html.css")!;
        using var reader = new StreamReader(stream);
        return CssParser.ParseStyleSheet(reader.ReadToEnd(), new CssSourceOptions(SourceName));
    });

    /// <summary>The sheet.</summary>
    public static StyleSheet Sheet => Parsed.Value.Value;

    /// <summary>What reading it reported (nothing, when the sheet is right).</summary>
    public static CssResult<StyleSheet> Result => Parsed.Value;
}
