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
            parsed => string.Concat(parsed.Document.DocumentElement!.Children.OfType<HtmlElement>().Single(element => element.IsHtml("body")).Children.OfType<HtmlText>().Select(text => text.Data)),
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
    public void Bytes_that_are_not_utf8_under_a_utf8_declaration_become_replacement_characters_with_a_diagnostic()
    {
        var result = Parse([.. Utf8("<meta charset=utf-8><body>\na"), 0xC3, 0x28, .. Utf8("b")]);

        BodyText(result).Should().Be("\na�(b");
        var diagnostic = result.Match(parsed => parsed.Diagnostics, _ => []).Single(error => (string)error.Metadata![ErrorMetadataKeys.Value] == "invalid-byte-sequence");
        diagnostic.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(2);
        diagnostic.Metadata![ErrorMetadataKeys.SourceColumn].Should().Be(2);
    }

    [Fact]
    public void Bytes_that_are_not_utf8_with_nothing_declared_are_refused()
    {
        // Chromium 159 detects windows-1252 for these bytes and shows café (HD-16).
        var error = Failed(Parse([.. Utf8("<!DOCTYPE html>\n<p>caf"), 0xE9, .. Utf8("</p>")]));

        error.Code.Should().Be(HtmlErrorCodes.Encoding);
        error.Metadata![ErrorMetadataKeys.Value].Should().Be("(undeclared)");
        (error.Metadata![ErrorMetadataKeys.SourceLine], error.Metadata![ErrorMetadataKeys.SourceColumn]).Should().Be((2, 7));
    }

    [Theory]
    [InlineData("<?xml version=\"1.0\" encoding=\"iso-8859-2\"?>", "iso-8859-2")]
    [InlineData("<?xml version='1.0' encoding='windows-1251'?>", "windows-1251")]
    public void An_xml_declaration_that_names_another_encoding_is_refused(string declaration, string label)
    {
        // Chromium 159 decodes an HTML document by an XML declaration at its start (HD-13).
        var error = Failed(Parse(Utf8(declaration + "<!DOCTYPE html><p>café")));

        error.Code.Should().Be(HtmlErrorCodes.Encoding);
        error.Metadata![ErrorMetadataKeys.Value].Should().Be(label);
    }

    [Fact]
    public void An_xml_declaration_that_names_utf8_is_accepted() =>
        BodyText(Parse(Utf8("<?xml version=\"1.0\" encoding=\"UTF-8\"?><!DOCTYPE html><body>café"))).Should().Be("café");

    [Theory]
    [InlineData("<meta charset=latin1 charset=utf-8>", true)]
    [InlineData("<meta charset=utf-8 charset=latin1>", false)]
    public void Of_two_charset_attributes_the_last_one_counts(string meta, bool accepted)
    {
        // Chromium 159 takes the last (HD-14); the prescan of the standard would take the first.
        var result = Parse(Utf8("<head>" + meta + "</head><body>x"));

        result.Match(_ => true, error => error.Code != HtmlErrorCodes.Encoding).Should().Be(accepted);
    }

    [Theory]
    // A listed limit (HD-14, docs/architecture.md): past the first 1024 bytes the prescan does not see the meta, and
    // tree construction sees only the first of two charset attributes (the tokenizer drops a repeated attribute).
    [InlineData("<meta charset=utf-8 charset=shift_jis>", true)]
    [InlineData("<meta charset=shift_jis charset=utf-8>", false)]
    public void Of_two_charset_attributes_past_the_first_1024_bytes_the_first_one_counts(string meta, bool accepted)
    {
        var result = Parse(Utf8("<!DOCTYPE html><!--" + new string('c', 1_100) + "--><head>" + meta + "</head><body>x"));

        result.Match(_ => true, error => error.Code != HtmlErrorCodes.Encoding).Should().Be(accepted);
    }

    [Theory]
    // E-2: two meta elements that name two encodings are refused in either order (stricter than Chromium, which
    // reads the first: a refusal is allowed).
    [InlineData("<meta charset=utf-8><meta charset=shift_jis>")]
    [InlineData("<meta charset=shift_jis><meta charset=utf-8>")]
    public void Two_meta_elements_that_name_two_encodings_are_refused_in_either_order(string metas) =>
        Failed(Parse(Utf8("<head>" + metas + "</head><body>x"))).Code.Should().Be(HtmlErrorCodes.Encoding);

    [Fact]
    public void An_xml_declaration_that_names_another_encoding_is_refused_by_its_name()
    {
        var error = Failed(Parse(Utf8("<?xml version=\"1.0\" encoding=\"iso-8859-2\"?><!DOCTYPE html><p>x")));

        error.Code.Should().Be(HtmlErrorCodes.Encoding);
        error.Message.Should().StartWith("The XML declaration says the bytes are in the encoding 'iso-8859-2'");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Utf16_that_ends_inside_a_character_drops_the_rest(bool bigEndian)
    {
        // Chromium 159 drops a last odd byte and a high surrogate without its pair (HD-15).
        var encoding = new UnicodeEncoding(bigEndian, byteOrderMark: true);
        var whole = encoding.GetPreamble().Concat(encoding.GetBytes("<body>ab")).ToArray();
        byte[] surrogate = bigEndian ? [0xD8, 0x3D] : [0x3D, 0xD8];

        BodyText(Parse([.. whole, 0x41])).Should().Be("ab");
        BodyText(Parse([.. whole, .. surrogate])).Should().Be("ab");
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
