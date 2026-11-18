using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Ave.Extensions.Documents.TestSupport;

/// <summary>Hand-made changes to font files for tests.</summary>
public static class FontFiles
{
    /// <summary>
    /// Rebuilds a font file with one table replaced, or added when the font has no table with that tag. The table
    /// directory is written again in tag order; checksums are left 0, since the parser does not check them.
    /// </summary>
    /// <param name="font">The font file.</param>
    /// <param name="tag">The table tag, for example "GSUB".</param>
    /// <param name="data">The new table.</param>
    /// <returns>The new font file.</returns>
    public static byte[] WithTable(byte[] font, string tag, byte[] data)
    {
        int count = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
        var tables = new List<(string Tag, byte[] Data)>();
        for (var index = 0; index < count; index++)
        {
            var record = 12 + index * 16;
            var name = Encoding.ASCII.GetString(font, record, 4);
            var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 8));
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 12));
            tables.Add((name, name == tag ? data : font.AsSpan(offset, length).ToArray()));
        }

        if (tables.All(table => table.Tag != tag))
        {
            tables.Add((tag, data));
            tables.Sort((left, right) => string.CompareOrdinal(left.Tag, right.Tag));
        }

        var file = new BigEndianBuilder().UInt32(0x00010000).UInt16(tables.Count).UInt16(0).UInt16(0).UInt16(0);
        var position = 12 + tables.Count * 16;
        foreach (var (name, bytes) in tables)
        {
            file.Bytes(Encoding.ASCII.GetBytes(name)).UInt32(0).UInt32(position).UInt32(bytes.Length);
            position += (bytes.Length + 3) / 4 * 4;
        }

        foreach (var (_, bytes) in tables)
        {
            file.Bytes(bytes).Bytes(new byte[(4 - bytes.Length % 4) % 4]);
        }

        return file.ToArray();
    }

    /// <summary>The bytes of one table of a font file.</summary>
    /// <param name="font">The font file.</param>
    /// <param name="tag">The table tag.</param>
    /// <returns>A copy of the table.</returns>
    /// <exception cref="ArgumentException">The font has no such table.</exception>
    public static byte[] Table(byte[] font, string tag)
    {
        int count = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
        for (var index = 0; index < count; index++)
        {
            var record = 12 + index * 16;
            if (Encoding.ASCII.GetString(font, record, 4) == tag)
            {
                var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 8));
                var length = (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 12));
                return font.AsSpan(offset, length).ToArray();
            }
        }

        throw new ArgumentException($"The font has no '{tag}' table.", nameof(tag));
    }

    /// <summary>
    /// Rebuilds a font file with hand-made glyph descriptions: glyph i gets <paramref name="glyphs"/>[i] (an empty array for a
    /// glyph without an outline), every later glyph of the font gets no outline. 'glyf' and 'loca' are written anew, 'loca'
    /// with long offsets (and 'head'.indexToLocFormat set to 1 to say so); the glyph count stays the font's.
    /// </summary>
    /// <param name="font">The font file.</param>
    /// <param name="glyphs">The glyph descriptions, at most the font's glyph count.</param>
    /// <returns>The new font file.</returns>
    public static byte[] WithGlyphs(byte[] font, IReadOnlyList<byte[]> glyphs)
    {
        var maxp = Table(font, "maxp");
        int glyphCount = BinaryPrimitives.ReadUInt16BigEndian(maxp.AsSpan(4));
        var glyf = new BigEndianBuilder();
        var loca = new BigEndianBuilder().UInt32(0);
        for (var glyph = 0; glyph < glyphCount; glyph++)
        {
            if (glyph < glyphs.Count)
            {
                // Not padded: with long offsets a glyph may have any length, and a padding byte would be part of it.
                glyf.Bytes(glyphs[glyph]);
            }

            loca.UInt32(glyf.Length);
        }

        var head = Table(font, "head");
        head[50] = 0;
        head[51] = 1;
        return WithTable(WithTable(WithTable(font, "head", head), "glyf", glyf.ToArray()), "loca", loca.ToArray());
    }
}
