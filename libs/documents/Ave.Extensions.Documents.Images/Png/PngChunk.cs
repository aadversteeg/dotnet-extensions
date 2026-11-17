using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Images.Png;

/// <summary>One chunk of a PNG file (PNG §5.3 "Chunk layout"): a four-letter type and its data.</summary>
/// <param name="Type">The chunk type, for example <c>IHDR</c>.</param>
/// <param name="Data">The chunk data.</param>
/// <param name="Offset">Where the chunk starts in the file, for error messages.</param>
internal sealed record PngChunk(string Type, ReadOnlyMemory<byte> Data, long Offset)
{
    /// <summary>
    /// True for a critical chunk: "Ancillary bit: first byte … 0 (uppercase) = critical" (§5.4). A decoder that meets
    /// an unknown critical chunk must not show the image.
    /// </summary>
    public bool IsCritical => char.IsAsciiLetterUpper(Type[0]);

    /// <summary>
    /// Splits the file after the signature into chunks. Each chunk is: a 4-byte big-endian length (at most 2^31 − 1),
    /// the 4-byte type, the data, and a CRC-32 over type and data (§5.3). A wrong CRC is an error: the data is
    /// damaged. Reading stops after IEND (§11.2.5); bytes after it are ignored.
    /// </summary>
    /// <param name="file">The whole file.</param>
    /// <param name="start">The offset of the first chunk (just after the signature).</param>
    /// <returns>The chunks in file order, or a <c>Documents.Image.Invalid</c> error.</returns>
    public static Result<ImmutableArray<PngChunk>, Error> ReadAll(ReadOnlyMemory<byte> file, int start)
    {
        var chunks = ImmutableArray.CreateBuilder<PngChunk>();
        var offset = start;
        while (true)
        {
            var span = file.Span;
            if (span.Length - offset < 12)
            {
                return ImageErrors.Invalid<ImmutableArray<PngChunk>>("the file ends before the IEND chunk.", offset);
            }

            var length = BinaryPrimitives.ReadUInt32BigEndian(span[offset..]);
            if (length > int.MaxValue || length > span.Length - offset - 12)
            {
                return ImageErrors.Invalid<ImmutableArray<PngChunk>>($"a chunk length of {length} runs past the end of the file.", offset);
            }

            var typeAndData = span.Slice(offset + 4, 4 + (int)length);
            var type = Encoding.ASCII.GetString(typeAndData[..4]);
            if (!IsValidType(typeAndData[..4]))
            {
                return ImageErrors.Invalid<ImmutableArray<PngChunk>>("a chunk type must be four ASCII letters.", offset + 4);
            }

            var storedCrc = BinaryPrimitives.ReadUInt32BigEndian(span[(offset + 8 + (int)length)..]);
            if (Crc32.Compute(typeAndData) != storedCrc)
            {
                return ImageErrors.Invalid<ImmutableArray<PngChunk>>($"the CRC of chunk {type} does not match its data.", offset);
            }

            chunks.Add(new PngChunk(type, file.Slice(offset + 8, (int)length), offset));
            offset += 12 + (int)length;
            if (type == "IEND")
            {
                return Result<ImmutableArray<PngChunk>, Error>.Success(chunks.ToImmutable());
            }
        }
    }

    /// <summary>§5.3: "Each byte of a chunk type is restricted to the decimal values 65 to 90 and 97 to 122", the ISO 646
    /// letters A–Z and a–z.</summary>
    private static bool IsValidType(ReadOnlySpan<byte> type)
    {
        foreach (var value in type)
        {
            if (!char.IsAsciiLetter((char)value))
            {
                return false;
            }
        }

        return true;
    }
}
