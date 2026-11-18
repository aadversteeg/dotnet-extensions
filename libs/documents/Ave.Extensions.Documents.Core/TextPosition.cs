namespace Ave.Extensions.Documents.Core;

/// <summary>
/// A place in a text a front-end read: the 1-based line and column of a character. The markup readers (HTML, CSS)
/// put one on every node, token and diagnostic, so a problem can be shown at the place the author wrote it.
/// </summary>
/// <remarks>
/// <para>A line ends at a line feed, a carriage return, or a carriage return followed by a line feed (the HTML
/// Standard §13.2.3.5 and CSS Syntax 3 §3.3 both normalise these to one line feed before reading). The column counts
/// UTF-16 code units of the DECODED text, from 1 at the start of the line: that is what a .NET string indexes and what
/// editors and browser developer tools count, so a column can be used to index the line without converting. A
/// character outside the Basic Multilingual Plane therefore takes two columns. Bytes are never counted: the same text
/// read from UTF-8 or from UTF-16 bytes gives the same positions.</para>
/// <para>The text form of an error carries the column in <see cref="ErrorMetadataKeys.SourceColumn"/>, next to
/// <see cref="ErrorMetadataKeys.SourceFile"/> (the name of the text) and <see cref="ErrorMetadataKeys.SourceLine"/>.</para>
/// </remarks>
/// <param name="Line">The 1-based line.</param>
/// <param name="Column">The 1-based column, in UTF-16 code units.</param>
public readonly record struct TextPosition(int Line, int Column)
{
    /// <summary>The first character of a text: line 1, column 1.</summary>
    public static TextPosition Start => new(1, 1);

    /// <summary>Formats as <c>line:column</c>, for example <c>12:5</c>.</summary>
    /// <returns>The text form.</returns>
    public override string ToString() => $"{Line}:{Column}";
}
