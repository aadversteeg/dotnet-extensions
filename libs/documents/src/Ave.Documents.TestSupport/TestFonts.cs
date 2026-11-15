using System;
using System.IO;
using Ave.Documents.Fonts;

namespace Ave.Documents.TestSupport;

/// <summary>
/// The test fonts, loaded once from the test output folder: Lato 2.015 Regular, Bold and Italic (SIL Open Font License
/// 1.1, see assets/fonts/lato/OFL.txt), and Theano Didot 2.0 (SIL Open Font License 1.1, see
/// assets/fonts/theano/OFL.txt), a font with what Lato lacks: a GDEF table, a <c>mkmk</c> feature, an old-style
/// 'kern' table, a combining mark with a non-zero advance (U+0314, 420 units) and a non-zero line gap (560 of 2048).
/// </summary>
public static class TestFonts
{
    private static readonly Lazy<TrueTypeFont> LazyRegular = new(() => Load("Lato-Regular.ttf"));
    private static readonly Lazy<TrueTypeFont> LazyBold = new(() => Load("Lato-Bold.ttf"));
    private static readonly Lazy<TrueTypeFont> LazyItalic = new(() => Load("Lato-Italic.ttf"));
    private static readonly Lazy<TrueTypeFont> LazyTheanoDidot = new(() => Load("TheanoDidot-Regular.ttf"));

    /// <summary>Lato Regular (weight 400).</summary>
    public static TrueTypeFont Regular => LazyRegular.Value;

    /// <summary>Lato Bold (weight 700).</summary>
    public static TrueTypeFont Bold => LazyBold.Value;

    /// <summary>Lato Italic (weight 400, italic).</summary>
    public static TrueTypeFont Italic => LazyItalic.Value;

    /// <summary>Theano Didot Regular 2.0 (2048 units per em).</summary>
    public static TrueTypeFont TheanoDidot => LazyTheanoDidot.Value;

    /// <summary>A font set with Regular and Bold (the stage-0 set).</summary>
    public static FontSet Set => new([Regular, Bold]);

    /// <summary>A font set with Regular, Bold and Italic.</summary>
    public static FontSet SetWithItalic => new([Regular, Bold, Italic]);

    /// <summary>The bytes of a test font file.</summary>
    /// <param name="fileName">For example <c>Lato-Regular.ttf</c>.</param>
    /// <returns>The file contents.</returns>
    public static byte[] Bytes(string fileName) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fonts", fileName));

    private static TrueTypeFont Load(string fileName)
    {
        var result = FontParser.Parse(Bytes(fileName));
        return result.IsSuccess
            ? result.Value
            : throw new InvalidOperationException($"Test font {fileName} did not parse: {result.Error}");
    }
}
