using System.Collections.Immutable;
using System.Text;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Documents.Typesetting;
using Xunit.Abstractions;

namespace UnitTests.Extensions.Documents.Typesetting.Reference;

/// <summary>
/// Shaping at a break (<c>GreedyLineBreaker</c>, THE RULE FOR SHAPING AT A BREAK): every line of a broken paragraph has
/// the glyphs HarfBuzz gives that line alone. Paragraphs with ligatures across soft hyphens and zero width spaces, kerning
/// pairs across break opportunities and a hyphen after a kerned letter, broken at every width from one em to their whole
/// width in steps of 37 units; each line's text — its glyphs' texts, with the shown hyphen as the hyphen character it is
/// drawn with — shaped by HarfBuzz (the reference build, the same features as <see cref="HarfBuzzCorpusTests"/>) and
/// compared glyph for glyph: the visible glyphs, their positions and the line's total advance, in font units (one em per
/// unit, as the corpus test shapes).
/// </summary>
public class LineReshapingReferenceTests(ITestOutputHelper output)
{
    private const string SoftHyphen = "­";

    /// <summary>The paragraphs, in Lato Regular.</summary>
    public static readonly ImmutableArray<string> Paragraphs =
    [
        "of­fice ef­fi­cient af­fine waf­fle shuf­fle"
            + " fi­re­fly",
        "AVA​TAR To​To Ty-Va LT­VA Wa­Ve Yo​yo",
        "The of­fice staff fixed the af­fine flag; Wave, Yacht, To­yo­ta, AVA​TAR, ef­fi­cient.",
    ];

    private sealed record Checked(int Lines, int ShapedAgain, IReadOnlyList<string> Differences);

    [Fact]
    public void Every_line_has_the_glyphs_harfbuzz_gives_the_line_alone()
    {
        var font = TestFonts.Regular;
        var file = TestFonts.Bytes("Lato-Regular.ttf");
        var size = font.Metrics.UnitsPerEm;
        var results = Paragraphs.Select(paragraph => Check(font, file, size, paragraph)).ToList();

        output.WriteLine($"HarfBuzz {HarfBuzzReference.Instance.Version}: {results.Sum(result => result.Lines)} lines, "
            + $"{results.Sum(result => result.ShapedAgain)} of them with an end shaped again, {results.Sum(result => result.Differences.Count)} differences.");
        foreach (var difference in results.SelectMany(result => result.Differences).Take(20))
        {
            output.WriteLine(difference);
        }

        results.SelectMany(result => result.Differences).Should().BeEmpty();
        results.Sum(result => result.Lines).Should().Be(14_364, "every paragraph was broken at every width of the sweep");
        results.Sum(result => result.ShapedAgain).Should().Be(8_637, "the sweep breaks inside ligatures, at kerning pairs and at shown hyphens");
    }

    [Fact]
    public void Without_shaping_again_the_same_lines_differ_from_harfbuzz()
    {
        // The positive control: the paragraph's own glyphs for each line, as the line breaker gave them before shaping at a
        // break existed, are not what HarfBuzz gives a line that ends inside a ligature or at a kerning pair.
        var font = TestFonts.Regular;
        var file = TestFonts.Bytes("Lato-Regular.ttf");
        var size = font.Metrics.UnitsPerEm;
        var glyphs = Shaper.Shape(Paragraphs[0], font, size).Value;

        var lines = GreedyLineBreaker.BreakLines([[.. glyphs]], 2500);
        var differ = lines.Count(line => Differences(file, font, line.Segments.SelectMany(segment => segment.Glyphs).ToList(), "control").Count > 0);

        differ.Should().BePositive();
    }

