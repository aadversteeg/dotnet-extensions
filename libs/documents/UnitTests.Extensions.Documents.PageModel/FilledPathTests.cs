using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace UnitTests.Extensions.Documents.PageModel;

/// <summary>
/// The filled path of the page model (<see cref="FillPath"/>): its text form, so golden files hold it, and its part of the
/// contract both back-ends check (<see cref="PageModelRules"/>): at least one contour, each with at least one segment,
/// nothing missing, at most <see cref="PageModelRules.MaxPathSegments"/> segments, a fill rule of its enum, and every point
/// (and its height above the foot of the page) a number PDF can hold.
/// </summary>
public class FilledPathTests
{
    private static readonly ErrorCode Code = DocumentErrorCodes._ / "Test" / "Invalid";

    private static PageDocument Document(params DrawItem[] items) => new(new DocumentMetadata("Paths", null), [new Page(new Size(100, 100), [.. items])]);

    private static Result<PageDocument, Error> Checked(params DrawItem[] items) => PageModelRules.Check(Document(items), Code);

    [Fact]
    public void The_text_form_matches_the_golden_file() =>
        Golden.Compare("filled-path.page.txt", PageModelText.Write(Document(
            new ArtifactStart(ArtifactKind.Layout),
            TestPaths.Disc(31.875, 21.375, 1.875, new Color(255, 0, 0)),
            ArtifactEnd.Instance,
            TestPaths.Ring(61.875, 31.875, 2.25, 1.5),
            TestPaths.Polygon(new Point(10, 20), new Point(14.5, 20), new Point(14.5, 22), new Point(11.5, 22), new Point(11.5, 24), new Point(10, 24))))).Should().BeNull();

    [Fact]
    public void A_disc_a_ring_and_a_polygon_meet_the_contract()
    {
        var result = Checked(TestPaths.Disc(31.875, 21.375, 1.875), TestPaths.Ring(61.875, 31.875, 2.25, 1.5), TestPaths.Polygon(new Point(1, 1), new Point(5, 1), new Point(1, 5)));

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
    }

    public static TheoryData<string, DrawItem, string> Breaches => new()
    {
        { "no contour", new FillPath([], PathFillRule.NonZero, Color.Black), "has no contour" },
        { "a missing contour", new FillPath([null!], PathFillRule.NonZero, Color.Black), "a missing contour or segment" },
        { "a missing segment", new FillPath([new PathContour(new Point(1, 1), [null!])], PathFillRule.NonZero, Color.Black), "a missing contour or segment" },
        { "a contour without a segment", new FillPath([new PathContour(new Point(1, 1), [])], PathFillRule.NonZero, Color.Black), "a contour without a segment" },
        { "a fill rule that is not one", TestPaths.Disc(10, 10, 2) with { Rule = (PathFillRule)2 }, "the fill rule 2" },
        { "a start that is not a number", new FillPath([new PathContour(new Point(double.NaN, 1), [new PathLine(new Point(2, 2))])], PathFillRule.NonZero, Color.Black), "a point that is not a number within" },
        { "a control point out of range", new FillPath([new PathContour(new Point(1, 1), [new PathCubic(new Point(1, 3.5e38), new Point(2, 2), new Point(3, 1))])], PathFillRule.NonZero, Color.Black), "a point that is not a number within" },
    };

    [Theory]
    [MemberData(nameof(Breaches))]
    public void A_filled_path_that_breaks_the_contract_is_refused_under_the_callers_code(string kind, DrawItem item, string message)
    {
        var result = Checked(item);

        result.IsFailure.Should().BeTrue(kind);
        result.Error.Code.Should().Be(Code, kind);
        result.Error.Message.Should().Contain(message, kind);
    }

    [Fact]
    public void A_point_whose_height_above_the_foot_of_the_page_is_out_of_range_is_a_breach()
    {
        // The y is within ±MaxNumber; on a page MaxNumber high, its height above the foot (3.4e38 − (−1.7e38) = 5.1e38),
        // which the PDF back-end writes, is not.
        var path = new FillPath([new PathContour(new Point(1, 1), [new PathLine(new Point(2, -PageModelRules.MaxNumber / 2))])], PathFillRule.NonZero, Color.Black);

        var result = PageModelRules.Check(new Page(new Size(100, PageModelRules.MaxNumber), [path]), Code);

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("height above the foot of the page");
    }

    /// <summary>A path of two contours with <paramref name="segments"/> straight segments in all.</summary>
    private static FillPath WithSegments(int segments)
    {
        var first = segments / 2;
        PathContour Contour(int count, double y) => new(new Point(0, y), [.. Enumerable.Range(1, count).Select(index => (PathSegment)new PathLine(new Point(index * 0.01, y + (index % 2))))]);
        return new FillPath([Contour(first, 10), Contour(segments - first, 50)], PathFillRule.EvenOdd, Color.Black);
    }

    [Fact]
    public void A_filled_path_has_at_most_4096_segments_its_contours_together()
    {
        PageModelRules.MaxPathSegments.Should().Be(4096);
        Checked(WithSegments(PageModelRules.MaxPathSegments)).IsSuccess.Should().BeTrue();

        var result = Checked(WithSegments(PageModelRules.MaxPathSegments + 1));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(Code);
        result.Error.Message.Should().Contain("has 4097 segments; a filled path has at most 4096");
    }
}
