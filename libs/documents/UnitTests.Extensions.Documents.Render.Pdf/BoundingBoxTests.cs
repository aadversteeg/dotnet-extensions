using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.Pdf;
using Ave.Extensions.Documents.Render.Pdf;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Render.Pdf;

/// <summary>
/// The <c>/BBox</c> layout attribute of figures and tables (ISO 32000-1 §14.8.5.4.3, Table 344): "required for any
/// figure or table appearing in its entirety on a single page", the rectangle that encloses its visible content, in
/// default user space (origin bottom-left, y up). The pages here are 200 × 100 points.
/// </summary>
public class BoundingBoxTests
{
    private static readonly PdfRenderOptions Tagged = new(new DateTimeOffset(2026, 11, 15, 9, 22, 0, TimeSpan.FromHours(1)))
    {
        Profile = PdfProfile.PdfA3a,
    };

    private static readonly SourceLocation Here = new("BoundingBoxTests.cs", 1);

    private static Page Page(params DrawItem[] items) => new(new Size(200, 100), [.. items]);

    private static StructureStart Start(int node, SemanticRole role) => new(new NodeId(node), role, Here);

    private static StructureEnd End(int node) => new(new NodeId(node));

    private static DrawImage Image(Rect area) => new(area, TestImages.Load("rgba8.png"));

    private static GlyphRun Text(string text, Point origin) =>
        new(TestFonts.Regular, 10, Color.Black, origin, [new PositionedGlyph(TestFonts.Regular.GlyphFor(new System.Text.Rune(text[0])).Value, 0, text)]);

    private static IEnumerable<PdfStructureReadBack.Element> Elements(params Page[] pages)
    {
        var document = new PageDocument(new DocumentMetadata("Boxes", null) { Language = "en" }, [.. pages]);
        var pdf = PdfRenderer.Render(document, Tagged);
        pdf.IsSuccess.Should().BeTrue(pdf.IsFailure ? pdf.Error.ToString() : string.Empty);
        return PdfStructureReadBack.Read(pdf.Value).SelectMany(element => element.DepthFirst());
    }

    [Fact]
    public void A_figure_on_one_page_has_the_box_of_its_image_in_user_space()
    {
        // The image covers x 20–60 and, y down, 30–50: in user space y = 100 − 50 = 50 to 100 − 30 = 70.
        var elements = Elements(Page(
            Start(0, SemanticRole.Document), Start(1, SemanticRole.Figure("a square")),
            Image(new Rect(new Point(20, 30), new Size(40, 20))),
            End(1), End(0)));

        elements.Single(element => element.Type == "Figure").BoundingBox.Should().Equal(20, 50, 60, 70);
        elements.Single(element => element.Type == "Document").BoundingBox.Should().BeNull("only figures and tables have one");
    }

    [Fact]
    public void The_box_of_text_is_the_union_of_its_glyphs_outline_boxes()
    {
        // Glyph boxes from fontTools 4.68.0 (Lato Regular, 2000 units per em; at 10 pt a unit is 1/200 pt):
        // 'A' at x 0: 6, 0, 1348, 1433; 'g' at x 20: 62, −365, 996, 1030. The baseline is at y 50 (y down).
        // Left 10 + 6/200, right 10 + 20 + 996/200, top 50 − 1433/200, bottom 50 + 365/200; then y' = 100 − y.
        var run = new GlyphRun(TestFonts.Regular, 10, Color.Black, new Point(10, 50),
            [new PositionedGlyph(new GlyphId(3), 0, "A"), new PositionedGlyph(new GlyphId(93), 20, "g")]);

        var box = Elements(Page(Start(1, SemanticRole.Figure("A and g")), run, End(1)))
            .Single(element => element.Type == "Figure").BoundingBox!;

        box.Should().HaveCount(4);
        box[0].Should().BeApproximately(10.03, 1e-9);
        box[1].Should().BeApproximately(100 - (50 + 1.825), 1e-9);
        box[2].Should().BeApproximately(34.98, 1e-9);
        box[3].Should().BeApproximately(100 - (50 - 7.165), 1e-9);
    }

    [Fact]
    public void A_table_s_box_encloses_its_cells_and_the_grid_lines_drawn_inside_it()
    {
        // The grid line is a layout artifact, but it is visible content drawn inside the table's markers.
        var elements = Elements(Page(
            Start(1, SemanticRole.Table),
            new ArtifactStart(ArtifactKind.Layout), new FillRectangle(new Rect(new Point(10, 20), new Size(180, 1)), Color.Black), ArtifactEnd.Instance,
            Start(2, SemanticRole.TableBody), Start(3, SemanticRole.TableRow), Start(4, SemanticRole.TableCell),
            Image(new Rect(new Point(30, 40), new Size(20, 40))),
            End(4), End(3), End(2),
            End(1)));

        elements.Single(element => element.Type == "Table").BoundingBox.Should().Equal(10, 20, 190, 80);
        elements.Single(element => element.Type == "TD").BoundingBox.Should().BeNull();
    }

    [Fact]
    public void A_figure_on_a_later_page_is_flipped_with_that_page_s_height()
    {
        // Page 1 is 100 high, page 2 is 300 high: the image at y 30–50 on page 2 is at 250–270 in its user space.
        var first = Page(Start(0, SemanticRole.Paragraph), Text("a", new Point(20, 50)), End(0));
        var second = new Page(new Size(200, 300), [Start(1, SemanticRole.Figure("a square")), Image(new Rect(new Point(20, 30), new Size(40, 20))), End(1)]);

        Elements(first, second).Single(element => element.Type == "Figure").BoundingBox.Should().Equal(20, 250, 60, 270);
    }

    [Fact]
    public void A_raised_glyph_raises_the_box()
    {
        // The same 'A' (yMin 0, yMax 1433 units, 7.165 pt at 10 pt) with its baseline 4 pt higher: glyph Y = −4.
        var run = new GlyphRun(TestFonts.Regular, 10, Color.Black, new Point(10, 50), [new PositionedGlyph(new GlyphId(3), 0, "A", -4)]);

        var box = Elements(Page(Start(1, SemanticRole.Figure("A")), run, End(1))).Single(element => element.Type == "Figure").BoundingBox!;

        box[1].Should().BeApproximately(100 - 46, 1e-9, "the bottom is the raised baseline");
        box[3].Should().BeApproximately(100 - (46 - 7.165), 1e-9);
    }

    [Fact]
    public void A_table_continued_on_the_next_page_has_no_box()
    {
        // Not "in its entirety on a single page": no single rectangle of one page encloses it.
        Page Part(int row, string text) => Page(
            Start(1, SemanticRole.Table), Start(2, SemanticRole.TableBody), Start(row, SemanticRole.TableRow),
            Start(row + 1, SemanticRole.TableCell), Text(text, new Point(20, 50)), End(row + 1), End(row), End(2), End(1));

        var elements = Elements(Part(3, "a"), Part(5, "b"));

        elements.Single(element => element.Type == "Table").BoundingBox.Should().BeNull();
    }
}
