using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using static UnitTests.Extensions.Documents.Layout.Models;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>The model's own check of a run's <c>text-decoration-skip-ink</c>, whichever front-end made it, and its initial value.</summary>
public class SkipInkModelTests
{
    [Fact]
    public void The_initial_value_is_auto_as_in_CSS() =>
        Text().DecorationSkipInk.Should().Be(TextDecorationSkipInk.Auto, "CSS Text Decoration 4 §2.10.4: initial auto");

    [Fact]
    public void A_skip_ink_value_outside_its_enum_is_refused()
    {
        var paragraph = new Box(new NodeId(1), SemanticRole.Paragraph, LayoutAlgorithmId.Inline, BoxStyle.Initial,
            [new TextRun(new NodeId(2), "a", Text() with { DecorationSkipInk = (TextDecorationSkipInk)9 }, At(0))], At(0));
        var result = DocumentLayout.Run(Document(paragraph), TestFonts.Set);

        result.IsFailure.Should().BeTrue("the model holds a run the layout must refuse");
        result.Error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.TextDecoration);
        result.Error.Metadata![ErrorMetadataKeys.Property].Should().Be("text-decoration-skip-ink");
    }
}
