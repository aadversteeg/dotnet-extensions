using Ave.Documents.Pdf;

namespace UnitTests.Ave.Documents.Pdf;

/// <summary>
/// One rule for bad input in the PDF library: the writer checks the objects it is given and answers with an error
/// value — a number that is not finite, two entries with one key, a stream dictionary that sets what the writer
/// sets. Objects are values: two equal arrays, strings or dictionaries are equal.
/// </summary>
public class PdfObjectRulesTests
{
    private static PdfFile FileWith(PdfObject catalogEntry, PdfStream? stream = null) => new(
        [
            new IndirectValue(1, PdfDictionary.Of(("Type", new PdfName("Catalog")), ("Pages", new PdfReference(2)), ("Extra", catalogEntry))),
            new IndirectValue(2, PdfDictionary.Of(("Type", new PdfName("Pages")), ("Kids", new PdfArray([])), ("Count", new PdfInteger(0)))),
            .. stream is null ? [] : new IndirectObject[] { new IndirectStream(3, stream) },
        ],
        Catalog: new PdfReference(1),
        Info: null,
        FileIdentifier: null);

    [Fact]
    public void Two_entries_with_one_key_are_an_error_value()
    {
        var duplicate = new PdfDictionary([KeyValuePair.Create(new PdfName("A"), (PdfObject)new PdfInteger(1)), KeyValuePair.Create(new PdfName("A"), (PdfObject)new PdfInteger(2))]);

        var result = PdfWriter.Write(FileWith(duplicate));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(PdfErrorCodes.Write.InvalidObject);
    }

    [Theory]
    [InlineData("Length")]
    [InlineData("Filter")]
    public void A_stream_dictionary_that_sets_what_the_writer_sets_is_an_error_value(string key)
    {
        var stream = new PdfStream(PdfDictionary.Of((key, new PdfInteger(3))), [.. "abc"u8], StreamEncoding.None);

        var result = PdfWriter.Write(FileWith(new PdfInteger(0), stream));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(PdfErrorCodes.Write.InvalidObject);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_number_that_is_not_finite_is_an_error_value_wherever_it_is(double value)
    {
        PdfWriter.Write(FileWith(new PdfArray([new PdfArray([new PdfReal(value)])]))).Error.Code.Should().Be(PdfErrorCodes.Write.InvalidNumber);
        PdfSerializer.Serialize(new PdfReal(value)).Error.Code.Should().Be(PdfErrorCodes.Write.InvalidNumber);
        ContentStream.Serialize([ContentOperators.Rectangle(0, 0, value, 1)]).Error.Code.Should().Be(PdfErrorCodes.Write.InvalidNumber);
    }

    [Fact]
    public void Arrays_strings_and_dictionaries_with_equal_contents_are_equal()
    {
        new PdfArray([new PdfInteger(1), new PdfName("A")]).Should().Be(new PdfArray([new PdfInteger(1), new PdfName("A")]));
        PdfString.FromText("Title").Should().Be(PdfString.FromText("Title"));
        PdfDictionary.Of(("A", new PdfInteger(1))).Should().Be(PdfDictionary.Of(("A", new PdfInteger(1))));
        PdfDictionary.Of(("A", new PdfInteger(1))).GetHashCode().Should().Be(PdfDictionary.Of(("A", new PdfInteger(1))).GetHashCode());
    }
}
