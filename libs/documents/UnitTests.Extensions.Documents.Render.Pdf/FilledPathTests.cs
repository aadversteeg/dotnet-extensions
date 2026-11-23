using System.Collections.Immutable;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.Pdf;
using Ave.Extensions.Documents.Render.Pdf;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Documents.Typesetting;

namespace UnitTests.Extensions.Documents.Render.Pdf;

/// <summary>
/// The page model's filled path (<see cref="FillPath"/>) in PDF, on hand-made page models: inside <c>q</c>…<c>Q</c> its
/// colour, each contour as <c>m</c>, <c>c</c> or <c>l</c> per segment and <c>h</c>, every y as its height above the foot of
/// the page, then <c>f</c> or <c>f*</c> by its fill rule (ISO 32000-1 §8.5.2.1, §8.5.3.1); inside the same marked content
/// as the items beside it, or in an artifact; and a tagged file with paths as content and as an artifact passes veraPDF.
/// </summary>
public class FilledPathTests
{
    private static readonly PdfRenderOptions Tagged = new(new DateTimeOffset(2026, 11, 23, 20, 45, 0, TimeSpan.FromHours(1)))
    {
        Profile = PdfProfile.PdfA3a,
    };

    private static readonly SourceLocation Here = new("FilledPathTests.cs", 1);

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

    private static PageDocument Document(params DrawItem[] items) =>
        new(new DocumentMetadata("Filled paths", null) { Language = "en" }, [new Page(new Size(200, 100), [.. items])]);

    private static StructureStart Start(int node, SemanticRole? role = null) => new(new NodeId(node), role ?? SemanticRole.Paragraph, Here);

    private static StructureEnd End(int node) => new(new NodeId(node));

    /// <summary>Every operation of page 1 as the content stream writes it (operands and operator), in order.</summary>
    private static List<string> Written(ImmutableArray<byte> pdf)
    {
        using var read = UglyToad.PdfPig.PdfDocument.Open(pdf.ToArray());
        return [.. read.GetPage(1).Operations.Select(operation =>
        {
            var text = new System.IO.StringWriter();
            operation.Write(new MemoryStreamWriter(text));
            return text.ToString().Trim();
        })];
    }

    private static ImmutableArray<byte> Rendered(PageDocument document, PdfRenderOptions options)
    {
        var pdf = PdfRenderer.Render(document, options);
        pdf.IsSuccess.Should().BeTrue(pdf.IsFailure ? pdf.Error.ToString() : string.Empty);
        return pdf.Value;
    }

    [Fact]
    public void A_disc_is_its_colour_four_curves_closed_and_filled_by_the_non_zero_rule_in_user_space()
    {
        // The disc's centre is 21.375 pt below the top of a 100 pt page: 78.625 above its foot. Numbers have at most four
        // decimals, as every number the writer writes.
        var written = Written(Rendered(Document(TestPaths.Disc(31.875, 21.375, 1.875, new Color(255, 0, 0))), Tagged with { Profile = PdfProfile.Plain }));

        written.Should().Equal(
            "q",
            "1 0 0 rg",
            "33.75 78.625 m",
            "33.75 77.5895 32.9105 76.75 31.875 76.75 c",
            "30.8395 76.75 30 77.5895 30 78.625 c",
            "30 79.6605 30.8395 80.5 31.875 80.5 c",
            "32.9105 80.5 33.75 79.6605 33.75 78.625 c",
            "h",
            "f",
            "Q");
    }

    [Fact]
    public void A_ring_is_two_closed_contours_filled_by_the_even_odd_rule_and_lines_are_written_as_lines()
    {
        var ring = Written(Rendered(Document(TestPaths.Ring(61.875, 31.875, 2.25, 1.5)), Tagged with { Profile = PdfProfile.Plain }));
        ring.Count(operation => operation.EndsWith(" m", StringComparison.Ordinal)).Should().Be(2);
        ring.Count(operation => operation == "h").Should().Be(2);
        ring[^2].Should().Be("f*");

        var polygon = Written(Rendered(Document(TestPaths.Polygon(new Point(10, 20), new Point(14.5, 20), new Point(10, 24))), Tagged with { Profile = PdfProfile.Plain }));
        polygon.Should().Equal("q", "0 0 0 rg", "10 80 m", "14.5 80 l", "10 76 l", "h", "f", "Q");
    }

    [Fact]
    public void A_filled_path_is_in_the_marked_content_of_the_node_it_belongs_to_beside_the_text()
    {
        var operators = Written(Rendered(Document(Start(0, SemanticRole.Document), Start(1), Run("one", new Point(40, 30)), TestPaths.Disc(31.875, 26.375, 1.875), End(1), End(0)), Tagged))
            .Select(operation => operation.Split(' ')[^1]).ToList();

        var begin = operators.IndexOf("BDC");
        operators.Count(name => name == "BDC").Should().Be(1, "the text and the disc are one marked-content sequence of the paragraph");
        operators.Skip(begin).Should().ContainInOrder("BT", "ET", "m", "c", "h", "f", "EMC");
        operators.LastIndexOf("f").Should().BeLessThan(operators.LastIndexOf("EMC"));
    }

