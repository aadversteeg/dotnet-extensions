using System.Globalization;
using System.IO;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.TestSupport;
using static UnitTests.Extensions.Documents.Fonts.GlyphBytes;

namespace UnitTests.Extensions.Documents.Fonts;

/// <summary>
/// The outline decoder against fontTools 4.57.0 (in the reference tools image, <c>tools/reference/glyph-outlines.py</c>),
/// point for point and exactly (the same double, not within a tolerance): every glyph of the four test fonts, and a font
/// of hand-made composites with the placements the test fonts do not use (a 2 × 2 matrix, scaled offsets, matched
/// points, nesting). Needs Docker (trait <c>Category=Reference</c>).
/// </summary>
[Trait("Category", "Reference")]
public sealed class GlyphShapeReferenceTests
{
    private static readonly string[] FontFileNames = ["Lato-Regular.ttf", "Lato-Bold.ttf", "Lato-Italic.ttf", "TheanoDidot-Regular.ttf"];

    private const string Composites = "composites.ttf";

    private static readonly (int X, int Y, bool On)[] Curve = [(0, 0, true), (300, -40, false), (500, 200, false), (520, 700, true), (-10, 650, false)];

    private static byte[] CompositesFont() => FontFiles.WithGlyphs(
        TestFonts.Bytes("Lato-Regular.ttf"),
        [
            [],
            Simple(Curve, [(40, 40, false), (90, 40, false), (90, 90, false)]),
            Composite(
                new Component(1, 13, -21, ArgsAreXyValues | WeHaveATwoByTwo, F2Dot14(0.7071), F2Dot14(0.3), F2Dot14(-0.6), F2Dot14(1.25)),
                new Component(1, -7, 11, ArgsAreXyValues | WeHaveAnXAndYScale | ScaledComponentOffset, F2Dot14(0.333), F2Dot14(-1.5))),
            Composite(
                new Component(1, 0, 0, ArgsAreXyValues),
                new Component(1, 3, 1, WeHaveAScale, F2Dot14(0.61)),
                new Component(2, 7, 2, WeHaveATwoByTwo, F2Dot14(-0.2), F2Dot14(0.9), F2Dot14(1.1), F2Dot14(0.05))),
            Composite(
                new Component(3, 5, 9, ArgsAreXyValues | WeHaveAScale | UnscaledComponentOffset, F2Dot14(1.9)),
                new Component(2, 4, 0, 0)),
        ]);

    [DockerFact]
    public void Every_glyph_decodes_to_the_points_fontTools_reads()
    {
        var folder = Directory.CreateTempSubdirectory("ave-extensions-documents-outlines-").FullName;
        try
        {
            foreach (var name in FontFileNames)
            {
                File.WriteAllBytes(Path.Combine(folder, name), TestFonts.Bytes(name));
            }

            File.WriteAllBytes(Path.Combine(folder, Composites), CompositesFont());
            File.Copy(Path.Combine(AppContext.BaseDirectory, "tools", "glyph-outlines.py"), Path.Combine(folder, "glyph-outlines.py"));
            var (exitCode, output) = Docker.Run(
                ["run", "--rm", .. Docker.UserArguments, "-v", $"{folder}:/work", ReferenceTools.Image, "python3", "-I", "/work/glyph-outlines.py", "/work/outlines.txt",
                    .. FontFileNames.Append(Composites).Select(name => "/work/" + name)],
                TimeSpan.FromMinutes(5));
            exitCode.Should().Be(0, output);

            var fonts = FontFileNames.ToDictionary(name => name, name => FontParser.Parse(File.ReadAllBytes(Path.Combine(folder, name))).Value);
            fonts[Composites] = FontParser.Parse(CompositesFont()).Value;
            var lines = File.ReadAllLines(Path.Combine(folder, "outlines.txt"));
            var differences = lines.Select(line => Difference(line, fonts)).OfType<string>().ToList();

            lines.Should().HaveCount(fonts.Values.Sum(font => font.GlyphCount), "fontTools lists every glyph");
            differences.Should().BeEmpty($"{differences.Count} glyphs differ; the first: {string.Join("\n", differences.Take(10))}");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>Compares one line of the fontTools listing with the decoder's shape of that glyph; null when equal.</summary>
    private static string? Difference(string line, Dictionary<string, TrueTypeFont> fonts)
    {
        var fields = line.Split(' ');
        var glyph = new GlyphId(ushort.Parse(fields[1], CultureInfo.InvariantCulture));
        var expectedEnds = fields[2] == "-" ? [] : fields[2].Split(',').Select(end => int.Parse(end, CultureInfo.InvariantCulture)).ToArray();
        var expectedPoints = fields[3] == "-" ? [] : fields[3].Split(';').Select(Point).ToArray();

        var result = fonts[fields[0]].Shape(glyph);
        if (result.IsFailure)
        {
            return $"{fields[0]} {glyph}: {result.Error.Message}";
        }

        var contours = result.Value.Contours;
        var ends = contours.Select(contour => contour.Points.Count).Aggregate(new List<int>(), (list, count) => [.. list, (list.Count == 0 ? -1 : list[^1]) + count]);
        var points = contours.SelectMany(contour => contour.Points).ToArray();
        return ends.SequenceEqual(expectedEnds) && points.SequenceEqual(expectedPoints)
            ? null
            : $"{fields[0]} {glyph}: ends {string.Join(',', ends)} vs {fields[2]}; points {string.Join(';', points.Select(point => $"{point.X:R},{point.Y:R},{(point.OnCurve ? 1 : 0)}"))} vs {fields[3]}";
    }

    private static OutlinePoint Point(string text)
    {
        var parts = text.Split(',');
        return new OutlinePoint(double.Parse(parts[0], CultureInfo.InvariantCulture), double.Parse(parts[1], CultureInfo.InvariantCulture), parts[2] == "1");
    }
}
