using System.Collections.Immutable;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Documents.Typesetting;
using Xunit.Abstractions;

namespace UnitTests.Extensions.Documents.Typesetting.Reference;

/// <summary>
/// The gate of rule B3: our shaper against HarfBuzz on a corpus of English, Dutch, German, French, Greek and
/// Russian text plus ligature, kerning and mark stress words, in Lato Regular and Bold and in Theano Didot (a GDEF
/// table, a mark with a non-zero advance, stacked marks). Every line must give the same glyphs, the same text per
/// glyph, the same glyph positions and the same total advance.
/// </summary>
/// <remarks>
/// <para>What is compared: glyph ids; the characters each glyph shows (derived from HarfBuzz's clusters, with
/// cluster level "monotone characters" so a mark keeps its own cluster as in our shaper); each glyph's ABSOLUTE
/// position — pen position plus offset, in font units (we shape at a size of one em per unit) — and its vertical
/// offset; and the total advance. Absolute positions rather than raw advance/offset pairs, because they are what
/// a page shows, and two shapers may split one kerning value differently between advance and offset (HarfBuzz
/// splits a 'kern'-table value across the pair) without any visible difference.</para>
/// <para>Same features on both sides: HarfBuzz applies more features by default than the shaper implements. Those
/// are switched off in HarfBuzz — the list below — so both shapers apply exactly ccmp, liga, clig, kern and mark.
/// This is configuration of the reference, not special-casing of our shaper.</para>
/// </remarks>
public class HarfBuzzCorpusTests(ITestOutputHelper output)
{
    /// <summary>HarfBuzz's default features (hb-ot-shape.cc: common, horizontal and direction features) that
    /// the shaper does not implement.</summary>
    public static readonly ImmutableArray<string> FeaturesNotImplemented =
        ["-rvrn", "-ltra", "-ltrm", "-frac", "-numr", "-dnom", "-rand", "-abvm", "-blwm", "-locl", "-mkmk", "-rlig", "-calt", "-curs", "-dist", "-rclt"];

    private static readonly string Circumflex = char.ConvertFromUtf32(0x0302);
    private static readonly string Tilde = char.ConvertFromUtf32(0x0303);
    private static readonly string Diaeresis = char.ConvertFromUtf32(0x0308);
    private static readonly string Cedilla = char.ConvertFromUtf32(0x0327);
    private static readonly string Acute = char.ConvertFromUtf32(0x0301);
    private static readonly string ReversedCommaAbove = char.ConvertFromUtf32(0x0314);
    private static readonly string DotBelow = char.ConvertFromUtf32(0x0323);

