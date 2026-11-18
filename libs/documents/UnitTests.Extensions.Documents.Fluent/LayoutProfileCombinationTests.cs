using System.IO;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Functional;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// The three policies of a <see cref="LayoutProfile"/> are independent: each is read in one place, and none changes the
/// rules of another. Over all eight combinations, on documents with content taller than the page area (an image in a
/// table row that also breaks, under a repeated header row; a tall line between lines): no exception, every word drawn
/// outside artifacts exactly once, and whether a document is refused depends on the overflow policy alone.
/// </summary>
public class LayoutProfileCombinationTests
{
    private static byte[] Tall => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "chromium", "tall.png"));

    /// <summary>Every combination of the three policies.</summary>
    public static TheoryData<MonolithicOverflow, EmptyRowParts, FontMetricRounding> Combinations()
    {
        var data = new TheoryData<MonolithicOverflow, EmptyRowParts, FontMetricRounding>();
        foreach (var overflow in Enum.GetValues<MonolithicOverflow>())
        {
            foreach (var parts in Enum.GetValues<EmptyRowParts>())
            {
                foreach (var rounding in Enum.GetValues<FontMetricRounding>())
                {
                    data.Add(overflow, parts, rounding);
                }
            }
        }

        return data;
    }

    private static Element[][] Bodies =>
    [
        [
            Paragraph("intro"),
            Table(Columns(Fill(1), Fill(1)),
            [
                HeaderRow([Cell("H"), Cell("h")]),
                TableRow([Cell("r1"), Cell([Block([]).Padding(4.Pt()), Block([]).Padding(4.Pt())])]),
                TableRow([Cell([Image(Tall).Alt("tall").Height(408.Pt())]), Cell([Paragraph([.. Enumerable.Range(0, 30).SelectMany(i => i == 0 ? new[] { Text("p0") } : [LineBreak(), Text($"p{i}")])])])]),
                TableRow([Cell("after"), Cell("a")]),
            ]).GridLines(0.75.Pt(), Color.Black).SplitLinesAtBreaks(),
            Paragraph("end"),
        ],
        [Paragraph([Text("one "), Text("W").FontSize(200.Pt()), LineBreak(), Text("two")]), Paragraph("end")],
    ];

    [Fact]
    public void The_rule_for_a_slice_under_a_repeated_header_row_does_not_depend_on_the_other_two_policies()
    {
        // Under Slice the image's first slice comes on the same page, below the header, whatever the row-part and font
        // metric policies are (TablePage: content taller than the page area under a repeated header row).
        var firstSlices = new List<int>();
        foreach (var parts in Enum.GetValues<EmptyRowParts>())
        {
            foreach (var rounding in Enum.GetValues<FontMetricRounding>())
            {
                var document = Document([.. Bodies[0]], Page(300.Pt(), 204.Pt()).Margin(12.Pt()), title: "Title", language: "en")
                    .FontFamily("Lato").MonolithicOverflow(MonolithicOverflow.Slice).EmptyRowParts(parts).FontMetricRounding(rounding);
                var pages = DocumentTranslator.ToModel(document).OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic)).Value.Pages;
                firstSlices.Add(pages.Select((page, index) => (page, index)).First(pair => pair.page.Items.OfType<ClipStart>().Any()).index + 1);
            }
        }

        firstSlices.Should().HaveCount(4).And.AllBeEquivalentTo(firstSlices[0]);
    }

    /// <summary>The text of every glyph run outside artifacts, word by word.</summary>
    private static List<string> ContentWords(PageDocument document)
    {
        var words = new List<string>();
        foreach (var page in document.Pages)
        {
            var inArtifact = false;
            foreach (var item in page.Items)
            {
                inArtifact = item switch { ArtifactStart => true, ArtifactEnd => false, _ => inArtifact };
                if (item is GlyphRun run && !inArtifact)
                {
                    words.AddRange(string.Concat(run.Glyphs.Select(glyph => glyph.Text)).Split([' ', '\u2028'], StringSplitOptions.RemoveEmptyEntries));
                }
            }
        }

        return words;
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void Every_combination_lays_out_every_word_once_and_only_the_overflow_policy_decides_a_refusal(
        MonolithicOverflow overflow, EmptyRowParts parts, FontMetricRounding rounding)
    {
        string[][] expected =
        [
            ["intro", "H", "h", "r1", .. Enumerable.Range(0, 30).Select(i => $"p{i}"), "after", "a", "end"],
            ["one", "W", "two", "end"],
        ];
        foreach (var (body, words) in Bodies.Zip(expected))
        {
            var document = Document([.. body], Page(300.Pt(), 204.Pt()).Margin(12.Pt()), title: "Title", language: "en")
                .FontFamily("Lato").MonolithicOverflow(overflow).EmptyRowParts(parts).FontMetricRounding(rounding);
            var result = DocumentTranslator.ToModel(document).OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic));

            if (overflow == MonolithicOverflow.Refuse)
            {
                result.IsFailure.Should().BeTrue();
                result.Error.Code.Should().Be(LayoutErrorCodes.DoesNotFit);
                continue;
            }

            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
            ContentWords(result.Value).Order(StringComparer.Ordinal).Should().Equal(words.Order(StringComparer.Ordinal));
        }
    }
}
