using Ave.Extensions.Documents.Core;
using Ave.Extensions.ErrorPaths;

namespace UnitTests.Extensions.Documents.Core;

public class ErrorSourceExtensionsTests
{
    private static readonly SourceLocation Location = new(@"C:\src\Letter.cs", 42);

    [Fact]
    public void WithSource_adds_file_and_line()
    {
        var error = new Error(DocumentErrorCodes.Input.Invalid.Length, "too big").WithSource(Location);

        error.Metadata![ErrorMetadataKeys.SourceFile].Should().Be(@"C:\src\Letter.cs");
        error.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(42);
    }

    [Fact]
    public void WithSource_on_combined_errors_adds_the_location_to_every_part()
    {
        var combined = Errors.Aggregate(
            [new Error(DocumentErrorCodes.Input.Invalid.Text, "a"), new Error(DocumentErrorCodes.Input.Invalid.Text, "b")]);

        var located = combined.WithSource(Location);

        located.Parts().Should().AllSatisfy(part => part.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(42));
    }

    [Fact]
    public void WithSource_of_a_text_position_adds_name_line_and_column()
    {
        var error = new Error(DocumentErrorCodes.Input.Invalid.Length, "too big").WithSource("invoice.html", new TextPosition(3, 17));

        error.Metadata![ErrorMetadataKeys.SourceFile].Should().Be("invoice.html");
        error.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(3);
        error.Metadata![ErrorMetadataKeys.SourceColumn].Should().Be(17);
    }

    [Fact]
    public void WithSource_of_a_text_position_on_combined_errors_adds_it_to_every_part()
    {
        var combined = Errors.Aggregate(
            [new Error(DocumentErrorCodes.Input.Invalid.Text, "a"), new Error(DocumentErrorCodes.Input.Invalid.Text, "b")]);

        var located = combined.WithSource("style.css", new TextPosition(9, 2));

        located.Parts().Should().AllSatisfy(part => part.Metadata![ErrorMetadataKeys.SourceColumn].Should().Be(2));
    }

    [Fact]
    public void Text_position_text_is_line_colon_column() =>
        new TextPosition(12, 5).ToString().Should().Be("12:5");

    [Fact]
    public void Source_location_text_shows_only_the_file_name_so_output_is_machine_independent() =>
        new SourceLocation("/home/someone/src/Letter.cs", 7).ToString().Should().Be("Letter.cs:7");
}
