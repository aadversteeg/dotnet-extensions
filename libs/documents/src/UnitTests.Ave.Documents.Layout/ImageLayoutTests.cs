using Ave.Documents.Core;
using Ave.Documents.DocumentModel;
using Ave.Documents.Layout;
using Ave.Documents.PageModel;
using Ave.Documents.TestSupport;
using static UnitTests.Ave.Documents.Layout.Models;

namespace UnitTests.Ave.Documents.Layout;

/// <summary>Images as replaced elements: used size from width, height and the natural aspect ratio; monolithic.</summary>
public class ImageLayoutTests
{
    /// <summary>baseline-rgb.jpg: 40 × 30 pixels = 30 × 22.5 pt (1 px = 0.75 pt), aspect ratio 4:3.</summary>
    private static Box Image(int id, Length? width = null, Length? height = null, BoxStyle? style = null) =>
        new(new NodeId(id), SemanticRole.Figure, LayoutAlgorithmId.Image, style ?? BoxStyle.Initial,
            [new ImageNode(new NodeId(id + 1), TestImages.Load("baseline-rgb.jpg"), width, height, At(0))], At(0));

    private static Size UsedSize(Box image) =>
        ((BoxFragment)((BoxFragment)Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), Document(image)).Value[0].Root.Fragment).Children[0].Fragment)
            .Children[0].Fragment.Size;

    [Fact]
    public void Without_a_size_an_image_shows_at_its_natural_size_in_CSS_pixels() =>
        UsedSize(Image(1)).Should().Be(new Size(30, 22.5));

    [Fact]
    public void A_width_alone_keeps_the_aspect_ratio() => UsedSize(Image(1, width: 60.Pt())).Should().Be(new Size(60, 45));

    [Fact]
    public void A_height_alone_keeps_the_aspect_ratio() => UsedSize(Image(1, height: 45.Pt())).Should().Be(new Size(60, 45));

    [Fact]
    public void Width_and_height_together_fit_the_image_inside_both_keeping_the_ratio()
    {
        UsedSize(Image(1, width: 60.Pt(), height: 30.Pt())).Should().Be(new Size(40, 30), "the height limits");
        UsedSize(Image(1, width: 40.Pt(), height: 60.Pt())).Should().Be(new Size(40, 30), "the width limits");
    }

    [Fact]
    public void A_natural_size_wider_than_the_box_is_scaled_down_to_the_box()
    {
        var narrow = BoxStyle.Initial with { Margin = new Edges(0.Pt(), 165.Pt(), 0.Pt(), 0.Pt()) };

        UsedSize(Image(1, style: narrow)).Should().Be(new Size(15, 11.25));
    }

    [Fact]
    public void An_image_never_breaks_it_moves_to_the_next_page_whole()
    {
        var narrow = BoxStyle.Initial with { Margin = new Edges(0.Pt(), 160.Pt(), 0.Pt(), 0.Pt()) };
        var pages = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), Document(Paragraph(1, Words(6), narrow), Image(10, width: 40.Pt()))).Value;

        pages.Should().HaveCount(2, "six lines leave 8 pt; the 30 pt image goes to page 2 whole");
        ((BoxFragment)pages[1].Root.Fragment).Children.Single().Fragment.Size.Height.Should().Be(30);
    }

    [Fact]
    public void An_image_taller_than_an_empty_page_does_not_fit()
    {
        var error = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), Document(Image(10, height: 100.Pt()))).Error;

        error.Code.Should().Be(DocumentErrorCodes.Layout.DoesNotFit);
    }

    [Fact]
    public void The_painter_draws_the_image_into_its_used_rectangle()
    {
        var pages = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), Document(Image(10, width: 60.Pt()))).Value;

        var draw = PagePainter.Paint(new DocumentMetadata(null, null), pages).Pages[0].Items.OfType<DrawImage>().Single();

        draw.Area.Should().Be(new Rect(new Point(10, 10), new Size(60, 45)));
        draw.Image.Should().Be(TestImages.Load("baseline-rgb.jpg"));
    }
}
