using System.Collections.Immutable;
using System.Text;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Documents.Typesetting;
using Ave.Extensions.Functional;
using Xunit.Abstractions;

namespace UnitTests.Extensions.Documents.Typesetting.Reference;

/// <summary>
/// Shaping at a break (<c>GreedyLineBreaker</c>, THE RULE FOR SHAPING AT A BREAK). Where a line ends or starts at a place
/// that is not safe to break, the breaker shapes that piece of the text again on its own; HarfBuzz is the reference for
/// the glyphs of a piece shaped as one piece, so every such piece must have the glyphs HarfBuzz gives that piece's text
/// alone, and a shown hyphen the glyph HarfBuzz gives the hyphen alone. (Where the line breaks, and what a line is made of
/// — the paragraph's glyphs in the middle, a ligature's half shaped without the rest — is Chromium's, compared in
/// <see cref="ChromiumLineEndTests"/>.) Paragraphs with ligatures across soft hyphens and zero width spaces, kerning
/// pairs across break opportunities and a hyphen after a kerned letter, in Lato Regular, broken at every width from one em
/// to their whole width in steps of 37 units, in both treatments of a line end at a space. Every piece is checked to be a
/// part of the input, and every line to show its part of the input once. Font units (one em per unit, as the corpus test
/// shapes).
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

    private sealed record Checked(int Lines, int Pieces, int Hyphens, IReadOnlyList<string> Differences);

    [Fact]
    public void Every_piece_shaped_again_has_the_glyphs_harfbuzz_gives_that_piece_alone()
    {
        var font = TestFonts.Regular;
        var file = TestFonts.Bytes("Lato-Regular.ttf");
        var size = font.Metrics.UnitsPerEm;
        var results = Paragraphs.SelectMany(paragraph => new[] { LineEndShaping.KeepParagraphGlyphs, LineEndShaping.ShapeAgainBeforeSpace }
            .Select(atSpace => Check(font, file, size, paragraph, atSpace))).ToList();
        var differences = results.SelectMany(result => result.Differences).ToList();

        output.WriteLine($"HarfBuzz {HarfBuzzReference.Instance.Version}: {results.Sum(result => result.Lines)} lines, {results.Sum(result => result.Pieces)} pieces "
            + $"shaped again, {results.Sum(result => result.Hyphens)} of them with a shown hyphen, {differences.Count} differences.");
        foreach (var difference in differences.Take(20))
        {
            output.WriteLine(difference);
        }

        differences.Should().BeEmpty();
        results.Sum(result => result.Lines).Should().Be(28_595, "every paragraph was broken at every width of the sweep, in both treatments of a line end at a space");
        results.Sum(result => result.Pieces).Should().Be(19_350, "the sweep breaks inside ligatures and at kerning pairs");
        results.Sum(result => result.Hyphens).Should().Be(4_629, "the sweep shows hyphens at the end of pieces shaped again");
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
        var differ = lines.Count(line =>
        {
            var all = line.Segments.SelectMany(segment => segment.Glyphs).ToList();
            var showsHyphen = all[^1].Text == SoftHyphen && !all[^1].IsInvisible;
            return Differences(file, font, all, Text(line), showsHyphen, "control").Count > 0;
        });

        differ.Should().BePositive();
    }

    private static Checked Check(TrueTypeFont font, byte[] file, double size, string paragraph, LineEndShaping atSpace)
    {
        var glyphs = Shaper.Shape(paragraph, font, size).Value;
        var whole = glyphs.Sum(glyph => glyph.Advance);
        var (lines, pieces, hyphens) = (0, 0, 0);
        var differences = new List<string>();
        for (var width = size; width <= whole + 37; width += 37)
        {
            var shapedAgain = new List<(string Text, bool ShowsHyphen, ValueList<ShapedGlyph> Glyphs)>();
            var broken = GreedyLineBreaker.BreakLines([[.. glyphs]], width, (_, text, showsHyphen, budget) =>
                Shaper.ShapeLinePiece(text, font, size, showsHyphen, budget).OnSuccessMap(piece =>
                {
                    shapedAgain.Add((text, showsHyphen, piece));
                    return piece;
                }), atSpace);
            broken.IsSuccess.Should().BeTrue(broken.IsFailure ? broken.Error.ToString() : string.Empty);

            // Each line shows its part of the input once: the lines' texts, in order, are the input without the spaces the
            // line ends removed.
            var cursor = 0;
            foreach (var line in broken.Value)
            {
                lines++;
                var text = Text(line);
                while (cursor < paragraph.Length && paragraph[cursor] == ' ')
                {
                    cursor++;
                }

                string.CompareOrdinal(paragraph, cursor, text, 0, text.Length).Should().Be(0, $"width {width}: line '{text}' is the input from offset {cursor}");
                cursor += text.Length;
            }

            paragraph[cursor..].Trim().Should().BeEmpty($"width {width}: the lines show the whole input");

            // Every piece shaped again is a part of the input, and has HarfBuzz's glyphs for it.
            foreach (var (text, showsHyphen, piece) in shapedAgain)
            {
                pieces++;
                hyphens += showsHyphen ? 1 : 0;
                paragraph.Contains(text, StringComparison.Ordinal).Should().BeTrue($"width {width}: the piece '{text}' is a part of the input");
                differences.AddRange(Differences(file, font, piece, text, showsHyphen, $"width {width} ({atSpace})"));
            }
        }

        return new Checked(lines, pieces, hyphens, differences);
    }

    /// <summary>A line's text: its glyphs' texts (a shown hyphen's is the soft hyphen it stands for).</summary>
    private static string Text(TextLine line) => string.Concat(line.Segments.SelectMany(segment => segment.Glyphs).Select(glyph => glyph.Text));

    /// <summary>How the glyphs of ours differ from HarfBuzz's shaping of <paramref name="text"/>; with
    /// <paramref name="showsHyphen"/>, the soft hyphen the text ends with is HarfBuzz's hyphen shaped alone.</summary>
    private static List<string> Differences(byte[] file, TrueTypeFont font, IReadOnlyList<ShapedGlyph> ours, string text, bool showsHyphen, string where)
    {
        var space = font.GlyphFor(new Rune(' ')).Value.Value;
        var shaped = HarfBuzzReference.Instance.Shape(file, text, "Latn", HarfBuzzCorpusTests.FeaturesNotImplemented)
            .Select(glyph => (Glyph: glyph.Glyph, Advance: (double)glyph.XAdvance, Offset: (double)glyph.XOffset, Hidden: glyph.Glyph == space && glyph.XAdvance == 0))
            .ToList();
        if (showsHyphen && text.EndsWith(SoftHyphen, StringComparison.Ordinal))
        {
            // The soft hyphen is hidden (HarfBuzz draws a default ignorable as the space glyph without advance); in its
            // place the hyphen, shaped alone.
            var hyphenCharacter = font.GlyphFor(new Rune(0x2010)).HasValue ? "‐" : "-";
            var hyphen = HarfBuzzReference.Instance.Shape(file, hyphenCharacter, "Latn", HarfBuzzCorpusTests.FeaturesNotImplemented).Single();
            shaped[^1] = (hyphen.Glyph, hyphen.XAdvance, hyphen.XOffset, false);
        }

        // HarfBuzz hides a default ignorable by drawing the space glyph without advance (hb-ot-shape.cc,
        // hb_ot_hide_default_ignorables); the shaper marks it invisible. Compared: everything else.
        var theirs = Positioned(shaped);
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

        var (theirWidth, myWidth) = (shaped.Sum(glyph => glyph.Advance), ours.Sum(glyph => glyph.Advance));
        if (Math.Abs(theirWidth - myWidth) > 1e-6)
        {
            differences.Add($"{where} '{text}': width harfbuzz {theirWidth} ours {myWidth}");
        }

        return differences;
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
