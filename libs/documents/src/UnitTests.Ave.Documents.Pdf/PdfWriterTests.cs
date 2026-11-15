using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Ave.Documents.Pdf;

namespace UnitTests.Ave.Documents.Pdf;

public class PdfWriterTests
{
    private static PdfFile MinimalFile(ImmutableArray<byte>? identifier = null) => new(
        [
            new IndirectValue(1, PdfDictionary.Of(("Type", new PdfName("Catalog")), ("Pages", new PdfReference(2)))),
            new IndirectValue(2, PdfDictionary.Of(("Type", new PdfName("Pages")), ("Kids", new PdfArray([new PdfReference(3)])), ("Count", new PdfInteger(1)))),
            new IndirectValue(3, PdfDictionary.Of(("Type", new PdfName("Page")), ("Parent", new PdfReference(2)), ("MediaBox", PdfArray.OfNumbers(0, 0, 200, 100)), ("Contents", new PdfReference(4)))),
            new IndirectStream(4, new PdfStream(PdfDictionary.Of(), [.. "0 0 1 rg 10 10 50 50 re f\n"u8], StreamEncoding.Flate)),
        ],
        Catalog: new PdfReference(1),
        Info: null,
        FileIdentifier: identifier);

    private static string Latin1(ImmutableArray<byte> bytes) => Encoding.Latin1.GetString(bytes.AsSpan());

    [Fact]
    public void Starts_with_the_header_and_a_binary_comment()
    {
        var bytes = PdfWriter.Write(MinimalFile()).Value;

        Latin1(bytes).Should().StartWith("%PDF-1.7\n%");
        bytes.Skip(10).Take(4).Should().OnlyContain(value => value >= 128, "§7.5.2 asks for four bytes of 128 or more");
    }

    [Fact]
    public void Every_cross_reference_entry_points_at_its_object_and_is_20_bytes_long()
    {
        var text = Latin1(PdfWriter.Write(MinimalFile()).Value);

        var startXref = int.Parse(Regex.Match(text, @"startxref\n(\d+)\n%%EOF\n$").Groups[1].Value, CultureInfo.InvariantCulture);
        text[startXref..].Should().StartWith("xref\n0 5\n0000000000 65535 f\r\n");
        var entries = text[(startXref + "xref\n0 5\n".Length)..].Split("trailer")[0];
        entries.Length.Should().Be(5 * 20);
        for (var number = 1; number <= 4; number++)
        {
            var offset = int.Parse(entries.Substring(number * 20, 10), CultureInfo.InvariantCulture);
            text[offset..].Should().StartWith($"{number} 0 obj\n");
        }
    }

    [Fact]
    public void Streams_get_length_and_filter_and_decompress_to_their_data()
    {
        var text = Latin1(PdfWriter.Write(MinimalFile()).Value);

        var match = Regex.Match(text, @"4 0 obj\n<< /Length (\d+) /Filter /FlateDecode >>\nstream\n");
        match.Success.Should().BeTrue();
        var length = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var data = Encoding.Latin1.GetBytes(text.Substring(match.Index + match.Length, length));
        using var zlib = new ZLibStream(new MemoryStream(data), CompressionMode.Decompress);
        using var reader = new StreamReader(zlib);
        reader.ReadToEnd().Should().Be("0 0 1 rg 10 10 50 50 re f\n");
        text.Substring(match.Index + match.Length + length, "\nendstream".Length).Should().Be("\nendstream");
    }

    [Fact]
    public void The_trailer_names_size_root_and_an_identifier_derived_from_the_content()
    {
        var first = Latin1(PdfWriter.Write(MinimalFile()).Value);
        var second = Latin1(PdfWriter.Write(MinimalFile()).Value);

        first.Should().MatchRegex(@"trailer\n<< /Size 5 /Root 1 0 R /ID \[<[0-9A-F]{32}> <[0-9A-F]{32}>\] >>");
        first.Should().Be(second, "the same content gives the same identifier and the same bytes");
    }

    [Fact]
    public void A_given_identifier_is_used_as_is()
    {
        var text = Latin1(PdfWriter.Write(MinimalFile([.. Enumerable.Repeat((byte)0xAB, 16)])).Value);

        text.Should().Contain("/ID [<ABABABABABABABABABABABABABABABAB> <ABABABABABABABABABABABABABABABAB>]");
    }

    [Fact]
    public void Object_numbers_with_a_gap_are_an_error()
    {
        var file = MinimalFile() with { Objects = MinimalFile().Objects.RemoveAt(1) };

        PdfWriter.Write(file).Error.Code.Should().Be(PdfErrorCodes.Write.InvalidObjectNumbers);
    }

    [Fact]
    public void A_non_finite_number_is_an_error()
    {
        var file = MinimalFile() with { Objects = MinimalFile().Objects.SetItem(2, new IndirectValue(3, new PdfReal(double.PositiveInfinity))) };

        PdfWriter.Write(file).Error.Code.Should().Be(PdfErrorCodes.Write.InvalidNumber);
    }

    [Fact]
    public void Content_streams_put_one_operation_per_line_operands_first()
    {
        var bytes = ContentStream.Serialize(
        [
            ContentOperators.SaveState(),
            ContentOperators.SetFillRgb(1, 0.5, 0),
            ContentOperators.Rectangle(10, 20, 30, 40),
            ContentOperators.Fill(),
            ContentOperators.RestoreState(),
        ]).Value;

        Encoding.ASCII.GetString(bytes.AsSpan()).Should().Be("q\n1 0.5 0 rg\n10 20 30 40 re\nf\nQ\n");
    }

    [Fact]
    public void Content_stream_with_a_non_finite_operand_is_an_error() =>
        ContentStream.Serialize([ContentOperators.MoveTextTo(double.NaN, 0)]).Error.Code
            .Should().Be(PdfErrorCodes.Write.InvalidNumber);

    [Fact]
    public void Without_a_given_identifier_the_ID_is_the_first_16_bytes_of_SHA_256_of_the_file_body()
    {
        // The file identifier (ISO 32000-1 §14.4) defaults to a hash of the content, so the same content gives the same
        // identifier and other content another. The body is everything before the cross-reference table.
        var bytes = PdfWriter.Write(MinimalFile()).Value;
        var text = Latin1(bytes);
        var crossReference = int.Parse(Regex.Match(text, @"startxref\s+(\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
        var body = bytes.AsSpan(0, crossReference);
        var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(body).AsSpan(0, 16));

        var id = Regex.Match(text, @"/ID \[<([0-9A-Fa-f]+)> <([0-9A-Fa-f]+)>\]");

        id.Success.Should().BeTrue();
        id.Groups[1].Value.Should().BeEquivalentTo(expected);
        id.Groups[2].Value.Should().BeEquivalentTo(expected);
    }
}
