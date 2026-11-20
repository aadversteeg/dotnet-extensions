using System.Runtime.CompilerServices;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// <see cref="Elements.InlineImage"/>: the image in a line is made and checked as any image, becomes the image's box among
/// the paragraph's runs, and its span takes no formatting. In each test the offending call is on the line right after
/// <c>CurrentLine()</c>.
/// </summary>
public class InlineImageInputTests
{
    private static readonly byte[] Logo = TestImages.Bytes("rgb8.png");

    private static Result<StyledDocument, Error> Model(params Span[] spans) =>
        DocumentTranslator.ToModel(Document([Paragraph([.. spans]), Paragraph("after")], title: "Title", language: "en").FontFamily("Lato"));

    private static IEnumerable<Node> PreOrder(Node node) => node is Box box ? [box, .. box.Children.SelectMany(PreOrder)] : [node];

    [Fact]
    public void An_image_span_becomes_the_image_s_box_among_the_runs_with_ids_in_document_order()
    {
        var model = Model(Text("ab "), InlineImage(Image(Logo).Alt("logo").Margin(2.Pt())), Text(" cd")).Value;

        var paragraph = (Box)model.Root.Children[0];
        paragraph.Children.Select(child => child.GetType().Name).Should().Equal("TextRun", "Box", "TextRun");
        var image = (Box)paragraph.Children[1];
        (image.Layout, image.Role, image.Style.Margin).Should().Be((LayoutAlgorithmId.Image, SemanticRole.Figure("logo"), Edges.All(2.Pt())));
        image.Children.Should().ContainSingle().Which.Should().BeOfType<ImageNode>();
        PreOrder(model.Root).Select(node => node.Id.Value).Should().Equal(Enumerable.Range(0, PreOrder(model.Root).Count()), "every node has its pre-order position as its id");
    }

    [Fact]
    public void An_image_in_a_line_gets_the_checks_of_any_image()
    {
        var line = CurrentLine() + 1;
        var image = Image(Logo).Alt("logo").Decorative();

        var error = Model(Text("ab "), InlineImage(image)).Error;

        error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.ImageDescription);
        error.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(line);
    }

    [Fact]
    public void Formatting_on_the_span_of_an_image_is_refused_at_the_span()
    {
        var line = CurrentLine() + 1;
        var span = InlineImage(Image(Logo).Alt("logo")).Bold();

        var error = Model(Text("ab "), span).Error;

        error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.NotApplicable);
        error.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(line);
        error.Metadata![ErrorMetadataKeys.SourceFile].Should().Be(ThisFile());
    }

    [Fact]
    public void A_page_break_modifier_on_an_image_in_a_line_is_refused_when_the_document_is_laid_out()
    {
        var model = Model(Text("ab "), InlineImage(Image(Logo).Alt("logo").KeepWithNext())).Value;

        DocumentLayout.Run(model, TestFonts.Set).Error.Code.Should().Be(DocumentErrorCodes.Input.Unsupported.AtomicInline);
    }

    private static int CurrentLine([CallerLineNumber] int line = 0) => line;

    private static string ThisFile([CallerFilePath] string file = "") => file;
}
