using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using static UnitTests.Extensions.Documents.Layout.Models;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>The model's own checks of the white-space properties of a run, whichever front-end made it.</summary>
public class WhiteSpaceModelTests
{
    private static Error Refused(TextStyle style, string text = "a")
    {
        var paragraph = new Box(new NodeId(1), SemanticRole.Paragraph, LayoutAlgorithmId.Inline, BoxStyle.Initial,
            [new TextRun(new NodeId(2), text, style, At(0))], At(0));
        var result = DocumentLayout.Run(Document(paragraph), TestFonts.Set);
        result.IsFailure.Should().BeTrue("the model holds a run the layout must refuse");
        return result.Error;
    }

    [Fact]
    public void A_white_space_collapse_value_outside_its_enum_is_refused()
    {
        var error = Refused(Text() with { WhiteSpaceCollapse = (WhiteSpaceCollapse)9 });

        error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.WhiteSpace);
        error.Metadata![ErrorMetadataKeys.Property].Should().Be("white-space-collapse");
    }

    [Fact]
    public void A_text_wrap_mode_value_outside_its_enum_is_refused()
    {
        var error = Refused(Text() with { TextWrapMode = (TextWrapMode)9 });

        error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.WhiteSpace);
        error.Metadata![ErrorMetadataKeys.Property].Should().Be("text-wrap-mode");
    }

    [Theory]
    [InlineData(WhiteSpaceCollapse.Preserve)]
    [InlineData(WhiteSpaceCollapse.BreakSpaces)]
    public void A_tab_in_a_run_whose_spaces_are_preserved_is_refused(WhiteSpaceCollapse mode)
    {
        var error = Refused(Text() with { WhiteSpaceCollapse = mode }, "ab\tc");

        error.Code.Should().Be(DocumentErrorCodes.Input.Unsupported.PreservedTab);
        error.Metadata![ErrorMetadataKeys.Offset].Should().Be(2);
    }
}
