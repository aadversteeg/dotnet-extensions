using System.Collections.Immutable;
using System.Text;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.Pdf;
using Ave.Extensions.Documents.Render.Pdf;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Documents.Typesetting;

namespace UnitTests.Extensions.Documents.Render.Pdf;

/// <summary>
/// Tagged PDF from the page model's markers, on hand-made page models: one structure element per node even across
/// pages, artifacts, the nesting rules the back-end enforces, and that a plain file stays untouched.
/// </summary>
public class TaggingTests
{
    private static readonly PdfRenderOptions Tagged = new(new DateTimeOffset(2026, 11, 15, 9, 22, 0, TimeSpan.FromHours(1)))
    {
        Profile = PdfProfile.PdfA3a,
    };

    private static readonly SourceLocation Here = new("TaggingTests.cs", 1);

    private static GlyphRun Run(string text, Point origin)
    {
        var glyphs = Shaper.Shape(text, TestFonts.Regular, 10).Value;
        var x = 0.0;
        var positioned = ImmutableArray.CreateBuilder<PositionedGlyph>();
        foreach (var glyph in glyphs)
        {
            positioned.Add(new PositionedGlyph(glyph.Glyph, x, glyph.Text));
            x += glyph.Advance;
        }

        return new GlyphRun(TestFonts.Regular, 10, Color.Black, origin, positioned.ToImmutable());
    }

    private static Page Page(params DrawItem[] items) => new(new Size(200, 100), [.. items]);

    private static PageDocument Document(params Page[] pages) => new(new DocumentMetadata("Tagging", null) { Language = "en" }, [.. pages]);

    private static StructureStart Start(int node, SemanticRole role) => new(new NodeId(node), role, Here);

    private static StructureEnd End(int node) => new(new NodeId(node));

    /// <summary>A document node and a paragraph that continues from page 1 onto page 2, with a header artifact on both.</summary>
    private static PageDocument SplitParagraph() => Document(
        Page(
            new ArtifactStart(ArtifactKind.PageHeader), Run("Header", new Point(10, 10)), ArtifactEnd.Instance,
            Start(0, SemanticRole.Document), Start(1, SemanticRole.Paragraph), Run("first ", new Point(10, 50)), End(1), End(0)),
        Page(
            new ArtifactStart(ArtifactKind.PageHeader), Run("Header", new Point(10, 10)), ArtifactEnd.Instance,
            Start(0, SemanticRole.Document), Start(1, SemanticRole.Paragraph), Run("second", new Point(10, 50)), End(1), End(0)));

    [Fact]
    public void A_node_split_over_two_pages_is_one_element_with_its_text_from_both_pages_in_order()
    {
        var pdf = PdfRenderer.Render(SplitParagraph(), Tagged).Value;

        PdfStructureReadBack.Outline(PdfStructureReadBack.Read(pdf)).Should().Be("Document\n  P \"first second\"\n");
        Encoding.Latin1.GetString(pdf.AsSpan()).Should().Contain("/Type /MCR", "content on another page than the element's /Pg is referenced with its own page");
    }

    [Fact]
    public void Header_text_is_a_pagination_artifact_and_not_in_the_structure()
    {
        var pdf = PdfRenderer.Render(SplitParagraph(), Tagged).Value;

        using var read = UglyToad.PdfPig.PdfDocument.Open(pdf.ToArray());
        var artifact = read.GetPage(1).GetMarkedContents().Should().Contain(content => content.IsArtifact).Which;
        string.Concat(artifact.Letters.Select(letter => letter.Value)).Should().Be("Header");
        artifact.Properties.Data["Type"].ToString().Should().Be("/Pagination");
        artifact.Properties.Data["Subtype"].ToString().Should().Be("/Header");
        PdfStructureReadBack.Outline(PdfStructureReadBack.Read(pdf)).Should().NotContain("Header");
    }

    [Fact]
    public void Content_outside_every_structure_node_and_artifact_is_refused_when_tagged_but_drawn_when_plain()
    {
        var document = Document(Page(Run("loose", new Point(10, 50))));

        PdfRenderer.Render(document, Tagged).Error.Code.Should().Be(PdfRenderErrorCodes.Conformance.Tagged.UnmarkedContent);
        PdfRenderer.Render(document, Tagged with { Profile = PdfProfile.Plain }).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("end without start")]
    [InlineData("start without end")]
    [InlineData("structure inside artifact")]
    [InlineData("nested artifact")]
    public void Markers_that_do_not_nest_break_the_page_model_contract_in_every_profile(string problem)
    {
        DrawItem[] items = problem switch
        {
            "end without start" => [End(1)],
            "start without end" => [Start(1, SemanticRole.Paragraph)],
            "structure inside artifact" => [new ArtifactStart(ArtifactKind.Layout), Start(1, SemanticRole.Paragraph), End(1), ArtifactEnd.Instance],
            _ => [new ArtifactStart(ArtifactKind.Layout), new ArtifactStart(ArtifactKind.Layout), ArtifactEnd.Instance, ArtifactEnd.Instance],
        };

        PdfRenderer.Render(Document(Page(items)), Tagged with { Profile = PdfProfile.Plain }).Error.Code
            .Should().Be(PdfRenderErrorCodes.Write.InvalidPageModel);
    }

