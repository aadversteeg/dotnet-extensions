using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Graphics;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.Render.Raster;
using Ave.Extensions.Functional;

namespace UnitTests.Extensions.Documents.Render.Raster;

/// <summary>
/// The page model's clips (<see cref="ClipStart"/>, <see cref="ClipEnd"/>) in the raster back-end: THE NESTING RULE FOR
/// MARKERS (<see cref="DrawItem"/>) and the limit of <see cref="PageModelRules.MaxClipDepth"/> clips are the page-model
/// contract both back-ends check (<see cref="PageModelRules"/>), so a page model the PDF back-end refuses for them, the
/// raster back-end refuses too, under its own code.
/// </summary>
public class ClipTests
{
    private static readonly ClipStart Clip = new(new Rect(new Point(0, 20), new Size(100, 30)));

    private static Page PageOf(params DrawItem[] items) => new(new Size(100, 100), [.. items]);

    private static Result<RgbaImage, Ave.Extensions.ErrorPaths.Error> Draw(Page page) => PageRasterizer.Render(page, new RasterOptions(72));

    private static DrawItem[] Nested(int depth) =>
    [
        .. Enumerable.Repeat<DrawItem>(Clip, depth),
        new FillRectangle(new Rect(new Point(10, 25), new Size(50, 10)), Color.Black),
        .. Enumerable.Repeat<DrawItem>(ClipEnd.Instance, depth),
    ];

    [Theory]
    [InlineData(28)]
    [InlineData(20_000)]
    public void More_than_27_clips_one_inside_the_other_are_refused_as_the_PDF_back_end_refuses_them(int depth)
    {
        var result = Draw(PageOf(Nested(depth)));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(RasterErrorCodes.InvalidPageModel);
    }

    [Theory]
    [InlineData("clip end without start")]
    [InlineData("clip start without end")]
    [InlineData("clip ends inside an artifact")]
    public void Clips_that_do_not_nest_with_the_other_markers_are_refused(string problem)
    {
        DrawItem[] items = problem switch
        {
            "clip end without start" => [ClipEnd.Instance],
            "clip start without end" => [Clip],
            _ => [Clip, new ArtifactStart(ArtifactKind.Layout), ClipEnd.Instance, ArtifactEnd.Instance],
        };

        Draw(PageOf(items)).Error.Code.Should().Be(RasterErrorCodes.InvalidPageModel);
    }
}
