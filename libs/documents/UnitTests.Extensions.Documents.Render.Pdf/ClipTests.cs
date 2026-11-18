using System.Collections.Immutable;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.Pdf;
using Ave.Extensions.Documents.Render.Pdf;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Documents.Typesetting;

namespace UnitTests.Extensions.Documents.Render.Pdf;

/// <summary>
/// The page model's clips (<see cref="ClipStart"/>, <see cref="ClipEnd"/>) in PDF, on hand-made page models: a clip is
/// <c>q x y w h re W n</c> … <c>Q</c>, opened and closed between marked-content sequences so the two kinds of pair nest,
/// and THE NESTING RULE FOR MARKERS (<see cref="DrawItem"/>) holds for clips as for structure and artifacts.
/// </summary>
public class ClipTests
{
    private static readonly PdfRenderOptions Tagged = new(new DateTimeOffset(2026, 11, 15, 9, 22, 0, TimeSpan.FromHours(1)))
    {
        Profile = PdfProfile.PdfA3a,
    };

    private static readonly SourceLocation Here = new("ClipTests.cs", 1);

    private static readonly ClipStart Clip = new(new Rect(new Point(0, 20), new Size(200, 30)));

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
        new(new DocumentMetadata("Clips", null) { Language = "en" }, [new Page(new Size(200, 100), [.. items])]);

    private static StructureStart Start(int node) => new(new NodeId(node), SemanticRole.Paragraph, Here);

    private static StructureEnd End(int node) => new(new NodeId(node));

    private static List<string> Operators(ImmutableArray<byte> pdf)
    {
        using var read = UglyToad.PdfPig.PdfDocument.Open(pdf.ToArray());
        return [.. read.GetPage(1).Operations.Select(operation => operation.Operator)];
    }

    [Fact]
    public void A_clip_around_tagged_content_is_a_saved_state_with_a_clipping_rectangle_around_its_marked_content()
    {
        var pdf = PdfRenderer.Render(Document(Start(1), Clip, Run("clipped", new Point(10, 40)), ClipEnd.Instance, End(1)), Tagged).Value;

        var operators = Operators(pdf);
        var begin = operators.IndexOf("BDC");
        var end = operators.LastIndexOf("EMC");
        operators.Take(begin).Should().EndWith(["q", "re", "W", "n"], "the clip opens before the marked-content sequence");
        operators.Skip(end + 1).Should().Equal(["Q"], "and closes after it");
    }

    [Fact]
    public void The_clipping_rectangle_is_in_PDF_user_space()
    {
        // Page coordinates (y down, from the top) become user space (y up, from the bottom): a 30 pt band whose top is 20 pt
        // below the top of a 100 pt page has its bottom edge at y = 50.
        var pdf = PdfRenderer.Render(Document(Start(1), Clip, Run("clipped", new Point(10, 40)), ClipEnd.Instance, End(1)), Tagged with { Profile = PdfProfile.Plain }).Value;

        using var read = UglyToad.PdfPig.PdfDocument.Open(pdf.ToArray());
        var rectangle = read.GetPage(1).Operations.First(operation => operation.Operator == "re");
        var written = new System.IO.StringWriter();
        rectangle.Write(new MemoryStreamWriter(written));
        written.ToString().Should().StartWith("0 50 200 30 re");
    }

    [Fact]
    public void Clips_that_hold_artifacts_and_content_open_and_close_between_marked_content_sequences()
    {
        var pdf = PdfRenderer.Render(Document(
            Start(1),
            Run("before", new Point(10, 15)),
            Clip,
            Run("inside", new Point(10, 40)),
            new ArtifactStart(ArtifactKind.Repeated), Run("copy", new Point(10, 45)), ArtifactEnd.Instance,
            ClipEnd.Instance,
            Run("after", new Point(10, 80)),
            End(1)), Tagged).Value;

        var operators = Operators(pdf).Where(name => name is "q" or "Q" or "W" or "BDC" or "EMC").ToList();
        // Each glyph run draws inside its own q…Q too; the marked-content sequences and the clip's pair never cross.
        var depth = new Stack<string>();
        foreach (var name in operators)
        {
            switch (name)
            {
                case "q" or "BDC":
                    depth.Push(name);
                    break;
                case "Q":
                    depth.Pop().Should().Be("q", "a Q closes a q, never a marked-content sequence");
                    break;
                case "EMC":
                    depth.Pop().Should().Be("BDC", "an EMC closes a BDC, never a saved state");
                    break;
            }
        }

        depth.Should().BeEmpty();
        Operators(pdf).Count(name => name == "W").Should().Be(1);
    }

    [Theory]
    [InlineData("clip end without start")]
    [InlineData("clip start without end")]
    [InlineData("clip ends inside a structure node it is outside of")]
    [InlineData("structure node ends inside a clip it is outside of")]
    [InlineData("clip ends inside an artifact")]
    public void Clips_that_do_not_nest_with_the_other_markers_are_an_invalid_structure(string problem)
    {
        DrawItem[] items = problem switch
        {
            "clip end without start" => [Start(1), ClipEnd.Instance, End(1)],
            "clip start without end" => [Start(1), Clip, Run("x", new Point(10, 40)), End(1)],
            "clip ends inside a structure node it is outside of" => [Clip, Start(1), Run("x", new Point(10, 40)), ClipEnd.Instance, End(1)],
            "structure node ends inside a clip it is outside of" => [Start(1), Clip, Run("x", new Point(10, 40)), End(1), ClipEnd.Instance],
            _ => [Start(1), Clip, new ArtifactStart(ArtifactKind.Layout), Run("x", new Point(10, 40)), ClipEnd.Instance, ArtifactEnd.Instance, End(1)],
        };

        PdfRenderer.Render(Document(items), Tagged with { Profile = PdfProfile.Plain }).Error.Code
            .Should().Be(PdfRenderErrorCodes.Write.InvalidStructure);
    }

    [Fact]
    public void A_clip_inside_an_artifact_and_a_clip_inside_a_clip_are_allowed()
    {
        var document = Document(
            new ArtifactStart(ArtifactKind.PageHeader), new ClipStart(new Rect(new Point(0, 0), new Size(200, 20))), Run("head", new Point(10, 15)), ClipEnd.Instance, ArtifactEnd.Instance,
            Start(1), Clip, Clip with { Area = new Rect(new Point(0, 25), new Size(200, 10)) }, Run("x", new Point(10, 33)), ClipEnd.Instance, ClipEnd.Instance, End(1));

        PdfRenderer.Render(document, Tagged).IsSuccess.Should().BeTrue();
        Operators(PdfRenderer.Render(document, Tagged).Value).Count(name => name == "W").Should().Be(3);
    }

    [Fact]
    public void The_text_form_writes_a_clip_and_its_end()
    {
        PageModelText.Write(Document(Start(1), Clip, ClipEnd.Instance, End(1))).Should().Contain("  clip (0, 20) 200x30\n  end clip\n");
    }

    /// <summary>Writes PdfPig's operation text into a <see cref="System.IO.StringWriter"/>.</summary>
    private sealed class MemoryStreamWriter(System.IO.StringWriter target) : System.IO.MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count) => target.Write(System.Text.Encoding.ASCII.GetString(buffer, offset, count));

        public override void Write(ReadOnlySpan<byte> buffer) => target.Write(System.Text.Encoding.ASCII.GetString(buffer));

        public override void WriteByte(byte value) => target.Write((char)value);
    }
}
