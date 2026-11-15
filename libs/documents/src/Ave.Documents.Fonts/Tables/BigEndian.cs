using System;
using System.Buffers.Binary;
using System.Text;

namespace Ave.Documents.Fonts.Tables;

/// <summary>
/// Reads the big-endian values OpenType files are made of ("OpenType font files use big-endian byte order",
/// OpenType spec, "Data types").
/// </summary>
/// <remarks>
/// These readers do not check bounds themselves. Every table parser first checks that the table is long enough
/// for everything it is about to read and returns a <c>Truncated</c> error otherwise; an out-of-range read here
/// is therefore a bug in a parser and throws, as programming errors do.
/// </remarks>
internal static class BigEndian
{
    /// <summary>Reads a uint16.</summary>
    public static ushort UInt16(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);

    /// <summary>Reads an int16 (also used for the FWORD type).</summary>
    public static short Int16(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadInt16BigEndian(data[offset..]);

    /// <summary>Reads a uint32 (also used for the Offset32 type).</summary>
    public static uint UInt32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);

    /// <summary>Reads a Fixed: a signed 16.16 fixed-point number.</summary>
    public static double Fixed(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadInt32BigEndian(data[offset..]) / 65536.0;

    /// <summary>Reads a Tag: four ASCII characters, for example <c>cmap</c>.</summary>
    public static string Tag(ReadOnlySpan<byte> data, int offset) => Encoding.ASCII.GetString(data.Slice(offset, 4));
}
