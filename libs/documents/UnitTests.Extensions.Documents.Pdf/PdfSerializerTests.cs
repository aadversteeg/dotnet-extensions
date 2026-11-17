using Ave.Extensions.Documents.Pdf;

namespace UnitTests.Extensions.Documents.Pdf;

/// <summary>Object syntax per ISO 32000-1 §7.3; expected strings follow the examples in that section.</summary>
public class PdfSerializerTests
{
    [Theory]
    [InlineData(1.0, "1")]
    [InlineData(-0.5, "-0.5")]
    [InlineData(0.123456, "0.1235")]
    public void Numbers_have_no_exponent_and_four_decimals_at_most(double value, string expected) =>
        PdfSerializer.Serialize(PdfNumber.From(value)).Value.Should().Be(expected);

    [Theory]
    [InlineData("Type", "/Type")]
    [InlineData("A;Name_With-Various***Characters?", "/A;Name_With-Various***Characters?")]
    [InlineData("Lime Green", "/Lime#20Green")]
    [InlineData("paired()parentheses", "/paired#28#29parentheses")]
    [InlineData("The_Key_of_F#_Minor", "/The_Key_of_F#23_Minor")]
    public void Names_escape_delimiters_and_white_space_as_hex(string name, string expected) =>
        PdfSerializer.Serialize(new PdfName(name)).Value.Should().Be(expected);

    [Fact]
    public void Literal_strings_escape_parentheses_and_backslashes() =>
        PdfSerializer.Serialize(PdfString.FromText(@"a (b) \c")).Value.Should().Be(@"(a \(b\) \\c)");

    [Fact]
    public void Text_outside_ascii_becomes_utf16_big_endian_with_a_byte_order_mark() =>
        PdfSerializer.Serialize(PdfString.FromText("é")).Value.Should().Be(@"(\376\377\000\351)");

    [Fact]
    public void Binary_strings_are_hexadecimal() =>
        PdfSerializer.Serialize(PdfString.FromBytes([0x00, 0x24, 0xFF])).Value.Should().Be("<0024FF>");

    [Fact]
    public void Dictionaries_keep_their_entry_order_and_leave_out_absent_entries()
    {
        var dictionary = PdfDictionary.Of(
            ("Type", new PdfName("Page")),
            ("Optional", null),
            ("Kids", new PdfArray([new PdfReference(3), new PdfBoolean(true), PdfNull.Instance])));

        PdfSerializer.Serialize(dictionary).Value.Should().Be("<< /Type /Page /Kids [3 0 R true null] >>");
    }

    [Fact]
    public void A_duplicate_key_is_an_error_value_when_written()
    {
        var dictionary = PdfDictionary.Of(("A", new PdfInteger(1)), ("A", new PdfInteger(2)));

        PdfSerializer.Serialize(dictionary).Error.Code.Should().Be(PdfErrorCodes.Write.InvalidObject);
    }

    [Fact]
    public void A_non_finite_number_anywhere_in_the_tree_is_an_error()
    {
        var value = new PdfArray([new PdfInteger(1), PdfDictionary.Of(("X", new PdfReal(double.NaN)))]);

        PdfSerializer.Serialize(value).Error.Code.Should().Be(PdfErrorCodes.Write.InvalidNumber);
    }

    [Fact]
    public void Dates_use_the_pdf_date_format()
    {
        PdfSerializer.Serialize(PdfDate.Format(new DateTimeOffset(2026, 11, 15, 9, 22, 5, TimeSpan.FromHours(1)))).Value
            .Should().Be("(D:20261115092205+01'00)");
        PdfSerializer.Serialize(PdfDate.Format(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero))).Value
            .Should().Be("(D:20260102030405Z)");
    }
}
