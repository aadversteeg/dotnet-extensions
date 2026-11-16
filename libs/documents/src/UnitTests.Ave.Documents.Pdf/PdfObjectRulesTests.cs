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
    [InlineData(3.5e38)]
    [InlineData(-1e300)]
    public void A_number_that_is_not_finite_or_beyond_the_real_range_is_an_error_value_wherever_it_is(double value)
    {
        PdfWriter.Write(FileWith(new PdfArray([new PdfArray([new PdfReal(value)])]))).Error.Code.Should().Be(PdfErrorCodes.Write.InvalidNumber);
        PdfSerializer.Serialize(new PdfReal(value)).Error.Code.Should().Be(PdfErrorCodes.Write.InvalidNumber);
        ContentStream.Serialize([ContentOperators.Rectangle(0, 0, value, 1)]).Error.Code.Should().Be(PdfErrorCodes.Write.InvalidNumber);
    }

    [Theory]
    [InlineData(2147483647.0, "2147483647")]
    [InlineData(-2147483648.0, "-2147483648")]
    [InlineData(2147483648.0, "2147483648.0")]
    [InlineData(-2147483649.0, "-2147483649.0")]
    [InlineData(3e9, "3000000000.0")]
    [InlineData(1e20, "100000000000000000000.0")]
    public void A_whole_number_outside_the_integer_range_is_written_as_a_real_with_a_decimal_point(double value, string expected)
    {
        // ISO 32000-1 Annex C, Table C.1: an integer is at most 2^31 - 1 and at least -2^31 (veraPDF rule 6.1.13-1).
        // A reader tells a real from an integer by its decimal point (§7.3.3), so the real must carry one.
        PdfSerializer.Serialize(PdfNumber.From(value)).Value.Should().Be(expected);
        ContentStream.Serialize([ContentOperators.Rectangle(0, 0, value, 1)]).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(5.0, "5.0")]
    [InlineData(0.0, "0.0")]
    [InlineData(0.00001, "0.0")]
    [InlineData(-7.00004, "-7.0")]
    [InlineData(1e20, "100000000000000000000.0")]
    public void A_real_is_never_written_in_the_form_of_an_integer(double value, string expected) =>
        PdfSerializer.Serialize(new PdfReal(value)).Value.Should().Be(expected);

    [Theory]
    [InlineData(-12.0, -12)]
    [InlineData(3.00004, 3)]
    public void A_number_that_is_whole_at_four_decimals_and_in_the_integer_range_is_an_integer(double value, int expected) =>
        PdfNumber.From(value).Should().Be(new PdfInteger(expected));

    [Fact]
    public void The_largest_real_is_written()
    {
        PdfSerializer.Serialize(new PdfReal(PdfSerializer.MaxReal)).IsSuccess.Should().BeTrue();
        PdfSerializer.Serialize(new PdfReal(-PdfSerializer.MaxReal)).IsSuccess.Should().BeTrue();
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