    [Fact]
    public void A_node_that_moves_to_another_parent_on_the_next_page_is_an_invalid_structure()
    {
        var document = Document(
            Page(Start(0, SemanticRole.Document), Start(1, SemanticRole.Paragraph), Run("a", new Point(10, 50)), End(1), End(0)),
            Page(Start(1, SemanticRole.Paragraph), Run("b", new Point(10, 50)), End(1)));

        var error = PdfRenderer.Render(document, Tagged).Error;

        error.Code.Should().Be(PdfRenderErrorCodes.Write.InvalidStructure);
        error.Metadata![ErrorMetadataKeys.Node].Should().Be("#1");
    }

    [Fact]
    public void A_tagged_file_is_deterministic() =>
        PdfRenderer.Render(SplitParagraph(), Tagged).Value.Should().Equal(PdfRenderer.Render(SplitParagraph(), Tagged).Value);

    [Fact]
    public void A_plain_file_has_no_conformance_objects_and_no_marked_content()
    {
        var pdf = PdfRenderer.Render(SplitParagraph(), Tagged with { Profile = PdfProfile.Plain }).Value;

        var file = Encoding.Latin1.GetString(pdf.AsSpan());
        file.Should().NotContain("/StructTreeRoot").And.NotContain("/Metadata").And.NotContain("/OutputIntents").And.NotContain("/MarkInfo");
        using var read = UglyToad.PdfPig.PdfDocument.Open(pdf.ToArray());
        read.GetPages().Should().OnlyContain(page => page.GetMarkedContents().Count == 0);
    }

    [Fact]
    public void Table_cells_carry_scope_and_spans_as_table_attributes()
    {
        var document = Document(Page(
            Start(0, SemanticRole.Document),
            Start(1, SemanticRole.Table),
            Start(2, SemanticRole.TableHead), Start(3, SemanticRole.TableRow),
            Start(4, SemanticRole.TableHeaderCell) with { Span = new CellSpan(2, 1) }, Run("H", new Point(10, 20)), End(4),
            End(3), End(2),
            Start(5, SemanticRole.TableBody), Start(6, SemanticRole.TableRow),
            Start(7, SemanticRole.TableCell), Run("a", new Point(10, 40)), End(7),
            Start(8, SemanticRole.TableCell) with { Span = new CellSpan(1, 3) }, Run("b", new Point(60, 40)), End(8),
            End(6), End(5),
            End(1), End(0)));

        var outline = PdfStructureReadBack.Outline(PdfStructureReadBack.Read(PdfRenderer.Render(document, Tagged).Value));

        outline.Should().Be(
            "Document\n  Table\n    THead\n      TR\n        TH Scope=Column ColSpan=2 \"H\"\n"
            + "    TBody\n      TR\n        TD \"a\"\n        TD RowSpan=3 \"b\"\n");
    }

    [Fact]
    public void A_header_cell_carries_the_scope_of_its_role_row_or_both()
    {
        var document = Document(Page(
            Start(0, SemanticRole.Document),
            Start(1, SemanticRole.Table),
            Start(2, SemanticRole.TableBody), Start(3, SemanticRole.TableRow),
            Start(4, SemanticRole.TableHeaderCellFor(TableHeaderScope.Both)), Run("H", new Point(10, 20)), End(4),
            Start(5, SemanticRole.TableHeaderCell), Run("C", new Point(60, 20)), End(5),
            End(3), Start(6, SemanticRole.TableRow),
            Start(7, SemanticRole.TableHeaderCellFor(TableHeaderScope.Row)), Run("R", new Point(10, 40)), End(7),
            Start(8, SemanticRole.TableCell), Run("a", new Point(60, 40)), End(8),
            End(6), End(2),
            End(1), End(0)));

        var outline = PdfStructureReadBack.Outline(PdfStructureReadBack.Read(PdfRenderer.Render(document, Tagged).Value));

        outline.Should().Be(
            "Document\n  Table\n    TBody\n      TR\n        TH Scope=Both \"H\"\n        TH Scope=Column \"C\"\n"
            + "      TR\n        TH Scope=Row \"R\"\n        TD \"a\"\n");
    }
}