    [Fact]
    public void A_filled_path_in_an_artifact_is_marked_as_an_artifact()
    {
        var written = Written(Rendered(Document(new ArtifactStart(ArtifactKind.Layout), TestPaths.Disc(31.875, 26.375, 1.875), ArtifactEnd.Instance), Tagged));

        written[0].Should().StartWith("/Artifact").And.EndWith("BDC");
        written.Should().ContainInOrder("h", "f", "EMC");
    }

    [Fact]
    public void A_filled_path_inside_a_clip_is_drawn_inside_the_clipping_rectangle()
    {
        var operators = Written(Rendered(Document(new ClipStart(new Rect(new Point(0, 20), new Size(32, 10))), TestPaths.Disc(31.875, 26.375, 1.875), ClipEnd.Instance), Tagged with { Profile = PdfProfile.Plain }))
            .Select(operation => operation.Split(' ')[^1]).ToList();

        operators.Should().Equal("q", "re", "W", "n", "q", "rg", "m", "c", "c", "c", "c", "h", "f", "Q", "Q");
    }

    [Fact]
    public void A_figure_s_box_holds_the_curves_of_its_filled_path()
    {
        // A curve from (10, 50) to (30, 50) through control points 20 pt higher bulges to y 35 at t = 1/2, not to the control
        // points' 30: the box is the curve's, in user space (a 100 pt page) y 50 to 65.
        var bulge = new FillPath([new PathContour(new Point(10, 50), [new PathCubic(new Point(10, 30), new Point(30, 30), new Point(30, 50))])], PathFillRule.NonZero, Color.Black);
        var document = Document(Start(0, SemanticRole.Document), Start(1, SemanticRole.Figure("a bulge")), bulge, End(1), End(0));

        PdfStructureReadBack.Read(Rendered(document, Tagged)).SelectMany(element => element.DepthFirst()).Single(element => element.Type == "Figure")
            .BoundingBox.Should().Equal(10, 50, 30, 65);
    }

    [Fact]
    public void A_filled_path_beyond_the_limit_of_segments_breaks_the_page_model_contract()
    {
        var segments = Enumerable.Range(1, PageModelRules.MaxPathSegments + 1).Select(index => (PathSegment)new PathLine(new Point(index * 0.01, index % 2)));
        var path = new FillPath([new PathContour(new Point(0, 0), [.. segments])], PathFillRule.NonZero, Color.Black);

        PdfRenderer.Render(Document(path), Tagged with { Profile = PdfProfile.Plain }).Error.Code.Should().Be(PdfRenderErrorCodes.Write.InvalidPageModel);
    }

    /// <summary>A tagged page with a disc as content of a paragraph, beside its text, and a ring as an artifact.</summary>
    private static PageDocument ContentAndArtifact() => Document(
        Start(0, SemanticRole.Document),
        Start(1),
        Run("Item", new Point(40, 30)),
        TestPaths.Disc(31.875, 26.375, 1.875),
        End(1),
        new ArtifactStart(ArtifactKind.Layout),
        TestPaths.Ring(31.875, 50.375, 2.25, 1.5),
        ArtifactEnd.Instance,
        End(0));

    [DockerTheory]
    [Trait("Category", "Reference")]
    [InlineData("3a")]
    [InlineData("ua1")]
    public void A_file_with_filled_paths_as_content_and_as_an_artifact_passes_veraPDF(string flavour)
    {
        var folder = System.IO.Directory.CreateTempSubdirectory("ave-extensions-documents-paths-").FullName;
        try
        {
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, "filled-paths.pdf"),
                [.. Rendered(ContentAndArtifact(), Tagged with { Profile = flavour == "3a" ? PdfProfile.PdfA3a : PdfProfile.PdfUa1 })]);

            var result = VeraPdf.Validate(folder, flavour).Single();

            result.IsCompliant.Should().BeTrue(result.ToString());
        }
        finally
        {
            System.IO.Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>Writes PdfPig's operation text into a <see cref="System.IO.StringWriter"/>.</summary>
    private sealed class MemoryStreamWriter(System.IO.StringWriter target) : System.IO.MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count) => target.Write(System.Text.Encoding.ASCII.GetString(buffer, offset, count));

        public override void Write(ReadOnlySpan<byte> buffer) => target.Write(System.Text.Encoding.ASCII.GetString(buffer));

        public override void WriteByte(byte value) => target.Write((char)value);
    }
}
