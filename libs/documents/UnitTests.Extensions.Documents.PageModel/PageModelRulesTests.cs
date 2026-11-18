using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;

namespace UnitTests.Extensions.Documents.PageModel;

/// <summary>
/// The page-model contract shared by the back-ends (<see cref="PageModelRules"/>): what is not tested through the PDF
/// back-end's contract tests (UnitTests.Extensions.Documents.Render.Pdf/PageModelContractTests.cs) — the error code is the
/// caller's, positions and sizes must be finite, and one page can be checked on its own.
/// </summary>
public class PageModelRulesTests
{
    private static readonly ErrorCode Code = DocumentErrorCodes._ / "Test" / "Invalid";

    private static readonly DocumentMetadata Metadata = new("Title", null);

    private static GlyphRun Run(Point origin, double x = 0, double y = 0) =>
        new(TestFonts.Regular, 10, Color.Black, origin, [new PositionedGlyph(TestFonts.Regular.GlyphFor(new System.Text.Rune('A')).Value, x, "A", y)]);

    private static Page PageWith(params DrawItem[] items) => new(new Size(100, 100), [.. items]);

    public static TheoryData<DrawItem> ItemsThatAreNotFinite =>
    [
        Run(new Point(double.NaN, 10)),
        Run(new Point(10, double.PositiveInfinity)),
        Run(new Point(10, 10), x: double.NegativeInfinity),
        Run(new Point(10, 10), y: double.NaN),
        new FillRectangle(new Rect(new Point(0, double.NaN), new Size(5, 5)), Color.Black),
        new FillRectangle(new Rect(new Point(0, 0), new Size(double.PositiveInfinity, 5)), Color.Black),
        new DrawImage(new Rect(new Point(0, 0), new Size(5, double.NaN)), TestImages.Load("rgb8.png")),
    ];

    [Theory]
    [MemberData(nameof(ItemsThatAreNotFinite))]
    public void A_position_or_size_that_is_not_finite_is_a_breach(DrawItem item)
    {
        var result = PageModelRules.Check(new PageDocument(Metadata, [PageWith(item)]), Code);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(Code, "the breach is reported under the caller's code");
        result.Error.Message.Should().Contain("not a number within");
    }

    [Theory]
    [InlineData(PageModelRules.MaxNumber, true)]
    [InlineData(-PageModelRules.MaxNumber, true)]
    [InlineData(3.4031e38, false)]
    [InlineData(-1.5e308, false)]
    [InlineData(double.MaxValue, false)]
    public void Every_number_is_within_the_range_of_a_PDF_real(double value, bool allowed)
    {
        // ISO 32000-1 Annex C, Table C.1: a real is within ±3.403 × 10^38.
        var image = TestImages.Load("rgb8.png");
        var pages = new[]
        {
            PageWith(Run(new Point(value, 10))),
            PageWith(Run(new Point(10, 10), x: value)),
            PageWith(Run(new Point(10, 10), y: value)),
            PageWith(new GlyphRun(TestFonts.Regular, Math.Abs(value), Color.Black, new Point(1, 1), [])),
            PageWith(new FillRectangle(new Rect(new Point(value, 0), new Size(1, 1)), Color.Black)),
            PageWith(new FillRectangle(new Rect(new Point(0, 0), new Size(1, value)), Color.Black)),
            PageWith(new DrawImage(new Rect(new Point(0, value), new Size(1, 1)), image)),
            new Page(new Size(Math.Abs(value), 100), []),
        };

        foreach (var page in pages)
        {
            PageModelRules.Check(page, Code).IsSuccess.Should().Be(allowed, $"{value} in {page.Items.FirstOrDefault()?.GetType().Name ?? "the page size"}");
        }
    }

    [Fact]
    public void Finite_items_pass()
    {
        var page = PageWith(
            Run(new Point(10, 20), x: 3.5, y: -1),
            new FillRectangle(new Rect(new Point(-5, 1e6), new Size(-3, 0)), Color.Black),
            new DrawImage(new Rect(new Point(0, 0), new Size(5, 5)), TestImages.Load("rgb8.png")));

        PageModelRules.Check(new PageDocument(Metadata, [page]), Code).IsSuccess.Should().BeTrue();
        PageModelRules.Check(page, Code).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void One_page_is_checked_by_the_rules_of_one_page()
    {
        var start = new StructureStart(new NodeId(1), SemanticRole.Paragraph, new SourceLocation("x.cs", 1));
        var first = PageWith(start, new StructureEnd(new NodeId(1)));
        var second = PageWith(start with { Role = SemanticRole.Heading(1) }, new StructureEnd(new NodeId(1)));

        // One node with two roles is a breach across pages; each page on its own is fine.
        PageModelRules.Check(new PageDocument(Metadata, [first, second]), Code).IsFailure.Should().BeTrue();
        PageModelRules.Check(first, Code).IsSuccess.Should().BeTrue();
        PageModelRules.Check(second, Code).IsSuccess.Should().BeTrue();

        // A rule of one page is checked.
        var bad = PageModelRules.Check(new Page(new Size(0, 10), []), Code);
        bad.IsFailure.Should().BeTrue();
        bad.Error.Message.Should().Contain("Page 1");
    }
}
