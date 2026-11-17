using System;

namespace Ave.Extensions.Documents.Images.Png;

/// <summary>
/// The CRC-32 every PNG chunk ends with (PNG §5.5 "CRC algorithm", the ISO 3309 / ITU-T V.42 polynomial
/// x^32+x^26+x^23+x^22+x^16+x^12+x^11+x^10+x^8+x^7+x^5+x^4+x^2+x+1, reflected as 0xEDB88320). The base library has no
/// CRC-32 outside System.IO.Hashing, a separate package, so the table-driven form from the specification's sample
/// code is written out here.
/// </summary>
internal static class Crc32
{
    private const uint Polynomial = 0xEDB88320;

    private static readonly uint[] Table = BuildTable();

    /// <summary>The CRC of the bytes: start with all ones, run every byte through the table, invert at the end.</summary>
    public static uint Compute(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc = Table[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    /// <summary>The CRC of every byte value, as in the specification's <c>make_crc_table</c>.</summary>
    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint value = 0; value < table.Length; value++)
        {
            var crc = value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? Polynomial ^ (crc >> 1) : crc >> 1;
            }

            table[value] = crc;
        }

        return table;
    }
}
