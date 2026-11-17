using Ave.Extensions.Documents.Core;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents.Fonts;

/// <summary>Errors about fonts.</summary>
public static class FontErrorCodes
{
    /// <summary><c>Documents.Font</c>.</summary>
    public static readonly ErrorCode _ = DocumentErrorCodes._ / "Font";

    /// <summary>A font file that cannot be read. Metadata: SourceFile, SourceLine (the loading call).</summary>
    public static class Parse
    {
        /// <summary><c>Documents.Font.Parse</c>.</summary>
        public static readonly ErrorCode _ = FontErrorCodes._ / "Parse";

        /// <summary>The data ends before a structure it announces. Metadata: Table, Offset.</summary>
        public static readonly ErrorCode Truncated = _ / "Truncated";

        /// <summary>Not a TrueType font file (for example a CFF-based OpenType font). Metadata: Value.</summary>
        public static readonly ErrorCode UnsupportedFormat = _ / "UnsupportedFormat";

        /// <summary>A table that this reader needs is absent. Metadata: Table.</summary>
        public static readonly ErrorCode MissingTable = _ / "MissingTable";

        /// <summary>A table contains a value that the OpenType specification does not allow.
        /// Metadata: Table, Value.</summary>
        public static readonly ErrorCode InvalidValue = _ / "InvalidValue";
    }

    /// <summary>Ranges given for a coverage or a class definition that are not in glyph order, overlap, or end before
    /// they start. Metadata: Value (the index of the first such range).</summary>
    public static readonly ErrorCode InvalidRanges = _ / "InvalidRanges";

    /// <summary>
    /// The text contains a character the font has no glyph for. Never silently dropped.
    /// Metadata: FontName, Character, CodePoint, SourceFile, SourceLine.
    /// </summary>
    public static readonly ErrorCode GlyphMissing = _ / "GlyphMissing";

    /// <summary>No font in the font set matches the family and weight asked for.
    /// Metadata: FontName, Value (the weight), SourceFile, SourceLine.</summary>
    public static readonly ErrorCode NotFound = _ / "NotFound";
}
