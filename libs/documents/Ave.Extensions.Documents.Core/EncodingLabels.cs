namespace Ave.Extensions.Documents.Core;

/// <summary>
/// "Get an encoding" (Encoding Standard §4.2): a label, with ASCII white space around it removed and compared ASCII
/// case-insensitively, names an encoding or nothing. The one table of the HTML and CSS readers, generated
/// (EncodingLabels.g.cs, WHATWG data under the BSD 3-Clause License, NOTICE.md).
/// </summary>
public static partial class EncodingLabels
{
    /// <summary>The name of the encoding UTF-8.</summary>
    public const string Utf8 = "UTF-8";

    /// <summary>The encoding a label names, or null when it names none (the standard's "failure").</summary>
    /// <param name="label">The label as written, or null.</param>
    /// <returns>The encoding's name, or null.</returns>
    public static string? Resolve(string? label) =>
        label is not null && Names.TryGetValue(InfraAscii.ToLower(label.Trim(['\t', '\n', '\f', '\r', ' '])), out var name) ? name : null;
}
