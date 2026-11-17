using System;
using System.IO;
using Ave.Extensions.Documents.Images;

namespace Ave.Extensions.Documents.TestSupport;

/// <summary>The image test files (Pillow-made, see tools/generate-test-images.py), copied next to every test assembly.</summary>
public static class TestImages
{
    /// <summary>The bytes of an image test file.</summary>
    /// <param name="fileName">For example <c>rgba8.png</c>.</param>
    /// <returns>The file contents.</returns>
    public static byte[] Bytes(string fileName) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "images", fileName));

    /// <summary>A decoded image test file.</summary>
    /// <param name="fileName">For example <c>rgba8.png</c> (7 × 5 pixels) or <c>baseline-rgb.jpg</c> (40 × 30).</param>
    /// <returns>The image.</returns>
    public static ImageData Load(string fileName)
    {
        var result = ImageDecoder.Decode(Bytes(fileName));
        return result.IsSuccess ? result.Value : throw new InvalidOperationException($"Test image {fileName} did not decode: {result.Error}");
    }
}
