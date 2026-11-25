using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using static UnitTests.Extensions.Documents.Layout.Models;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// A box among the runs of an inline box is an atomic inline: in this version only a box laid out by the image algorithm,
/// without page break properties, one nesting level below its paragraph; every walker of the model takes it.
/// </summary>
public class AtomicInlineModelTests
{
    private static Box ImageBox(int id, BoxStyle? style = null) =>
        new(new NodeId(id), SemanticRole.Figure("figure"), LayoutAlgorithmId.Image, style ?? BoxStyle.Initial,
            [new ImageNode(new NodeId(id + 1), TestImages.Load("baseline-rgb.jpg"), 12.Pt(), null, At(0))], At(0));

    private static Box Line(int id, params Node[] atomics) =>
        new(new NodeId(id), SemanticRole.Paragraph, LayoutAlgorithmId.Inline, BoxStyle.Initial,
            [new TextRun(new NodeId(id + 1), "ab ", Text(), At(0)), .. atomics, new TextRun(new NodeId(id + 2), " cd", Text(), At(0))], At(0));

    private static Error Refused(StyledDocument document)
    {
        var result = DocumentLayout.Run(document, TestFonts.Set);
        result.IsFailure.Should().BeTrue("the model holds a box inline layout cannot set in a line");
        return result.Error;
    }

    [Fact]
    public void An_image_box_among_the_runs_is_laid_out_in_the_line()
    {
        var laid = DocumentLayout.Run(Document(Line(1, ImageBox(10))), TestFonts.Set);

        laid.IsSuccess.Should().BeTrue(laid.IsFailure ? laid.Error.ToString() : string.Empty);
        laid.Value.Pages[0].Items.OfType<Ave.Extensions.Documents.PageModel.DrawImage>().Should().ContainSingle();
    }

    [Fact]
    public void A_box_of_another_algorithm_among_the_runs_is_refused_with_a_message_that_says_so()
    {
        var error = Refused(Document(Line(1, new Box(new NodeId(10), SemanticRole.Table, LayoutAlgorithmId.Tracks, BoxStyle.Initial, [], At(0)))));

        error.Code.Should().Be(DocumentErrorCodes.Input.Unsupported.AtomicInline);
        error.Message.Should().Contain("image algorithm").And.Contain("inline-block");
        error.Metadata![ErrorMetadataKeys.Node].Should().Be("#10");
        error.Metadata![ErrorMetadataKeys.Value].Should().Be(LayoutAlgorithmId.Tracks.ToString());
    }

    [Theory]
    [InlineData("before")]
    [InlineData("after")]
    [InlineData("inside")]
    public void An_image_in_a_line_with_page_break_properties_is_refused(string property)
    {
        var style = property switch
        {
            "before" => BoxStyle.Initial with { BreakBefore = BreakBetween.Page },
            "after" => BoxStyle.Initial with { BreakAfter = BreakBetween.Avoid },
            _ => BoxStyle.Initial with { BreakInside = BreakInside.Avoid },
        };

        var error = Refused(Document(Line(1, ImageBox(10, style))));

        error.Code.Should().Be(DocumentErrorCodes.Input.Unsupported.AtomicInline);
        error.Metadata![ErrorMetadataKeys.Node].Should().Be("#10");
    }

    [Fact]
    public void An_image_in_a_line_is_one_nesting_level_below_its_paragraph()
    {
        static Box Nested(int levels, Box paragraph)
        {
            var box = paragraph;
            for (var level = 1; level < levels; level++)
            {
                box = Flow(level, [box]);
            }

            return box;
        }

        // A paragraph at the deepest level is laid out (ModelCheckTests); with an image in its line, one level less.
        DocumentLayout.Run(Document(Nested(ModelLimits.MaxNestingDepth - 1, Line(1_000_000, ImageBox(2_000_000)))), TestFonts.Set).IsSuccess.Should().BeTrue();
        Refused(Document(Nested(ModelLimits.MaxNestingDepth, Line(1_000_000, ImageBox(2_000_000))))).Code.Should().Be(DocumentErrorCodes.Input.Invalid.TooDeep);
    }

    [Fact]
    public void The_model_text_writes_the_image_box_inside_its_paragraph()
    {
        var lines = DocumentModelText.Write(Document(Line(1, ImageBox(10)))).Split('\n');

        var paragraph = Array.FindIndex(lines, line => line.StartsWith("  box #1 inline", StringComparison.Ordinal));
        lines[paragraph + 2].Should().StartWith("    box #10 image");
        lines[paragraph + 3].Should().StartWith("      image #11 ");
    }

    [Fact]
    public void A_paragraph_of_only_an_image_has_a_line_box_and_is_not_empty_for_margin_collapsing()
    {
        var paragraph = new Box(new NodeId(1), SemanticRole.Paragraph, LayoutAlgorithmId.Inline, BoxStyle.Initial, [ImageBox(10)], At(0));

        InlineLayout.HasLineBoxes(paragraph).Should().BeTrue();
        MarginCollapsing.IsEmpty(paragraph).Should().BeFalse();
    }
}
