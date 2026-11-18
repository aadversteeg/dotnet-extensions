using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.ErrorPaths;
using static UnitTests.Extensions.Documents.Layout.Models;

namespace UnitTests.Extensions.Documents.Layout;

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

    [Fact]
    public void An_algorithm_that_makes_no_progress_is_reported_as_a_defect_of_the_engine_not_thrown()
    {
        // A broken root algorithm: it always asks to resume at its first child, also when it was told to resume there.
        LayoutAlgorithm stuck = (_, box, space, _) =>
            LayoutResult.Place(new BoxFragment(box, new Size(space.AvailableWidth, 10), []), new FlowBreakToken(0, null));
        var environment = LayoutEnvironment.Create(TestFonts.Set).WithAlgorithm(LayoutAlgorithmId.Flow, stuck);

        var result = Paginator.Paginate(environment, Document(Paragraph(1, "x")));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(LayoutErrorCodes.EngineDefect);
        result.Error.Metadata![ErrorMetadataKeys.Value].Should().Be("made no progress: it returned the break token it was given");
    }

    [Fact]
    public void An_algorithm_that_has_no_room_at_the_start_of_a_page_is_reported_as_a_defect_of_the_engine_not_thrown()
    {
        LayoutAlgorithm noRoom = (_, _, _, _) => Ave.Extensions.Functional.Result<LayoutResult, Error>.Success(LayoutResult.NoRoom.Instance);
        var environment = LayoutEnvironment.Create(TestFonts.Set).WithAlgorithm(LayoutAlgorithmId.Flow, noRoom);

        var result = Paginator.Paginate(environment, Document(Paragraph(1, "x")));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(LayoutErrorCodes.EngineDefect);
        result.Error.Metadata![ErrorMetadataKeys.Value].Should().Be("reported no room at the start of a page");
    }

    [Fact]
    public void A_root_whose_top_margin_leaves_no_room_says_so_and_reports_the_room_it_had()
    {
        // Review 40: the wording "which has no room left for it" and the metadata value of DoesNotFit were asserted
        // nowhere. A root box with a 90 pt top margin on an 80 pt page area has -10 pt left for its content.
        var document = Document(Paragraph(1, "x"));
        document = document with { Root = document.Root with { Style = BoxStyle.Initial with { Margin = new Edges(90.Pt(), 0.Pt(), 0.Pt(), 0.Pt()), Padding = Edges.All(1.Pt()) } } };

        var error = Fail(document);

        error.Code.Should().Be(LayoutErrorCodes.DoesNotFit);
        error.Message.Should().Contain("does not fit in the page, which has no room left for it, and cannot be broken");
        error.Metadata![ErrorMetadataKeys.Value].Should().Be("-10");
    }

    private static int CurrentLine([System.Runtime.CompilerServices.CallerLineNumber] int line = 0) => line;
}
