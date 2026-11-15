using Ave.Documents.Core;
using Ave.Documents.Fluent;
using static Ave.Documents.Fluent.Elements;

namespace UnitTests.Ave.Documents.Fluent;

/// <summary>
/// Elements nest at most <see cref="InputLimits.MaxNestingDepth"/> deep. Each level costs layout a call frame, so
/// without a limit a deep enough document ends the process with a stack overflow instead of an error.
/// </summary>
public class NestingDepthTests
{
    private static Element Nested(int depth) =>
        Enumerable.Range(0, depth - 1).Aggregate((Element)Paragraph("deep"), (inner, _) => Block([inner]));

    [Fact]
    public void A_document_at_the_limit_is_accepted() =>
        DocumentTranslator.ToModel(Document([Nested(InputLimits.MaxNestingDepth)]).FontFamily("Lato")).IsSuccess.Should().BeTrue();

    [Theory]
    [InlineData(1)]
    [InlineData(400)]
    [InlineData(5000)]
    public void Deeper_nesting_is_an_input_error_not_a_stack_overflow(int beyond)
    {
        var error = DocumentTranslator.ToModel(Document([Nested(InputLimits.MaxNestingDepth + beyond)]).FontFamily("Lato")).Error;

        error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.TooDeep);
        error.Metadata![ErrorMetadataKeys.Value].Should().Be(InputLimits.MaxNestingDepth.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