    private Checked Check(TrueTypeFont font, byte[] file, double size, string paragraph)
    {
        var glyphs = Shaper.Shape(paragraph, font, size).Value;
        var whole = glyphs.Sum(glyph => glyph.Advance);
        var (lines, shapedAgain) = (0, 0);
        var differences = new List<string>();
        for (var width = size; width <= whole + 37; width += 37)
        {
            var broken = GreedyLineBreaker.BreakLines([[.. glyphs]], width, (_, text, showsHyphen, budget) => Shaper.ShapeLinePiece(text, font, size, showsHyphen, budget));
            broken.IsSuccess.Should().BeTrue(broken.IsFailure ? broken.Error.ToString() : string.Empty);
            foreach (var line in broken.Value)
            {
                lines++;
                var ours = line.Segments.SelectMany(segment => segment.Glyphs).ToList();
                shapedAgain += ours.Any(glyph => !glyphs.Contains(glyph)) ? 1 : 0;
                differences.AddRange(Differences(file, font, ours, $"width {width}"));
            }
        }

        return new Checked(lines, shapedAgain, differences);
    }

    /// <summary>How one line of ours differs from HarfBuzz's shaping of the line's text.</summary>
    private static List<string> Differences(byte[] file, TrueTypeFont font, IReadOnlyList<ShapedGlyph> ours, string where)
    {
        var text = LineText(font, ours);
        var expected = HarfBuzzReference.Instance.Shape(file, text, "Latn", HarfBuzzCorpusTests.FeaturesNotImplemented);
        var space = font.GlyphFor(new Rune(' ')).Value.Value;

        // HarfBuzz hides a default ignorable by drawing the space glyph without advance (hb-ot-shape.cc,
        // hb_ot_hide_default_ignorables); the shaper marks it invisible. Compared: everything else.
        var theirs = Positioned(expected.Select(glyph => (glyph.Glyph, (double)glyph.XAdvance, (double)glyph.XOffset, glyph.Glyph == space && glyph.XAdvance == 0)));
        var mine = Positioned(ours.Select(glyph => ((int)glyph.Glyph.Value, glyph.Advance, glyph.XOffset, glyph.IsInvisible)));
        var differences = new List<string>();
        if (!theirs.Select(glyph => glyph.Glyph).SequenceEqual(mine.Select(glyph => glyph.Glyph)))
        {
            differences.Add($"{where} '{text}': glyphs harfbuzz [{string.Join(" ", theirs.Select(glyph => glyph.Glyph))}] ours [{string.Join(" ", mine.Select(glyph => glyph.Glyph))}]");
        }
        else if (theirs.Zip(mine).Any(pair => Math.Abs(pair.First.Position - pair.Second.Position) > 1e-6))
        {
            differences.Add($"{where} '{text}': positions harfbuzz [{string.Join(" ", theirs.Select(glyph => glyph.Position))}] ours [{string.Join(" ", mine.Select(glyph => glyph.Position))}]");
        }

        var (theirWidth, myWidth) = (expected.Sum(glyph => (double)glyph.XAdvance), ours.Sum(glyph => glyph.Advance));
        if (Math.Abs(theirWidth - myWidth) > 1e-6)
        {
            differences.Add($"{where} '{text}': width harfbuzz {theirWidth} ours {myWidth}");
        }

        return differences;
    }

    /// <summary>The text a line shows: its glyphs' texts, with a shown hyphen (a visible glyph whose text is the soft hyphen
    /// it stands for) as the character it is drawn with, U+2010 HYPHEN in Lato.</summary>
    private static string LineText(TrueTypeFont font, IReadOnlyList<ShapedGlyph> glyphs)
    {
        var hyphen = font.GlyphFor(new Rune(0x2010)).HasValue ? "‐" : "-";
        return string.Concat(glyphs.Select((glyph, index) => index == glyphs.Count - 1 && glyph.Text == SoftHyphen && !glyph.IsInvisible ? hyphen : glyph.Text));
    }

    /// <summary>The visible glyphs with their positions: the advances of every glyph before plus their own offset.</summary>
    private static List<(int Glyph, double Position)> Positioned(IEnumerable<(int Glyph, double Advance, double Offset, bool Hidden)> glyphs)
    {
        var positioned = new List<(int Glyph, double Position)>();
        var pen = 0.0;
        foreach (var (glyph, advance, offset, hidden) in glyphs)
        {
            if (!hidden)
            {
                positioned.Add((glyph, pen + offset));
            }

            pen += advance;
        }

        return positioned;
    }
}
