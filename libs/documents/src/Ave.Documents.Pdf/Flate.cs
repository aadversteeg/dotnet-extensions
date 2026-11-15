using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;

namespace Ave.Documents.Pdf;

/// <summary>
/// The FlateDecode filter's encoder (ISO 32000-1 §7.4.4): zlib format (RFC 1950) around deflate data
/// (RFC 1951), from the .NET base library.
/// </summary>
/// <remarks>
/// The compressed bytes are the same for the same input on the same .NET runtime, which is what the determinism
/// test checks. A different runtime version may compress differently (the runtime's zlib can change); if output
/// must be byte-identical ACROSS runtime versions, a managed deflate encoder replaces this one function.
/// </remarks>
public static class Flate
{
    /// <summary>Compresses data in zlib format with a fixed compression level.</summary>
    /// <param name="data">The data.</param>
    /// <returns>The compressed data.</returns>
    public static ImmutableArray<byte> Compress(ImmutableArray<byte> data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data.AsSpan());
        }

        return [.. output.ToArray()];
    }
}
