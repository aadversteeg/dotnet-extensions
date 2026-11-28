using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.Documents.Html.Boxes;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.Documents.Images;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace UnitTests.Extensions.Documents.Html.Boxes;

/// <summary>
/// The images of one HTML document: each URL decoded once however many elements show it, and all of them together
/// within <see cref="ImageLimits.MaxDocumentPixels"/>, charged from each image's header before it is decoded.
/// </summary>
public class ImageLimitTests
{
    private static Result<HtmlBoxes, Error> Boxes(string body, UnsupportedPolicy policy = UnsupportedPolicy.Refuse, ResourceResolver? resources = null) =>
        HtmlParser.Parse($"<!doctype html><html lang=\"en\"><head><title>t</title><style>body {{ font-family: Lato }}</style></head><body>{body}</body></html>", new HtmlParseOptions("t.html"))
            .OnSuccessBind(parsed => HtmlStyleResolver.Resolve(parsed, new HtmlStyleOptions { Unsupported = policy, Resources = resources }))
            .OnSuccessBind(styled => HtmlBoxGenerator.Generate(styled, new HtmlBoxOptions { Fonts = TestFonts.SetWithItalic, Unsupported = policy, Resources = resources }));

    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }

        return ~crc;
    }

    private static byte[] Chunk(string type, byte[] data)
    {
        var chunk = new byte[12 + data.Length];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)data.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(chunk, 4);
        data.CopyTo(chunk, 8);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + data.Length), Crc(chunk.AsSpan(4, 4 + data.Length)));
        return chunk;
    }

    /// <summary>A PNG of 8192 x 8192 pixels, 1 bit per pixel with a palette, without image data: the header is all
    /// that is read before it is refused. <paramref name="mark"/> makes files that differ.</summary>
    private static string LargeHeader(byte mark)
    {
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, 8192);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), 8192);
        (header[8], header[9]) = (1, 3);
        byte[] png = [137, 80, 78, 71, 13, 10, 26, 10, .. Chunk("IHDR", header), .. Chunk("PLTE", [mark, 2, 3]), .. Chunk("IEND", [])];
        return "data:image/png;base64," + Convert.ToBase64String(png);
    }

    private static readonly string Small = "data:image/png;base64," + Convert.ToBase64String([.. ImageEncoder.EncodePng(1, 1, [0, 0, 0, 255]).Value]);

    private static string Img(string url) => $"<img alt=x src=\"{url}\">";

    /// <summary>A greyscale PNG of <paramref name="size"/> x <paramref name="size"/> black pixels, one byte each;
    /// <paramref name="mark"/> makes files that differ.</summary>
    private static byte[] Grey(int size, byte mark = 0)
    {
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)size);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)size);
        (header[8], header[9]) = (8, 0);
        return [137, 80, 78, 71, 13, 10, 26, 10, .. Chunk("IHDR", header), .. Chunk("tEXt", [(byte)'k', 0, mark]), .. Chunk("IDAT", Zeros(size)), .. Chunk("IEND", [])];
    }

    /// <summary>The image data of <see cref="Grey"/>: each row a filter byte and the pixels, all zero.</summary>
    private static byte[] Zeros(int size)
    {
        using var data = new MemoryStream();
        using (var zlib = new ZLibStream(data, CompressionLevel.Fastest, leaveOpen: true))
        {
            var row = new byte[size + 1];
            for (var y = 0; y < size; y++)
            {
                zlib.Write(row);
            }
        }

        return data.ToArray();
    }

    /// <summary>The largest image one document may decode, at the limit of one image (2^26 pixels).</summary>
    private static readonly Lazy<byte[]> Largest = new(() => Grey(8192));

    private static string DataUrl(byte[] png) => "data:image/png;base64," + Convert.ToBase64String(png);

    [Fact]
    public void An_image_past_the_pixels_of_a_document_is_refused_at_its_element_before_it_is_decoded()
    {
        // Two images at the limit of one take the pixels of a document; the small image after them is refused.
        var full = string.Concat(Enumerable.Range(0, (int)(ImageLimits.MaxDocumentPixels / ImageLimits.MaxPixels)).Select(image => Img(DataUrl(Grey(8192, (byte)image)))));
        var body = $"<p>{full}</p>\n<p>{Img(Small)}</p>";

        var refusals = Boxes(body).Error.Parts();

        var refused = refusals.Single();
        refused.Code.Should().Be(HtmlStyleErrorCodes.Unsupported.Rendering);
        refused.Message.Should().Contain("of one document").And.Contain(ImageLimits.MaxDocumentPixels.ToString(System.Globalization.CultureInfo.InvariantCulture));
        refused.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(2, "at the img that passes the limit");
    }

    [Fact]
    public void An_image_shown_many_times_is_decoded_and_charged_once()
    {
        // The largest image three times would be more than a document's pixels if each use were charged; once, it
        // leaves room for the small image.
        var large = DataUrl(Largest.Value);
        var body = $"<p>{Img(large)}{Img(large)}{Img(large)}</p>\n<p>{Img(Small)}</p>";

        Boxes(body).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void A_fragment_does_not_make_an_image_another_one()
    {
        // g.png#0, #1 and #2 name one file: decoded and charged once, it leaves room for the small image.
        var body = $"<p>{Img("g.png#0")}{Img("g.png#1")}{Img("g.png#2")}</p>\n<p>{Img(Small)}</p>";

        Boxes(body, resources: url => url == "g.png" ? new HtmlResource(Largest.Value) : null).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(UnsupportedPolicy.Warn)]
    [InlineData(UnsupportedPolicy.Refuse)]
    public void Images_whose_headers_announce_more_than_their_data_holds_leave_the_pixels_of_the_document_to_the_others(UnsupportedPolicy policy)
    {
        // Three files of 68 bytes whose headers announce 8192 x 8192 pixels and whose data inflates to ten bytes, then an honest
        // image of 16 x 16: each liar is refused as it does not decode, and costs nothing; the honest image is shown.
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, 8192);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), 8192);
        (header[8], header[9]) = (8, 0);
        byte[] liar = [137, 80, 78, 71, 13, 10, 26, 10, .. Chunk("IHDR", header), .. Chunk("IDAT", [0x78, 0x9C, 0x63, 0x60, 0x80, 0x01, 0x00, 0x00, 0x0A, 0x00, 0x01]), .. Chunk("IEND", [])];
        liar.Should().HaveCount(68);
        var files = new Dictionary<string, byte[]> { ["l1.png"] = liar, ["l2.png"] = liar, ["l3.png"] = liar, ["small.png"] = Grey(16) };
        var body = "<img alt=a width=20 src=l1.png><img alt=a width=20 src=l2.png><img alt=a width=20 src=l3.png><img alt=s width=20 src=small.png><p>after</p>";

        var boxes = Boxes(body, policy, url => files.TryGetValue(url, out var file) ? new HtmlResource(file) : null);

        List<Error> reported = policy == UnsupportedPolicy.Warn ? [.. boxes.Value.Diagnostics] : [.. boxes.Error.Parts()];
        reported.Should().HaveCount(3).And.OnlyContain(error => error.Message.Contains("does not decode", StringComparison.Ordinal) && !error.Message.Contains("of one document", StringComparison.Ordinal));
    }

    [Fact]
    public void Images_that_do_not_decode_end_decoding_for_the_document_once_they_have_produced_its_limit()
    {
        // Files of 65 KB whose data inflates to one byte less than the 8192 x 8192 pixels their headers announce: two of
        // them produce ImageLimits.MaxFailedDocumentPixels, so the third and the honest image after it are not decoded.
        var grey = Grey(8192);
        var idat = Encoding.ASCII.GetBytes("IDAT");
        var at = grey.AsSpan().IndexOf(idat) - 4;
        byte[] Short(byte mark)
        {
            using var shortData = new MemoryStream();
            using (var zlib = new ZLibStream(shortData, CompressionLevel.Fastest, leaveOpen: true))
            {
                zlib.Write(new byte[(8192 * 8193) - 1]);
            }

            return [.. grey.AsSpan(0, at), .. Chunk("tEXt", [(byte)'k', 0, mark]), .. Chunk("IDAT", shortData.ToArray()), .. Chunk("IEND", [])];
        }

        var files = new Dictionary<string, byte[]> { ["l1.png"] = Short(1), ["l2.png"] = Short(2), ["l3.png"] = Short(3), ["small.png"] = Grey(16) };
        var body = "<img alt=a width=20 src=l1.png><img alt=a width=20 src=l2.png>\n<img alt=a width=20 src=l3.png><img alt=s width=20 src=small.png><p>after</p>";

        var diagnostics = Boxes(body, UnsupportedPolicy.Warn, url => files.TryGetValue(url, out var file) ? new HtmlResource(file) : null).Value.Diagnostics;

        diagnostics.Should().HaveCount(4);
        diagnostics.Take(2).Should().OnlyContain(error => error.Message.Contains("does not decode", StringComparison.Ordinal) && error.Message.Contains("67117056 bytes", StringComparison.Ordinal));
        diagnostics.Skip(2).Should().OnlyContain(error => error.Message.Contains("is not decoded: images of this document that did not decode have produced 134217728 pixels, and those of one document may produce at most 134217728 together", StringComparison.Ordinal)
            && Equals(error.Metadata![ErrorMetadataKeys.SourceLine], 2));
    }
}
