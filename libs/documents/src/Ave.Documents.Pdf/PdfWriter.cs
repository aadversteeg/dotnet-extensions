using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Ave.Documents.Core;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Pdf;

/// <summary>
/// Writes a <see cref="PdfFile"/> as the bytes of a PDF 1.7 file with a classic cross-reference table
/// (ISO 32000-1 §7.5: header, body, cross-reference table, trailer). Pure: the same file gives the same bytes.
/// </summary>
public static class PdfWriter
{
    /// <summary>
    /// §7.5.2: the header line, then a comment of at least four bytes of 128 or more, which tells file-transfer
    /// tools that the file is binary.
    /// </summary>
    private static readonly byte[] Header = [.. "%PDF-1.7\n%"u8, 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n'];

    private const int FileIdentifierLength = 16;

    /// <summary>Writes the file.</summary>
    /// <param name="file">The objects and trailer entries.</param>
    /// <returns>The PDF bytes, or a <see cref="DocumentErrorCodes.Pdf.Write"/> error when objects are not numbered
    /// 1 to N or a number is not finite.</returns>
    public static Result<ImmutableArray<byte>, Error> Write(PdfFile file)
    {
        var problem = CheckNumbering(file.Objects) ?? CheckNumbers(file.Objects);
        if (problem is { } error)
        {
            return Result<ImmutableArray<byte>, Error>.Failure(error);
        }

        var output = new ArrayBufferWriter<byte>();
        output.Write(Header);

        // §7.5.4: the cross-reference table needs the byte offset of each object, so record them while writing.
        var offsets = new SortedDictionary<int, long>();
        foreach (var indirect in file.Objects)
        {
            offsets[indirect.Number] = output.WrittenCount;
            WriteIndirectObject(output, indirect);
        }

        var identifier = file.FileIdentifier ?? IdentifierFromContent(output.WrittenSpan);
        var crossReferenceOffset = output.WrittenCount;
        WriteAscii(output, CrossReferenceTable(offsets));
        WriteAscii(output, Trailer(file, offsets.Count + 1, identifier, crossReferenceOffset));
        return Result<ImmutableArray<byte>, Error>.Success([.. output.WrittenSpan]);
    }

    /// <summary>§7.5.4: object numbers must be 1..N; the table then needs one section without gaps.</summary>
    private static Error? CheckNumbering(ImmutableArray<IndirectObject> objects)
    {
        var numbers = objects.Select(indirect => indirect.Number).Order().ToImmutableArray();
        var expected = Enumerable.Range(1, objects.Length);
        return numbers.SequenceEqual(expected)
            ? null
            : new Error(
                    DocumentErrorCodes.Pdf.Write.InvalidObjectNumbers,
                    "Indirect objects must be numbered 1 to N without gaps or duplicates.")
                .With(ErrorMetadataKeys.Value, string.Join(",", numbers));
    }

    private static Error? CheckNumbers(ImmutableArray<IndirectObject> objects) =>
        objects
            .Select(indirect => indirect switch
            {
                IndirectValue value => PdfSerializer.FindNonFinite(value.Value),
                IndirectStream stream => PdfSerializer.FindNonFinite(stream.Stream.Dictionary),
                _ => null,
            })
            .FirstOrDefault(found => found is not null) is { } invalid
            ? PdfSerializer.InvalidNumber(invalid)
            : null;

    /// <summary>§7.3.10: <c>N 0 obj</c>, the object, <c>endobj</c>. A stream is its dictionary, then the
    /// keyword <c>stream</c>, an end-of-line, the encoded bytes, an end-of-line and <c>endstream</c> (§7.3.8.1).</summary>
    private static void WriteIndirectObject(ArrayBufferWriter<byte> output, IndirectObject indirect)
    {
        WriteAscii(output, $"{indirect.Number} 0 obj\n");
        switch (indirect)
        {
            case IndirectValue value:
                WriteAscii(output, PdfSerializer.Write(value.Value));
                break;
            case IndirectStream stream:
                var (dictionary, data) = Encode(stream.Stream);
                WriteAscii(output, PdfSerializer.Write(dictionary) + "\nstream\n");
                output.Write(data.AsSpan());
                WriteAscii(output, "\nendstream");
                break;
        }

        WriteAscii(output, "\nendobj\n");
    }

    /// <summary>Encodes the data and adds /Length (required, §7.3.8.2 Table 5) and /Filter to the dictionary.</summary>
    private static (PdfDictionary Dictionary, ImmutableArray<byte> Data) Encode(PdfStream stream)
    {
        if (stream.Dictionary.ContainsKey("Length") || stream.Dictionary.ContainsKey("Filter"))
        {
            throw new ArgumentException("A stream dictionary must not set /Length or /Filter; the writer adds them.");
        }

        var data = stream.Encoding == StreamEncoding.Flate ? Flate.Compress(stream.Data) : stream.Data;
        PdfObject? filter = stream.Encoding switch
        {
            StreamEncoding.Flate => new PdfName("FlateDecode"),
            StreamEncoding.Dct => new PdfName("DCTDecode"),
            _ => null,
        };
        var entries = stream.Dictionary.Entries
            .Select(entry => (entry.Key, (PdfObject?)entry.Value))
            .Append(("Length", new PdfInteger(data.Length)))
            .Append(("Filter", filter));
        return (PdfDictionary.Of(entries), data);
    }

    /// <summary>
    /// §7.5.4: one section starting at object 0, which is the head of the free list (<c>0000000000 65535 f</c>);
    /// then one in-use entry per object: a 10-digit byte offset, a 5-digit generation and <c>n</c>. Every entry is
    /// exactly 20 bytes, ending in CR LF.
    /// </summary>
    private static string CrossReferenceTable(SortedDictionary<int, long> offsets)
    {
        var table = new StringBuilder($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f\r\n");
        foreach (var offset in offsets.Values)
        {
            table.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n\r\n");
        }

        return table.ToString();
    }

    /// <summary>§7.5.5: the trailer dictionary, then the offset of the cross-reference table and <c>%%EOF</c>.
    /// /Size is one more than the highest object number. /ID holds two identifiers; for a newly created file
    /// both are the same (§14.4).</summary>
    private static string Trailer(PdfFile file, int size, ImmutableArray<byte> identifier, long crossReferenceOffset)
    {
        var id = PdfString.FromBytes(identifier);
        var trailer = PdfDictionary.Of(
            ("Size", new PdfInteger(size)),
            ("Root", file.Catalog),
            ("Info", file.Info),
            ("ID", new PdfArray([id, id])));
        return $"trailer\n{PdfSerializer.Write(trailer)}\nstartxref\n{crossReferenceOffset}\n%%EOF\n";
    }

    /// <summary>
    /// §14.4 asks for an identifier that is unique for the file and suggests a digest of the file's content
    /// among other things. A digest of everything written before the trailer satisfies "same content, same id".
    /// </summary>
    private static ImmutableArray<byte> IdentifierFromContent(ReadOnlySpan<byte> body) =>
        [.. SHA256.HashData(body).AsSpan(0, FileIdentifierLength)];

    private static void WriteAscii(ArrayBufferWriter<byte> output, string text) => output.Write(Encoding.ASCII.GetBytes(text));
}
