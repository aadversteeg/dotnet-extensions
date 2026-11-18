using System.IO;
using System.Runtime.CompilerServices;
using Ave.Extensions.Documents.Images;
using Ave.Extensions.Functional;

namespace UnitTests.Extensions.Documents.Samples;

/// <summary>
/// Golden-file comparison for images, kept in this project because it needs the raster side of the Images package (the
/// PNG encoder and the sample decoder): in <c>TestSupport</c> it would make the release-mode tests of every package
/// that uses <c>TestSupport</c> need that new API from nuget.org (docs/RELEASING.md). Same rules as
/// <c>Golden.Compare</c>: a <c>Golden</c> folder next to the test source, <c>UPDATE_GOLDEN=1</c> to (re)write.
/// </summary>
internal static class GoldenImages
{
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
            File.WriteAllBytes(path, [.. ImageEncoder.EncodePng(width, height, rgba).Value]);
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
