using Ave.Documents.Core;
using Ave.Extensions.ErrorPaths;

namespace UnitTests.Ave.Documents.Core;

public class ErrorSourceExtensionsTests
{
    private static readonly SourceLocation Location = new(@"C:\src\Letter.cs", 42);

    [Fact]
    public void WithSource_adds_file_and_line()
    {
        var error = new Error(DocumentErrorCodes.Layout.DoesNotFit, "too big").WithSource(Location);

        error.Metadata![ErrorMetadataKeys.SourceFile].Should().Be(@"C:\src\Letter.cs");
        error.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(42);
    }

    [Fact]
    public void WithSource_on_combined_errors_adds_the_location_to_every_part()
    {
        var combined = ResultCollections.Combine(
            [new Error(DocumentErrorCodes.Font.GlyphMissing, "a"), new Error(DocumentErrorCodes.Font.GlyphMissing, "b")]);

        var located = combined.WithSource(Location);

        ResultCollections.Parts(located).Should().AllSatisfy(part => part.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(42));
    }

    [Fact]
    public void Source_location_text_shows_only_the_file_name_so_output_is_machine_independent() =>
        new SourceLocation("/home/someone/src/Letter.cs", 7).ToString().Should().Be("Letter.cs:7");
}
