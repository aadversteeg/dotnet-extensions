using System.Buffers.Binary;
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
    private static Result<HtmlBoxes, Error> Boxes(string body, UnsupportedPolicy policy = UnsupportedPolicy.Refuse) =>
        HtmlParser.Parse($"<!doctype html><html lang=\"en\"><head><title>t</title><style>body {{ font-family: Lato }}</style></head><body>{body}</body></html>", new HtmlParseOptions("t.html"))
            .OnSuccessBind(parsed => HtmlStyleResolver.Resolve(parsed, new HtmlStyleOptions { Unsupported = policy }))
            .OnSuccessBind(styled => HtmlBoxGenerator.Generate(styled, new HtmlBoxOptions { Fonts = TestFonts.SetWithItalic, Unsupported = policy }));

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

    [Fact]
    public void An_image_past_the_pixels_of_a_document_is_refused_at_its_element_before_it_is_decoded()
    {
        var full = string.Concat(Enumerable.Range(0, (int)(ImageLimits.MaxDocumentPixels / ImageLimits.MaxPixels)).Select(image => Img(LargeHeader((byte)image))));
        var body = $"<p>{full}</p>\n<p>{Img(Small)}</p>";
        var allocated = GC.GetAllocatedBytesForCurrentThread();

        var refusals = Boxes(body).Error.Parts();

        (GC.GetAllocatedBytesForCurrentThread() - allocated).Should().BeLessThan(64L * 1024 * 1024, "no image is decoded");
        var refused = refusals.Single(error => error.Message.Contains("of one document", StringComparison.Ordinal));
        refused.Code.Should().Be(HtmlStyleErrorCodes.Unsupported.Rendering);
        refused.Message.Should().Contain(ImageLimits.MaxDocumentPixels.ToString(System.Globalization.CultureInfo.InvariantCulture));
        refused.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(2, "at the img that passes the limit");
    }

    [Fact]
    public void An_image_shown_many_times_is_decoded_and_charged_once()
    {
        // The large image three times would be three times the limit of one image, more than a document's pixels, if
        // each use were charged; once, it leaves room for the small image.
        var large = LargeHeader(1);
        var body = $"<p>{Img(large)}{Img(large)}{Img(large)}</p>\n<p>{Img(Small)}</p>";

        var refusals = Boxes(body).Error.Parts();

        refusals.Should().NotContain(error => error.Message.Contains("of one document", StringComparison.Ordinal));
        refusals.Should().HaveCount(3, "each use of the image without data is refused, from one decode");
    }
}
