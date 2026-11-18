using System.Text;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace UnitTests.Extensions.Documents.Html.Parsing;

/// <summary>The byte input: the byte order mark, UTF-8 by default, U+FFFD for bad bytes, and a meta that names another
/// encoding refused, in the prescan and in the head.</summary>
public class EncodingTests
{
    private static Result<HtmlParseResult, Error> Parse(byte[] bytes) => HtmlParser.Parse(bytes, new HtmlParseOptions("bytes.html"));

    private static string BodyText(Result<HtmlParseResult, Error> result) =>
        result.Match(
            parsed => string.Concat(parsed.Document.DocumentElement.Children.OfType<HtmlElement>().Single(element => element.IsHtml("body")).Children.OfType<HtmlText>().Select(text => text.Data)),
            error => throw new InvalidOperationException(error.ToString()));

    private static Error Failed(Result<HtmlParseResult, Error> result) =>
        result.Match(value => throw new InvalidOperationException($"Expected a failure, got {value}."), error => error);

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    [Fact]
    public void Bytes_without_a_byte_order_mark_are_utf8() =>
        BodyText(Parse(Utf8("<body>café €"))).Should().Be("café €");

    [Fact]
    public void A_utf8_byte_order_mark_is_removed() =>
        BodyText(Parse([0xEF, 0xBB, 0xBF, .. Utf8("<body>x")])).Should().Be("x");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_utf16_byte_order_mark_chooses_utf16(bool bigEndian)
    {
        var encoding = new UnicodeEncoding(bigEndian, byteOrderMark: true);
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes("<body>é\U0001F600")).ToArray();

        BodyText(Parse(bytes)).Should().Be("é\U0001F600");
    }

    [Fact]
    public void Bytes_that_are_not_utf8_become_replacement_characters_with_a_diagnostic()
    {
        var result = Parse([.. Utf8("<body>\na"), 0xC3, 0x28, .. Utf8("b")]);

        BodyText(result).Should().Be("\na�(b");
        var diagnostic = result.Match(parsed => parsed.Diagnostics, _ => []).Single(error => (string)error.Metadata![ErrorMetadataKeys.Value] == "invalid-byte-sequence");
        diagnostic.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(2);
        diagnostic.Metadata![ErrorMetadataKeys.SourceColumn].Should().Be(2);
    }

    [Theory]
    [InlineData("<meta charset=windows-1252>", "windows-1252")]
    [InlineData("<meta charset=\"ISO-8859-1\">", "iso-8859-1")] // the prescan lowercases what it reads (§13.2.3.2)
    [InlineData("<meta http-equiv=Content-Type content='text/html; charset=shift_jis'>", "shift_jis")]
    public void A_meta_that_names_another_encoding_is_refused(string meta, string label)
    {
        var error = Failed(Parse(Utf8("<!DOCTYPE html>\n<head>" + meta + "</head><body>x")));

        error.Code.Should().Be(HtmlErrorCodes.Encoding);
        error.Metadata![ErrorMetadataKeys.Value].Should().Be(label);
        error.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(2);
        error.Metadata![ErrorMetadataKeys.SourceColumn].Should().Be(7);
    }

    [Theory]
    [InlineData("<meta charset=utf-8>")]
    [InlineData("<meta charset=UTF8>")]
    [InlineData("<meta charset=utf-16>")]
    [InlineData("<meta charset=no-such-encoding>")]
    [InlineData("<meta http-equiv=refresh content='0; charset=latin1'>")]
    public void A_meta_that_names_utf8_or_nothing_known_is_accepted(string meta) =>
        BodyText(Parse(Utf8("<head>" + meta + "</head><body>x"))).Should().Be("x");

    [Fact]
    public void A_meta_in_the_head_after_the_prescan_window_is_refused_too()
    {
        var error = Failed(Parse(Utf8("<head><title>" + new string('t', 2000) + "</title><meta charset=latin1></head>")));

        error.Code.Should().Be(HtmlErrorCodes.Encoding);
        error.Metadata![ErrorMetadataKeys.SourceColumn].Should().Be(2022);
    }

    [Fact]
    public void A_meta_in_the_prescan_window_is_found_even_outside_the_head()
    {
        // The prescan reads the first 1024 bytes as bytes; tree construction would put this meta in the body.
        var error = Failed(Parse(Utf8("<body><p>x<meta charset=koi8-r>")));

        error.Code.Should().Be(HtmlErrorCodes.Encoding);
        error.Metadata![ErrorMetadataKeys.SourceColumn].Should().Be(11);
    }

    [Fact]
    public void A_byte_order_mark_wins_over_any_meta() =>
        BodyText(Parse([0xEF, 0xBB, 0xBF, .. Utf8("<head><meta charset=latin1></head><body>x")])).Should().Be("x");

    [Fact]
    public void Text_input_has_no_encoding_and_a_meta_charset_is_ignored() =>
        HtmlParser.Parse("<meta charset=latin1><body>x").Match(_ => true, _ => false).Should().BeTrue();

    [Fact]
    public void Bytes_longer_than_the_limit_are_refused_before_decoding() =>
        Failed(HtmlParser.Parse(new byte[HtmlInputLimits.MaxCharacters + 1])).Code.Should().Be(HtmlErrorCodes.TooLarge);
}
