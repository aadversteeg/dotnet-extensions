using System;
using System.IO;
using System.Runtime.CompilerServices;
using Ave.Extensions.Documents.Images;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.TestSupport;

/// <summary>
/// Golden-file comparison for the text serialisations. A golden file lives in a <c>Golden</c> folder next to
/// the test source file. To accept new output after a deliberate change, run the tests with the environment
/// variable <c>UPDATE_GOLDEN=1</c>, review the diff with git, and commit it.
/// </summary>
public static class Golden
{
    /// <summary>
    /// Compares <paramref name="actual"/> with the golden file. Returns null when equal; otherwise a message that
    /// says where they first differ. With UPDATE_GOLDEN=1 the file is (re)written and null is returned.
    /// </summary>
    /// <param name="name">The golden file name, for example <c>letter.model.txt</c>.</param>
    /// <param name="actual">The text produced by the test.</param>
    /// <param name="testFile">Filled in by the compiler: locates the Golden folder.</param>
    /// <returns>Null when the text matches, or a description of the first difference.</returns>
    public static string? Compare(string name, string actual, [CallerFilePath] string testFile = "")
    {
        var path = Path.Combine(Path.GetDirectoryName(testFile)!, "Golden", name);
        if (Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
            return null;
        }

        if (!File.Exists(path))
        {
            return $"Golden file {path} does not exist; run with UPDATE_GOLDEN=1 to create it.";
        }

        var expected = File.ReadAllText(path).ReplaceLineEndings("\n");
        if (expected == actual)
        {
            return null;
        }

        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');
        for (var index = 0; index < Math.Max(expectedLines.Length, actualLines.Length); index++)
        {
            var left = index < expectedLines.Length ? expectedLines[index] : "<end of file>";
            var right = index < actualLines.Length ? actualLines[index] : "<end of file>";
            if (left != right)
            {
                return $"{name} differs at line {index + 1}:\n  expected: {left}\n  actual:   {right}";
            }
        }

        return $"{name} differs (line endings only).";
    }

    /// <summary>
    /// Compares pixels with a golden PNG file in the <c>Golden</c> folder next to the test source, by its decoded pixels,
    /// not its bytes: the PNG bytes also depend on the runtime's zlib, the pixels do not. Equal pixel for pixel, or a
    /// message with the first differing pixel. With UPDATE_GOLDEN=1 the file is (re)written as a PNG.
    /// </summary>
    /// <param name="name">The golden file name, for example <c>letter.page1.96dpi.png</c>.</param>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="rgba">The pixels, 8-bit RGBA, row by row.</param>
    /// <param name="testFile">Filled in by the compiler: locates the Golden folder.</param>
    /// <returns>Null when the pixels match, or a description of the first difference.</returns>
    public static string? ComparePixels(string name, int width, int height, ReadOnlySpan<byte> rgba, [CallerFilePath] string testFile = "")
    {
        var path = Path.Combine(Path.GetDirectoryName(testFile)!, "Golden", name);
        if (Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, [.. ImageEncoder.EncodePng(width, height, rgba)]);
            return null;
        }

        if (!File.Exists(path))
        {
            return $"Golden file {path} does not exist; run with UPDATE_GOLDEN=1 to create it.";
        }

        var decoded = ImageDecoder.Decode(File.ReadAllBytes(path)).OnSuccessBind(ImageDecoder.DecodeSamples);
        if (decoded.IsFailure)
        {
            return $"{name} does not decode: {decoded.Error}";
        }

        var golden = decoded.Value;
        if ((golden.PixelWidth, golden.PixelHeight) != (width, height))
        {
            return $"{name} is {golden.PixelWidth} x {golden.PixelHeight} pixels, the output {width} x {height}.";
        }

        var channels = golden.ColorSpace == ImageColorSpace.Gray ? 1 : 3;
        for (var pixel = 0; pixel < width * height; pixel++)
        {
            for (var channel = 0; channel < 4; channel++)
            {
                var expected = channel == 3
                    ? golden.Alpha is { } alpha ? alpha[pixel] : (byte)255
                    : golden.Samples[(pixel * channels) + (channels == 1 ? 0 : channel)];
                if (rgba[(pixel * 4) + channel] != expected)
                {
                    return $"{name} differs at pixel ({pixel % width}, {pixel / width}), channel {channel}: expected {expected}, actual {rgba[(pixel * 4) + channel]}.";
                }
            }
        }

        return null;
    }
}
