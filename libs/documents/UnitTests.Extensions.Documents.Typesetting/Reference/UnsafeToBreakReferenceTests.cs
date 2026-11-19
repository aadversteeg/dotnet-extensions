using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Documents.Typesetting;
using Xunit.Abstractions;

namespace UnitTests.Extensions.Documents.Typesetting.Reference;

/// <summary>
/// The places the shaper marks unsafe to break are HarfBuzz's (hb-buffer.h, <c>HB_GLYPH_FLAG_UNSAFE_TO_BREAK</c>): the line
/// breaker shapes a line end again exactly where HarfBuzz says a break changes the shaping, and nowhere else. A pair whose
/// values are all zero marks nothing; a lookup marks the clusters after its first glyph's own. Compared cluster by cluster
/// on the corpus lines that are one script run each, in Lato Regular, Lato Bold and Theano Didot, and on the paragraphs of
/// the line-reshaping test, and on ligatures that skipped a soft hyphen: a cluster is unsafe when HarfBuzz flags its
/// glyphs, and the engine's when it marks one of them.
/// </summary>
public class UnsafeToBreakReferenceTests(ITestOutputHelper output)
{
    [Fact]
    public void The_shaper_marks_unsafe_to_break_exactly_the_clusters_harfbuzz_flags()
    {
        // The corpus line of normalization ends in a Greek character: the shaper shapes it as a run of its own, HarfBuzz
        // with the line, so the flags at that run boundary differ by design. Every other line is one script run.
        var lines = HarfBuzzCorpusTests.Corpus.Where(line => line.Language != "Normalization").Select(line => (line.Script, line.Text))
            .Concat(LineReshapingReferenceTests.Paragraphs.Select(paragraph => ("Latn", paragraph)))

            // A ligature that skipped a soft hyphen (its cluster holds the hyphen's glyph too), then a letter it kerns
            // with: the pair marks the letter, not the hyphen in the ligature's own cluster.
            .Append(("Latn", "xf\u00ADfa xf\u00ADfe of\u00ADfo f\u00ADf."))
            .ToList();
        var fonts = new (TrueTypeFont Font, byte[] File)[]
        {
            (TestFonts.Regular, TestFonts.Bytes("Lato-Regular.ttf")),
            (TestFonts.Bold, TestFonts.Bytes("Lato-Bold.ttf")),
            (TestFonts.TheanoDidot, TestFonts.Bytes("TheanoDidot-Regular.ttf")),
        };
        var (clusters, unsafeClusters) = (0, 0);
        var differences = new List<string>();
        foreach (var (font, file) in fonts)
        {
            foreach (var (script, text) in lines)
            {
                var ours = Shaper.Shape(text, font, font.Metrics.UnitsPerEm).Value;
                var theirs = HarfBuzzReference.Instance.Shape(file, text, script, HarfBuzzCorpusTests.FeaturesNotImplemented);
                ours.Length.Should().Be(theirs.Length, $"the corpus test compares the glyphs of '{text}' in {font.Names.PostScriptName}");
                for (var index = 0; index < ours.Length; index++)
                {
                    if (ours[index].Text.Length == 0)
                    {
                        continue;
                    }

                    // The engine's cluster: this glyph and the glyphs after it without text of their own.
                    var end = index + 1;
                    while (end < ours.Length && ours[end].Text.Length == 0)
                    {
                        end++;
                    }

                    var mine = ours.Skip(index).Take(end - index).Any(glyph => glyph.UnsafeToBreak);
                    clusters++;
                    unsafeClusters += mine ? 1 : 0;
                    if (mine != theirs[index].UnsafeToBreak)
                    {
                        differences.Add($"{font.Names.PostScriptName} '{text}' glyph {index} ('{ours[index].Text}'): harfbuzz {theirs[index].UnsafeToBreak}, ours {mine}");
                    }
                }
            }
        }

        output.WriteLine($"{clusters} clusters, {unsafeClusters} unsafe to break, {differences.Count} differences.");
        foreach (var difference in differences.Take(20))
        {
            output.WriteLine(difference);
        }

        differences.Should().BeEmpty();
        clusters.Should().Be(3_046, "every cluster of the lines in the three fonts was compared");
        unsafeClusters.Should().Be(750, "kerning pairs and marks make clusters unsafe to break");
    }
}
