using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.Html.Parsing.Decoding;

/// <summary>What the HTML parser makes of the encoding a meta element names (the labels themselves are Core's
/// <see cref="Core.EncodingLabels"/>).</summary>
internal static class MetaEncodings
{
    /// <summary>
    /// The encoding a meta element's label makes the parser use (HTML Standard §13.2.3.2 step "Abort the prescan",
    /// and §13.2.3.4 "changing the encoding while parsing" step 2): UTF-16BE and UTF-16LE become UTF-8 (a document that
    /// is UTF-16 has a byte order mark, which wins before any meta is read), x-user-defined becomes windows-1252.
    /// </summary>
    /// <param name="name">The encoding the label named.</param>
    /// <returns>The encoding the parser would switch to.</returns>
    public static string ForMeta(string name) => name switch
    {
        "UTF-16BE" or "UTF-16LE" => EncodingLabels.Utf8,
        "x-user-defined" => "windows-1252",
        _ => name,
    };
}
