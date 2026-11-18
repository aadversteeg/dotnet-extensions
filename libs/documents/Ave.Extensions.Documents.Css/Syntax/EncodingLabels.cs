namespace Ave.Extensions.Documents.Css.Syntax;

/// <summary>
/// "Get an encoding" (Encoding Standard §4.2): a label, with ASCII white space around it removed and compared ASCII
/// case-insensitively, names an encoding or nothing. The table is generated (EncodingLabels.g.cs).
/// </summary>
internal static partial class EncodingLabels
{
    /// <summary>The encoding a label names, or null when it names none (the standard's "failure").</summary>
    /// <param name="label">The label as written, or null.</param>
    /// <returns>The encoding's name, or null.</returns>
    public static string? Resolve(string? label) =>
        label is not null && Names.TryGetValue(Ascii.ToLower(label.Trim(['\t', '\n', '\f', '\r', ' '])), out var name) ? name : null;
}
