using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Xunit.Abstractions;
using static UnitTests.Extensions.Documents.Layout.Models;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// A paragraph of many images and no text is measured and laid out in work linear in the images: each image's wrap mode is
/// read from its own box or the paragraph's style, in one pass, never by looking for a text run beside it. Counted: the bytes
/// the measurement and the layout allocate, at two sizes (the form of TextCostTests).
/// </summary>
public class ImageParagraphCostTests(ITestOutputHelper output)
{
    /// <summary>The factor a doubled input may cost at most: twice the work, with room for noise, well below quadratic's four.</summary>
    private const double Linear = 2.5;

    private static readonly Ave.Extensions.Documents.Images.ImageData Picture = TestImages.Load("baseline-rgb.jpg");

    private static Box Images(int count) =>
        new(new NodeId(1), SemanticRole.Paragraph, LayoutAlgorithmId.Inline, BoxStyle.Initial with { Text = Text() with { TextWrapMode = TextWrapMode.NoWrap } },
            [.. Enumerable.Range(0, count).Select(index => (Node)new Box(new NodeId(10 + 2 * index), SemanticRole.Decorative, LayoutAlgorithmId.Image, BoxStyle.Initial,
                [new ImageNode(new NodeId(11 + 2 * index), Picture, 2.Pt(), null, At(0))], At(0)))], At(0));

    private static long Bytes(int count)
    {
        var paragraph = Images(count);
        var environment = LayoutEnvironment.Create(TestFonts.Set);
        var before = GC.GetAllocatedBytesForCurrentThread();
        environment.Measure(paragraph).IsSuccess.Should().BeTrue();
        environment.Layout(paragraph, new ConstraintSpace(100, double.PositiveInfinity, IsAtPageStart: true), null).IsSuccess.Should().BeTrue();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Fact]
    public void A_paragraph_of_many_images_and_no_text_costs_work_linear_in_the_images()
    {
        Bytes(100);
        var (small, large) = (Bytes(2000), Bytes(4000));
        output.WriteLine($"images: 2000 -> {small} bytes; 4000 -> {large} bytes");

        large.Should().BeLessThanOrEqualTo((long)(Linear * small));
    }
}
