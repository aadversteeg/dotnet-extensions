using Ave.Extensions.Documents.Pdf;

namespace UnitTests.Extensions.Documents.Pdf;

/// <summary>
/// Every PDF value type compares by value, as records promise: two streams or two content operations built from the
/// same parts are equal and have the same hash code, wherever their arrays came from.
/// </summary>
public class ValueEqualityTests
{
    [Fact]
    public void Streams_with_the_same_dictionary_data_and_encoding_are_equal()
    {
        var first = new PdfStream(PdfDictionary.Of(("Type", new PdfName("XObject"))), [1, 2, 3], StreamEncoding.Flate);
        var second = new PdfStream(PdfDictionary.Of(("Type", new PdfName("XObject"))), [1, 2, 3], StreamEncoding.Flate);

        first.Should().Be(second);
        first.GetHashCode().Should().Be(second.GetHashCode());
        first.Should().NotBe(second with { Data = [1, 2, 4] });
    }

    [Fact]
    public void Content_operations_with_the_same_operator_and_operands_are_equal()
    {
        var first = ContentOperators.SetFillRgb(0.5, 0.25, 1);
        var second = ContentOperators.SetFillRgb(0.5, 0.25, 1);

        first.Should().Be(second);
        first.GetHashCode().Should().Be(second.GetHashCode());
        first.Should().NotBe(ContentOperators.SetFillRgb(0.5, 0.25, 0.75));
    }

    [Fact]
    public void Files_with_the_same_objects_catalog_and_identifier_are_equal()
    {
        PdfFile Build() => new(
            [.. new[] { new IndirectValue(1, PdfDictionary.Of(("Type", new PdfName("Catalog")))) }],
            new PdfReference(1),
            null,
            [.. new byte[] { 1, 2, 3 }]);

        Build().Should().Be(Build());
        Build().GetHashCode().Should().Be(Build().GetHashCode());
        Build().Should().NotBe(Build() with { FileIdentifier = [.. new byte[] { 1, 2, 4 }] });
    }
}
