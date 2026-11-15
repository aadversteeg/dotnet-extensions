using Ave.Documents.Core;
using Ave.Documents.Layout;
using Ave.Documents.DocumentModel;
using Ave.Documents.TestSupport;
using Ave.Documents.Fonts;
using Ave.Extensions.ErrorPaths;
using static UnitTests.Ave.Documents.Layout.Models;

namespace UnitTests.Ave.Documents.Layout;

public class LayoutErrorTests
{
    private static Error Fail(StyledDocument document)
    {
        var result = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), document);
        result.IsFailure.Should().BeTrue();
        return result.Error;
    }

    [Fact]
    public void A_block_taller_than_a_page_that_cannot_break_does_not_fit_and_names_its_source_line()
    {
        var line = CurrentLine() + 1;
        var tall = Paragraph(1, "x", BoxStyle.Initial with { Padding = new Edges(90.Pt(), 0.Pt(), 0.Pt(), 0.Pt()) });

        var error = Fail(Document(tall));

        error.Code.Should().Be(LayoutErrorCodes.DoesNotFit);
        error.Metadata![ErrorMetadataKeys.Node].Should().Be("#1");
        error.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(line);
        error.Metadata![ErrorMetadataKeys.SourceFile].Should().Be(At(0).File, "the location is the test's own line, not the helper's");
    }

    [Fact]
    public void A_line_taller_than_a_page_does_not_fit_even_after_other_content()
    {
        // At 100 pt Lato a line is 120 pt high; the page area is 80 pt. The first page holds "One"; on the
        // next page the big paragraph is at the start and still does not fit, so this is an error, not a loop.
        var error = Fail(Document(Paragraph(1, "One"), Paragraph(2, "Huge", size: 100)));

        error.Code.Should().Be(LayoutErrorCodes.DoesNotFit);
        error.Metadata![ErrorMetadataKeys.Node].Should().Be("#2");
    }

    [Fact]
    public void A_font_that_is_not_in_the_font_set_is_not_found_with_the_runs_location()
    {
        var run = new TextRun(new NodeId(2), "x", new TextStyle("Missing Sans", 10.Pt(), FontWeight.Regular, Color.Black), At(77));
        var paragraph = new Box(new NodeId(1), SemanticRole.Paragraph, LayoutAlgorithmId.Inline, BoxStyle.Initial, [run], At(76));

        var error = Fail(Document(paragraph));

        error.Code.Should().Be(FontErrorCodes.NotFound);
        error.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(77);
    }

    [Fact]
    public void A_missing_glyph_is_an_error_with_the_runs_location()
    {
        var error = Fail(Document(Paragraph(1, "Kanji 漢")));

        error.Code.Should().Be(FontErrorCodes.GlyphMissing);
        error.Metadata![ErrorMetadataKeys.CodePoint].Should().Be("U+6F22");
        error.Metadata.Should().ContainKey(ErrorMetadataKeys.SourceLine);
    }

    [Fact]
    public void Text_directly_in_a_flow_box_is_invalid_content()
    {
        var flow = Flow(1, [new TextRun(new NodeId(2), "loose text", Models.Text(), At(1))]);

        Fail(Document(flow)).Code.Should().Be(LayoutErrorCodes.InvalidContent);
    }

    [Fact]
    public void An_unknown_layout_algorithm_is_invalid_content()
    {
        var box = Flow(1, []) with { Layout = new LayoutAlgorithmId("grid") };

        var error = Fail(Document(box));

        error.Code.Should().Be(LayoutErrorCodes.InvalidContent);
        error.Metadata![ErrorMetadataKeys.Value].Should().Be("grid");
    }

    [Fact]
    public void Page_margins_that_leave_no_area_are_invalid()
    {
        var document = Document(Paragraph(1, "x")) with { Page = new PageStyle(100.Pt(), 100.Pt(), Edges.All(50.Pt())) };

        Fail(document).Code.Should().Be(DocumentErrorCodes.Input.Invalid.PageSize);
    }

    [Fact]
    public void The_layout_algorithm_id_is_open_so_a_front_end_can_add_its_own_algorithm()
    {
        // A trivial algorithm: every box is a fixed 50 × 30 pt rectangle.
        LayoutAlgorithm fixedSize = (_, box, space, _) =>
            LayoutResult.Place(new BoxFragment(box, new Size(50, 30), []), null);
        var environment = LayoutEnvironment.Create(TestFonts.Set).WithAlgorithm(new LayoutAlgorithmId("fixed"), fixedSize);
        var custom = Flow(1, []) with { Layout = new LayoutAlgorithmId("fixed") };

        var pages = Paginator.Paginate(environment, Document(custom, Paragraph(2, "after"))).Value;

        var root = (BoxFragment)pages.Single().Root.Fragment;
        root.Children[0].Fragment.Size.Should().Be(new Size(50, 30));
        root.Children[1].Offset.Y.Should().Be(30);
    }

    private static int CurrentLine([System.Runtime.CompilerServices.CallerLineNumber] int line = 0) => line;
}