    /// <summary>The corpus: (language, ISO 15924 script, text).</summary>
    public static readonly ImmutableArray<(string Language, string Script, string Text)> Corpus =
    [
        ("English", "Latn", "The office staff fixed the affine flag before the official AVATAR premiere."),
        ("English", "Latn", "Toyota, Volvo, Wave, Yacht, LT, P.A., To, Ty, Va, We, Yo, Tr, ff, fi, fl, ffi, ffl, fj."),
        ("English", "Latn", "AVATAR office fi ffi ffl efficient waffle shuffle firefly Typography"),
        ("Dutch", "Latn", "Het efficiënte bijzondere ijsje werd officieel geleverd aan de Vlaamse affiche-ontwerper."),
        ("Dutch", "Latn", "Wat voor fijne afgeronde VWO-examens; Yvonne en Tjitske fietsen naar Venlo."),
        ("German", "Latn", "Die Schifffahrt auf dem Fluss: Auflage, Straße, Größe, Äpfel, Wolf und Typografie."),
        ("German", "Latn", "Öffentliche Aufführung; täglich offen. VW, Tafel, Kaffee, Löffel."),
        ("French", "Latn", "L'efficacité de l'œuvre : « Voilà », dit-il. Ça va ? Affiche, souffle, difficile."),
        ("French", "Latn", "Où êtes-vous ? À côté du fiacre ; Ève, Yvette et Théo flânent."),
        ("Greek", "Grek", "Ελληνική τυπογραφία: ΑΥΤΟΚΙΝΗΤΟ, Άλφα και Ωμέγα, ΤΑΥΤΟΤΗΤΑ."),
        ("Greek", "Grek", "Γράμματα όπως ΑΒΓΔ και λέξεις όπως φίλος, ΥΓΕΙΑ, Τύπος."),
        ("Russian", "Cyrl", "Съешь же ещё этих мягких французских булок, да выпей чаю."),
        ("Russian", "Cyrl", "ТАКСИ, ГЛАВА, Учитель, ЧУВСТВО, Тыква, Ёжик."),
        ("Marks", "Latn", $"x{Circumflex} q{Tilde} x{Circumflex}x{Tilde} q{Circumflex}"),
        // Normalization before shaping (Unicode.Normalization): a base and a mark the font has precomposed; marks in
        // non-canonical order; a precomposed character with one more mark; singleton decompositions (ANGSTROM SIGN,
        // OHM SIGN); precomposed characters a font may lack but can show decomposed. OHM SIGN is Greek, so it comes last:
        // the shaper shapes each script run on its own (as a browser itemizes text), the reference the whole line.
        ("Normalization", "Latn", $"n{Cedilla} y{Diaeresis} A{Circumflex} o{Tilde} a{Circumflex}{DotBelow} a{DotBelow}{Circumflex} e{Acute}{Acute} \u00E9{DotBelow} \u212B \u01F5 \u1E43 \u1EAD \u2126"),
    ];

    /// <summary>More lines for Theano Didot only: U+0314 COMBINING REVERSED COMMA ABOVE has an advance of 420 units in
    /// it (HarfBuzz zeroes the advance of a mark), alone, after another mark and between letters — on letters without
    /// a precomposed form, so Unicode composition plays no part; and its kerning
    /// pairs. Not Greek with marks: there Theano's <c>ccmp</c> swaps the marks for Greek forms with contextual
    /// substitutions (GSUB type 5, lookups 1–4), a lookup type the shaper skips (documented on the Shaper).</summary>
    public static readonly ImmutableArray<(string Language, string Script, string Text)> TheanoCorpus =
    [
        ("Marks", "Latn", $"e{ReversedCommaAbove} a{ReversedCommaAbove}b x{Acute}{ReversedCommaAbove} v{ReversedCommaAbove}{Acute}w"),
        ("Kerning", "Latn", "AVATAR To Yo AT Ty VA LT Wo"),
    ];

    private sealed record Line(string Font, string Language, string Text, IReadOnlyList<string> Differences, int Glyphs);

    [Fact]
    public void Our_shaper_matches_harfbuzz_on_the_corpus()
    {
        var reference = HarfBuzzReference.Instance;
        var fonts = new[]
        {
            ("Lato-Regular", TestFonts.Regular, TestFonts.Bytes("Lato-Regular.ttf"), Corpus),
            ("Lato-Bold", TestFonts.Bold, TestFonts.Bytes("Lato-Bold.ttf"), Corpus),
            ("TheanoDidot-Regular", TestFonts.TheanoDidot, TestFonts.Bytes("TheanoDidot-Regular.ttf"), [.. Corpus, .. TheanoCorpus]),
        };
        var lines = fonts
            .SelectMany(font => font.Item4.Select(entry => Compare(reference, font.Item1, font.Item2, font.Item3, entry.Language, entry.Script, entry.Text)))
            .ToList();

        var failed = lines.Where(line => line.Differences.Count > 0).ToList();
        output.WriteLine($"HarfBuzz {reference.Version}: {lines.Count} lines ({Corpus.Length} corpus lines × {fonts.Length} fonts, {TheanoCorpus.Length} more in Theano Didot), "
            + $"{lines.Sum(line => line.Glyphs)} glyphs; {lines.Count - failed.Count} lines identical, {failed.Count} differ, 0 skipped.");
        foreach (var line in failed)
        {
            output.WriteLine($"{line.Font} {line.Language}: {line.Text}");
            foreach (var difference in line.Differences.Take(10))
            {
                output.WriteLine("   " + difference);
            }
        }

        failed.Should().BeEmpty();
        lines.Should().HaveCount(47, "every corpus line in every font was shaped");
        lines.Sum(line => line.Glyphs).Should().Be(2688, "every line was shaped whole");
    }

