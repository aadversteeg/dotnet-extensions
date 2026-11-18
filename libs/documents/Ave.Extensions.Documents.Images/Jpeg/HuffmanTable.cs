using System;

namespace Ave.Extensions.Documents.Images.Jpeg;

/// <summary>
/// A Huffman table from a DHT segment (ITU-T T.81 §B.2.4.2), with the decoding tables of Annex F.2.2.3: the codes are
/// generated from the number of codes of each length (BITS, Annex C, Figures C.1 and C.2), then for each length l
/// MAXCODE(l) is the largest code of that length, MINCODE(l) the smallest and VALPTR(l) the index of its value in HUFFVAL.
/// </summary>
internal sealed class HuffmanTable
{
    private readonly int[] _maxCode = new int[18];
    private readonly int[] _minCode = new int[17];
    private readonly int[] _valuePointer = new int[17];
    private readonly byte[] _values;

    private HuffmanTable(byte[] counts, byte[] values)
    {
        _values = values;
        var code = 0;
        var index = 0;
        for (var length = 1; length <= 16; length++)
        {
            _valuePointer[length] = index;
            _minCode[length] = code;
            code += counts[length - 1];
            index += counts[length - 1];
            _maxCode[length] = counts[length - 1] == 0 ? -1 : code - 1;
            code <<= 1;
        }

        // F.2.2.3: MAXCODE(17) is set so that the search always ends.
        _maxCode[17] = int.MaxValue;
    }

    /// <summary>
    /// Builds a table from BITS (16 counts) and HUFFVAL, or null when the counts describe more codes than the code
    /// space holds (a code of length l must be below 2^l; an all-ones code is allowed by the arithmetic but Annex C
    /// generates codes in order, so the codes of every length must fit).
    /// </summary>
    public static HuffmanTable? Create(ReadOnlySpan<byte> counts, ReadOnlySpan<byte> values)
    {
        var code = 0;
        for (var length = 1; length <= 16; length++)
        {
            code += counts[length - 1];
            if (code > 1 << length)
            {
                return null;
            }

            code <<= 1;
        }

        return new HuffmanTable(counts.ToArray(), values.ToArray());
    }

    /// <summary>
    /// DECODE (F.2.2.3, Figure F.16): read bits one at a time until the code read is at most MAXCODE of its length; the
    /// value is HUFFVAL[VALPTR + code − MINCODE]. Null when no code of 16 bits or fewer matches (corrupt data).
    /// </summary>
    public int? Decode(BitReader reader)
    {
        var code = reader.Bit();
        var length = 1;
        while (code > _maxCode[length])
        {
            if (length == 16)
            {
                return null;
            }

            code = (code << 1) | reader.Bit();
            length++;
        }

        var index = _valuePointer[length] + code - _minCode[length];
        return index < _values.Length ? _values[index] : null;
    }
}
