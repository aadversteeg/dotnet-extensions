using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Html.Boxes;

/// <summary>
/// The engine's side of the comparison of curves: <see cref="PrintedPages.Engine"/> writes a filled path of the page
/// model as the area it paints (<see cref="PathSampler"/>), by the sampling rule of the read-back of Chromium's print
/// (<c>tools/reference/chromium/paths.py</c>). The two samplers agree on the same shape: each test here pins the line
/// the script writes for it (<c>paths.coverage</c> on the same contours; the same numbers are pinned on the script's side
/// in <c>paths_test.py</c>, <c>SamplerAgreementTest</c>).
/// </summary>
public class PathSamplerTests
{
    private static PrintedPath Sampled(FillPath path, Rect? clip = null) => PathSampler.Sample(1, path, clip)!;

    private static string Line(PrintedPath path) =>
        FormattableString.Invariant($"{path.X0:0.000} {path.Y0:0.000} {path.X1:0.000} {path.Y1:0.000} {string.Join('/', path.Rows)}");

    private static readonly FillPath L = TestPaths.Polygon(new Point(10.1, 20.2), new Point(14.3, 20.2), new Point(14.3, 21.9), new Point(11.6, 21.9), new Point(11.6, 24.05), new Point(10.1, 24.05));

    [Fact]
    public void A_disc_paints_what_the_script_reads_for_it()
    {
        // The disc of a list marker at 14 px Lato: Chromium's own disc reads back as the same line.
        Line(Sampled(TestPaths.Disc(31.875, 201.375, 1.875))).Should().Be("30.000 199.500 33.750 203.250 2cfc2/cfffc/fffff/cfffc/2cfc2");
    }

    [Fact]
    public void A_ring_1_px_wide_paints_what_the_script_reads_for_it()
    {
        // The circle marker as a ring, radii 1.875 ± 0.375 pt, even-odd: Chromium's stroked circle reads back as the same line.
        Line(Sampled(TestPaths.Ring(61.875, 31.875, 2.25, 1.5))).Should().Be("59.250 29.250 64.500 34.500 0057500/0ac8ca0/5c000c5/7800087/5c000c5/0ac8ca0/0057500");
    }

    [Fact]
    public void A_ring_under_the_non_zero_rule_with_both_circles_one_way_round_is_a_disc()
    {
        Line(Sampled(TestPaths.Ring(61.875, 31.875, 2.25, 1.5, PathFillRule.NonZero))).Should().Be("59.250 29.250 64.500 34.500 0057500/0afffa0/5fffff5/7fffff7/5fffff5/0afffa0/0057500");
    }

    [Fact]
    public void A_square_cornered_path_paints_what_the_script_reads_for_it()
    {
        Line(Sampled(L)).Should().Be("9.750 19.500 15.000 24.750 1111110/8fffff1/8fffff1/8f93330/8f70000/8f70000/1100000");
    }

    [Fact]
    public void A_square_cornered_path_inside_a_clip_paints_what_the_script_reads_for_it()
    {
        Line(Sampled(L, new Rect(new Point(0, 0), new Size(13, 23)))).Should().Be("9.750 19.500 13.500 23.250 11110/8fff5/8fff5/8f931/5a500");
    }

    [Fact]
    public void A_path_the_clip_leaves_nothing_of_paints_nothing()
    {
        PathSampler.Sample(1, L, new Rect(new Point(0, 0), new Size(5, 5))).Should().BeNull();
    }

    private static PageDocument PageWith(params DrawItem[] items) => new(new DocumentMetadata(null, null), [new Page(new Size(300, 420), [.. items])]);

    private const string Chromium = "page 1 300.000 420.000\npath 1 30.000 199.500 33.750 203.250 #ff0000 2cfc2/cfffc/fffff/cfffc/2cfc2\n";

    [Fact]
    public void The_engine_writes_its_filled_paths_with_their_colour_and_the_comparison_finds_them_equal()
    {
        var engine = PrintedPages.Engine(PageWith(TestPaths.Disc(31.875, 201.375, 1.875, new Color(255, 0, 0))));

        engine.Pages[0].Paths.Should().ContainSingle().Which.Colour.Should().Be("#ff0000");
        HtmlCases.Difference(engine, PrintedPages.Parse(Chromium)).Should().BeNull();
    }

    [Fact]
    public void A_filled_path_in_another_place_or_left_out_is_a_difference()
    {
        HtmlCases.Difference(PrintedPages.Engine(PageWith(TestPaths.Disc(32.25, 201.375, 1.875, new Color(255, 0, 0)))), PrintedPages.Parse(Chromium))
            .Should().StartWith("page 1 path at (").And.EndWith("2cfc2/cfffc/fffff/cfffc/2cfc2 #ff0000 (print.txt)");
        HtmlCases.Difference(PrintedPages.Engine(PageWith()), PrintedPages.Parse(Chromium)).Should().Be("page 1 has 0 painted paths, Chromium 1 (print.txt)");
    }

    [Fact]
    public void The_engine_samples_a_filled_path_inside_the_clips_around_it()
    {
        var clipped = PrintedPages.Engine(PageWith(new ClipStart(new Rect(new Point(0, 0), new Size(13, 23))), L, ClipEnd.Instance));

        Line(clipped.Pages[0].Paths.Should().ContainSingle().Subject).Should().Be("9.750 19.500 13.500 23.250 11110/8fff5/8fff5/8f931/5a500");
        PrintedPages.Engine(PageWith(new ClipStart(new Rect(new Point(0, 0), new Size(5, 5))), L, ClipEnd.Instance)).Pages[0].Paths.Should().BeEmpty();
    }
}
