using System.Text;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Documents.Typesetting;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// The pieces of one paragraph that the line breaker shapes again share ONE budget, the paragraph's
/// (<see cref="LineShapingBudget"/>), through the wiring the layout uses: a font whose lookups cost much per glyph is
/// refused once the pieces together spend what the paragraph may, although each run, and each piece alone, stays under
/// a budget of its own.
/// </summary>
public class LineShapingBudgetTests
{
    private static readonly byte[] Lato = TestFonts.Bytes("Lato-Regular.ttf");

    /// <summary>Lato with a GSUB of <paramref name="lookups"/> lookups "a → a" (single substitution, delta 0): offsets in the
    /// lookup list to one lookup table, so that every glyph costs about two operations per lookup. Lato's GPOS stays, so
    /// A and V kern, also across a soft hyphen.</summary>
    private static TrueTypeFont CostlyLato(int lookups)
    {
        var a = TestFonts.Regular.GlyphFor(new Rune('a'))!.Value.Value;
        const int ScriptList = 10, Script = 18, FeatureList = 30, Feature = 38;
        var lookupList = Feature + 4 + 2 * lookups;
        var table = 2 + 2 * lookups;
        var gsub = new BigEndianBuilder()
            .UInt16(1).UInt16(0).UInt16(ScriptList).UInt16(FeatureList).UInt16(lookupList)
            .UInt16(1).Bytes("DFLT"u8.ToArray()).UInt16(Script - ScriptList)
            .UInt16(4).UInt16(0)
            .UInt16(0).UInt16(0xFFFF).UInt16(1).UInt16(0)
            .UInt16(1).Bytes("liga"u8.ToArray()).UInt16(Feature - FeatureList)
            .UInt16(0).UInt16(lookups);
        for (var index = 0; index < lookups; index++)
        {
            gsub.UInt16(index);
        }

        gsub.UInt16(lookups);
        for (var index = 0; index < lookups; index++)
        {
            gsub.UInt16(table);
        }

        gsub.UInt16(1).UInt16(0).UInt16(1).UInt16(8)
            .UInt16(1).UInt16(6).UInt16(0)
            .UInt16(1).UInt16(1).UInt16(a);
        var font = FontParser.Parse(FontFiles.WithTable(Lato, "GSUB", gsub.ToArray()));
        font.IsSuccess.Should().BeTrue(font.IsFailure ? font.Error.ToString() : string.Empty);
        return font.Value;
    }

    /// <summary>A paragraph of <paramref name="runs"/> runs "AV­AV ", 80 pt, in a 180 pt wide page area: each run is
    /// shaped alone, and each breaks at its soft hyphen, where A and V kern, so both sides of every break are shaped again.</summary>
    private static StyledDocument Paragraph(int runs)
    {
        var style = new TextStyle("Lato", 80.Pt(), FontWeight.Regular, Color.Black);
        var paragraph = new Box(new NodeId(1), SemanticRole.Paragraph, LayoutAlgorithmId.Inline, BoxStyle.Initial,
            [.. Enumerable.Range(0, runs).Select(index => (Node)new TextRun(new NodeId(100 + index), "AV­AV ", style, Models.At(index)))], Models.At(0));
        return new StyledDocument(new DocumentMetadata("Test", null), new PageStyle(200.Pt(), 14000.Pt(), Edges.All(10.Pt())), Models.Flow(0, [paragraph]));
    }

    [Fact]
    public void The_pieces_shaped_again_share_the_paragraph_s_budget_and_a_font_that_spends_it_is_refused()
    {
        // 4 000 lookups, about 8 000 operations a glyph: a run of six characters costs about 48 000, under the
        // max(65 536, 1 024 per glyph) it may spend alone; a piece of three characters about 24 000. Twenty runs shape some
        // 100 characters again, about 800 000 operations, which the paragraph's one budget of max(65 536, 1 024 x 120) does
        // not hold.
        var font = CostlyLato(4000);
        var environment = LayoutEnvironment.Create(new FontSet([font]));

        var run = Shaper.Shape("AV­AV ", font, 80);
        run.IsSuccess.Should().BeTrue(run.IsFailure ? run.Error.ToString() : "");
        var piece = Shaper.ShapeLinePiece("AV­", font, 80, showsHyphen: true);
        piece.IsSuccess.Should().BeTrue("each piece alone stays under a budget of its own");

        var laidOut = Paginator.Paginate(environment, Paragraph(20));

        laidOut.IsFailure.Should().BeTrue("the pieces of the paragraph together spend more than its budget");
        laidOut.Error.Code.Should().Be(TypesettingErrorCodes.TooMuchWork);
        laidOut.Error.Message.Should().Contain("operations");
    }

    [Fact]
    public void The_same_paragraph_in_a_font_that_costs_little_is_laid_out()
    {
        // The control: the same wiring, the same breaks, with the plain font.
        var laidOut = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), Paragraph(20));

        laidOut.IsSuccess.Should().BeTrue(laidOut.IsFailure ? laidOut.Error.ToString() : string.Empty);
    }
}
