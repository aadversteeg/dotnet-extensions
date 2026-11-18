namespace Ave.Extensions.Documents.Html.Parsing.Decoding;

/// <summary>
/// "Get an encoding" (Encoding Standard §4.2): a label, with ASCII white space around it removed and compared
/// ASCII case-insensitively, names an encoding or nothing. The table is generated (EncodingLabels.g.cs).
/// </summary>
internal static partial class EncodingLabels
{
    /// <summary>The name of the encoding UTF-8.</summary>
    public const string Utf8 = "UTF-8";

    /// <summary>The encoding a label names, or null when it names none (the standard's "failure").</summary>
    /// <param name="label">The label as written.</param>
    /// <returns>The encoding's name, or null.</returns>
    public static string? Resolve(string label)
    {
        var trimmed = Ascii.ToLower(label.Trim(['\t', '\n', '\f', '\r', ' ']));
        return Names.TryGetValue(trimmed, out var name) ? name : null;
    }

    /// <summary>
    /// The encoding a meta element's label makes the parser use (HTML Standard §13.2.3.2 step "Abort the prescan",
    /// and §13.2.3.4 "changing the encoding while parsing" step 2): UTF-16BE and UTF-16LE become UTF-8 (a document that
    /// is UTF-16 has a byte order mark, which wins before any meta is read), x-user-defined becomes windows-1252.
    /// </summary>
    /// <param name="name">The encoding the label named.</param>
    /// <returns>The encoding the parser would switch to.</returns>
    public static string ForMeta(string name) => name switch
    {
        "UTF-16BE" or "UTF-16LE" => Utf8,
        "x-user-defined" => "windows-1252",
        _ => name,
    };
}
