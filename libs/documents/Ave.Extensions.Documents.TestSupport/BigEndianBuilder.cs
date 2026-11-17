using System.Collections.Generic;

namespace Ave.Extensions.Documents.TestSupport;

/// <summary>Builds big-endian byte arrays for hand-made font tables in tests.</summary>
public sealed class BigEndianBuilder
{
    private readonly List<byte> _bytes = [];

    /// <summary>The bytes written so far.</summary>
    public int Length => _bytes.Count;

    /// <summary>Appends a 16-bit value, high byte first.</summary>
    /// <param name="value">The value; only its low 16 bits are written.</param>
    /// <returns>This builder.</returns>
    public BigEndianBuilder UInt16(int value)
    {
        _bytes.Add((byte)(value >> 8));
        _bytes.Add((byte)value);
        return this;
    }

    /// <summary>Appends a 32-bit value, high byte first.</summary>
    /// <param name="value">The value; only its low 32 bits are written.</param>
    /// <returns>This builder.</returns>
    public BigEndianBuilder UInt32(long value)
    {
        UInt16((int)(value >> 16));
        return UInt16((int)(value & 0xFFFF));
    }

    /// <summary>Appends bytes as they are.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>This builder.</returns>
    public BigEndianBuilder Bytes(IEnumerable<byte> bytes)
    {
        _bytes.AddRange(bytes);
        return this;
    }

    /// <summary>The bytes written.</summary>
    /// <returns>A new array.</returns>
    public byte[] ToArray() => [.. _bytes];
}