    private static Line Compare(HarfBuzzReference reference, string fontName, TrueTypeFont font, byte[] fontFile, string language, string script, string text)
    {
        var expected = reference.Shape(fontFile, text, script, FeaturesNotImplemented);
        var actual = Shaper.Shape(text, font, fontSize: font.Metrics.UnitsPerEm).Value;
        var differences = new List<string>();

        var expectedIds = expected.Select(glyph => glyph.Glyph).ToList();
        var actualIds = actual.Select(glyph => (int)glyph.Glyph.Value).ToList();
        if (!expectedIds.SequenceEqual(actualIds))
        {
            differences.Add($"glyphs: harfbuzz [{string.Join(" ", expectedIds)}] ours [{string.Join(" ", actualIds)}]");
            return new Line(fontName, language, text, differences, expected.Length);
        }

        var expectedTexts = TextsFromClusters(expected, text);
        var expectedPositions = AbsolutePositions(expected.Select(glyph => ((double)glyph.XAdvance, (double)glyph.XOffset)));
        var actualPositions = AbsolutePositions(actual.Select(glyph => (glyph.Advance, glyph.XOffset)));
        for (var index = 0; index < expected.Length; index++)
        {
            if (expectedTexts[index] != actual[index].Text)
            {
                differences.Add($"glyph {index} (g{expectedIds[index]}): text harfbuzz '{expectedTexts[index]}' ours '{actual[index].Text}'");
            }

            if (Math.Abs(expectedPositions[index] - actualPositions[index]) > 1e-6 || Math.Abs(expected[index].YOffset - actual[index].YOffset) > 1e-6)
            {
                differences.Add($"glyph {index} (g{expectedIds[index]} '{expectedTexts[index]}'): position harfbuzz ({expectedPositions[index]}, {expected[index].YOffset}) ours ({actualPositions[index]}, {actual[index].YOffset})");
            }
        }

        var expectedWidth = expected.Sum(glyph => glyph.XAdvance);
        var actualWidth = actual.Sum(glyph => glyph.Advance);
        if (Math.Abs(expectedWidth - actualWidth) > 1e-6)
        {
            differences.Add($"total advance: harfbuzz {expectedWidth} ours {actualWidth}");
        }

        return new Line(fontName, language, text, differences, expected.Length);
    }

    /// <summary>Each glyph's position: the sum of the advances before it plus its own offset.</summary>
    private static List<double> AbsolutePositions(IEnumerable<(double Advance, double Offset)> glyphs)
    {
        var positions = new List<double>();
        var pen = 0.0;
        foreach (var (advance, offset) in glyphs)
        {
            positions.Add(pen + offset);
            pen += advance;
        }

        return positions;
    }

    /// <summary>
    /// The characters each glyph shows, from HarfBuzz's clusters: a glyph shows the characters from its cluster up
    /// to the next larger cluster; when several glyphs share a cluster, the first shows them and the others none
    /// (the convention of our shaper).
    /// </summary>
    private static List<string> TextsFromClusters(ImmutableArray<ReferenceGlyph> glyphs, string text)
    {
        var characters = text.EnumerateRunes().Select(rune => rune.ToString()).ToArray();
        var texts = new List<string>();
        for (var index = 0; index < glyphs.Length; index++)
        {
            var cluster = glyphs[index].Cluster;
            var sharesCluster = index > 0 && glyphs[index - 1].Cluster == cluster;
            var next = glyphs.Skip(index + 1).Select(glyph => glyph.Cluster).Where(value => value > cluster).DefaultIfEmpty(characters.Length).Min();
            texts.Add(sharesCluster ? string.Empty : string.Concat(characters[cluster..next]));
        }

        return texts;
    }
}
