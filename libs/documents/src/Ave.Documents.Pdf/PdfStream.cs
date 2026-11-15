using Ave.Documents.Core;

namespace Ave.Documents.Pdf;

/// <summary>How the writer encodes a stream's data.</summary>
public enum StreamEncoding
{
    /// <summary>Written as is.</summary>
    None,

    /// <summary>Compressed with zlib/deflate and marked <c>/Filter /FlateDecode</c> (ISO 32000-1 §7.4.4).</summary>
    Flate,

    /// <summary>Data that is ALREADY a JPEG (DCT) encoded image, written unchanged and marked <c>/Filter /DCTDecode</c>
    /// (ISO 32000-1 §7.4.8).</summary>
    Dct,
}

/// <summary>
/// A stream object (ISO 32000-1 §7.3.8): a dictionary plus a sequence of bytes, for content streams, embedded
/// font files and CMaps. Holds the DECODED data (for <see cref="StreamEncoding.Dct"/>: the JPEG file); the writer encodes it and adds the <c>/Length</c> and
/// <c>/Filter</c> entries, which the dictionary must therefore not contain.
/// </summary>
/// <param name="Dictionary">The stream dictionary, without Length and Filter.</param>
/// <param name="Data">The decoded data.</param>
/// <param name="Encoding">How to encode the data in the file.</param>
public sealed record PdfStream(PdfDictionary Dictionary, ValueList<byte> Data, StreamEncoding Encoding);
