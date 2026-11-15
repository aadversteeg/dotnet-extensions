namespace UnitTests.Ave.Documents.Fonts;

/// <summary>Builds big-endian byte arrays for hand-made font tables in tests.</summary>
internal sealed class BigEndianBuilder
{
    private readonly List<byte> _bytes = [];

    public int Length => _bytes.Count;

    public BigEndianBuilder UInt16(int value)
    {
        _bytes.Add((byte)(value >> 8));
        _bytes.Add((byte)value);
        return this;
    }

    public BigEndianBuilder UInt32(long value)
    {
        UInt16((int)(value >> 16));
        return UInt16((int)(value & 0xFFFF));
    }

    public BigEndianBuilder Bytes(IEnumerable<byte> bytes)
    {
        _bytes.AddRange(bytes);
        return this;
    }

    public byte[] ToArray() => [.. _bytes];
}
