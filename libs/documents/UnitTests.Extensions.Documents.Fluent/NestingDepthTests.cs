using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fluent;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// Elements nest at most <see cref="InputLimits.MaxNestingDepth"/> deep. Each level costs layout a call frame, so
/// without a limit a deep enough document ends the process with a stack overflow instead of an error.
/// </summary>
public class NestingDepthTests
{
    private static Element Nested(int depth) =>
        Enumerable.Range(0, depth - 1).Aggregate((Element)Paragraph("deep"), (inner, _) => Block([inner]));

    [Fact]
    public void The_limit_is_64_levels()
    {
        // The number itself, not the constant: a test that reads the constant moves with it.
        DocumentTranslator.ToModel(Document([Nested(64)]).FontFamily("Lato")).IsSuccess.Should().BeTrue();
        DocumentTranslator.ToModel(Document([Nested(65)]).FontFamily("Lato")).Error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.TooDeep);
    }

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

    [Fact]
    public void Tables_nested_in_cells_count_as_levels()
    {
        // Each table puts its cells' content one level deeper, like a block: a table in a cell in a table, and so
        // on, runs as deep in layout as blocks in blocks.
        var tables = Enumerable.Range(0, InputLimits.MaxNestingDepth).Aggregate((Element)Paragraph("deep"),
            (inner, _) => Table(Columns(Fill(1)), [TableRow([Cell([inner])])]));

        DocumentTranslator.ToModel(Document([tables]).FontFamily("Lato")).Error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.TooDeep);
    }
}
