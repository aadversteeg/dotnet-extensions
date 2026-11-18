using System;

namespace Ave.Extensions.Documents.Images.Jpeg;

/// <summary>
/// Reads the bits of an entropy-coded segment (ITU-T T.81 §B.1.1.5, §F.1.2.3), most significant bit first. A byte FF in
/// the data is followed by a stuffed 00, which is dropped; FF followed by anything else is a marker and ends the segment.
/// Bits asked for past the end are supplied as 0 and counted, so the decoder can refuse data that ends too early.
/// </summary>
internal sealed class BitReader
{
    private readonly ReadOnlyMemory<byte> _data;
    private int _position;
    private int _buffer;
    private int _count;

    public BitReader(ReadOnlyMemory<byte> data, int position)
    {
        _data = data;
        _position = position;
    }

    /// <summary>The position of the first byte not read: the marker that ended the segment, once it has been reached.</summary>
    public int Position => _position;

    /// <summary>How many bits were asked for after the segment ended.</summary>
    public int MissingBits { get; private set; }

    /// <summary>One bit.</summary>
    public int Bit()
    {
        if (_count == 0)
        {
            Fill();
        }

        _count--;
        return (_buffer >> _count) & 1;
    }

    /// <summary>RECEIVE(s) (F.2.2.4): s bits as an unsigned number, s from 0 to 16.</summary>
    public int Bits(int count)
    {
        var value = 0;
        for (var index = 0; index < count; index++)
        {
            value = (value << 1) | Bit();
        }

        return value;
    }

    /// <summary>EXTEND (F.2.2.1, Figure F.12): a value of <paramref name="size"/> bits whose first bit is 0 is negative.</summary>
    public int Extended(int size)
    {
        if (size == 0)
        {
            return 0;
        }

        var value = Bits(size);
        return value < 1 << (size - 1) ? value - (1 << size) + 1 : value;
    }

    /// <summary>Drops the bits left in the current byte, so reading continues at a byte boundary (before a marker).</summary>
    public void AlignToByte() => _count = 0;

    private void Fill()
    {
        var span = _data.Span;
        if (_position < span.Length && span[_position] != 0xFF)
        {
            _buffer = span[_position++];
        }
        else if (_position + 1 < span.Length && span[_position] == 0xFF && span[_position + 1] == 0x00)
        {
            _buffer = 0xFF;
            _position += 2;
        }
        else
        {
            // A marker or the end of the file: the segment has ended.
            _buffer = 0;
            MissingBits += 8;
        }

        _count = 8;
    }
}
