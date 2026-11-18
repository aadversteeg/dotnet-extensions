namespace Ave.Extensions.Documents.Core;

/// <summary>
/// The metadata keys the engine puts on its errors (<c>Error.With(key, value)</c>). One list, so producers and
/// consumers (tests, front-end diagnostics) cannot drift apart through a typo.
/// </summary>
public static class ErrorMetadataKeys
{
    /// <summary>The source file of the node or call that caused the error (string, full path).</summary>
    public const string SourceFile = "SourceFile";

    /// <summary>The source line of the node or call that caused the error (int).</summary>
    public const string SourceLine = "SourceLine";

    /// <summary>The 1-based column in a text a front-end read (int, in UTF-16 code units; see
    /// <see cref="TextPosition"/>). Set together with <see cref="SourceFile"/> (the text's name) and
    /// <see cref="SourceLine"/>.</summary>
    public const string SourceColumn = "SourceColumn";

    /// <summary>The node id (string, for example <c>#7</c>).</summary>
    public const string Node = "Node";

    /// <summary>The PostScript or family name of the font involved (string).</summary>
    public const string FontName = "FontName";

    /// <summary>The character involved, as text (string).</summary>
    public const string Character = "Character";

    /// <summary>The Unicode code point involved, formatted <c>U+20AC</c> (string).</summary>
    public const string CodePoint = "CodePoint";

    /// <summary>The font table involved, for example <c>cmap</c> (string).</summary>
    public const string Table = "Table";

    /// <summary>A byte offset in the input (int).</summary>
    public const string Offset = "Offset";

    /// <summary>The offending value, as text (string).</summary>
    public const string Value = "Value";

    /// <summary>The property or parameter the value was given for (string).</summary>
    public const string Property = "Property";

    /// <summary>Of the diagnostics a <see cref="DocumentErrorCodes.TooManyDiagnostics"/> leaves out, how many refuse the
    /// document (int).</summary>
    public const string Refusals = "Refusals";
}
