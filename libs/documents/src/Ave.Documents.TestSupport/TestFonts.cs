using System;
using System.IO;
using Ave.Documents.Fonts;

namespace Ave.Documents.TestSupport;

/// <summary>
/// The test fonts: Lato 2.015 Regular and Bold (SIL Open Font License 1.1, see assets/fonts/lato/OFL.txt),
/// loaded once from the test output folder.
/// </summary>
public static class TestFonts
{
    private static readonly Lazy<TrueTypeFont> LazyRegular = new(() => Load("Lato-Regular.ttf"));
    private static readonly Lazy<TrueTypeFont> LazyBold = new(() => Load("Lato-Bold.ttf"));

    /// <summary>Lato Regular (weight 400).</summary>
    public static TrueTypeFont Regular => LazyRegular.Value;

    /// <summary>Lato Bold (weight 700).</summary>
    public static TrueTypeFont Bold => LazyBold.Value;

    /// <summary>A font set with both.</summary>
    public static FontSet Set => new([Regular, Bold]);

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
