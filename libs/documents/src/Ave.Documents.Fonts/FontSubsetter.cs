using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using Ave.Documents.Fonts.Tables;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Fonts;

/// <summary>
/// Builds a TrueType font program that holds only the glyphs a document uses, for embedding in PDF (ISO
/// 32000-1 §9.9: an embedded font "may be a subset"). Glyph ids are KEPT: every glyph that is not used becomes an
/// empty glyph, so the codes in the content streams, the widths and the ToUnicode map stay valid without any
/// renumbering. Deterministic: the same font and glyphs always give the same bytes.
/// </summary>
/// <remarks>
/// The subset has the tables a PDF consumer needs to draw the glyphs of a CIDFontType2 font with an identity
/// CIDToGIDMap (§9.7.4.2): 'head', 'hhea', 'maxp', 'hmtx', 'loca', 'glyf', and — when the font has them — the
/// hinting programs 'cvt ', 'fpgm', 'prep'. 'cmap', 'name', 'OS/2' and 'post' are left out: a viewer maps codes to
/// glyphs through the CIDToGIDMap, and the PDF itself carries the names and metrics (font descriptor, /W).
/// </remarks>
public static class FontSubsetter
{
    private static readonly string[] HintingTables = ["cvt ", "fpgm", "prep"];

    // 'glyf' composite glyph flags (OpenType spec, "glyf" → "Composite glyph description").
    private const ushort Arg1And2AreWords = 0x0001;
    private const ushort WeHaveAScale = 0x0008;
    private const ushort MoreComponents = 0x0020;
    private const ushort WeHaveAnXAndYScale = 0x0040;
    private const ushort WeHaveATwoByTwo = 0x0080;

    /// <summary>The checkSumAdjustment rule of 'head': "0xB1B0AFBA minus the sum of the entire font".</summary>
    private const uint ChecksumMagic = 0xB1B0AFBA;

    /// <summary>Builds the subset.</summary>
    /// <param name="font">The font.</param>
    /// <param name="glyphs">The glyphs used. Glyph 0 (.notdef) and the components of every composite glyph are
    /// added, because "the .notdef glyph … must be present" and a composite is drawn from its components.</param>
    /// <returns>The subset, or <c>Documents.Font.Parse.InvalidValue</c> for a composite glyph whose data is
    /// malformed.</returns>
    public static Result<FontSubset, Error> Subset(TrueTypeFont font, IEnumerable<GlyphId> glyphs) =>
        Closure(font, [new GlyphId(0), .. glyphs]).OnSuccessBind(kept =>
            TableDirectory.Parse(font.FileData).OnSuccessMap(directory =>
            {
                var tables = SubsetTables(font, directory, kept);
                return new FontSubset(Tag(font, kept), [.. kept], [.. Assemble(tables)]);
            }));

    /// <summary>
    /// The used glyphs plus, recursively, every component of a composite glyph among them. A worklist rather
    /// than recursion, so a malformed font whose composites refer to each other cannot overflow the stack.
    /// </summary>
    private static Result<ImmutableSortedSet<GlyphId>, Error> Closure(TrueTypeFont font, IEnumerable<GlyphId> start)
    {
        var kept = new SortedSet<GlyphId>(Comparer<GlyphId>.Create((a, b) => a.Value.CompareTo(b.Value)));
        var work = new Stack<GlyphId>(start);
        while (work.Count > 0)
        {
            var glyph = work.Pop();
            if (!kept.Add(glyph))
            {
                continue;
            }

            var components = Components(font.GlyphOutline(glyph).Span, glyph);
            if (components.IsFailure)
            {
                return Result<ImmutableSortedSet<GlyphId>, Error>.Failure(components.Error);
            }

            // A component must be a glyph of the font ("glyphIndex: glyph index of component", OpenType spec, "glyf"
            // → "Composite glyph description"); a font that names another is malformed.
            foreach (var component in components.Value)
            {
                if (component.Value >= font.GlyphCount)
                {
                    return FontParseErrors.InvalidValue<ImmutableSortedSet<GlyphId>>(
                        "glyf", $"composite glyph {glyph} names component {component.Value}, beyond maxp.numGlyphs {font.GlyphCount}.", component.Value);
                }

                work.Push(component);
            }
        }

        return Result<ImmutableSortedSet<GlyphId>, Error>.Success(kept.ToImmutableSortedSet(kept.Comparer));
    }

    /// <summary>
    /// The component glyph ids of a composite glyph; empty for a simple glyph. Layout per the OpenType spec
    /// ("glyf" → "Composite glyph description"): a 10-byte glyph header with a NEGATIVE numberOfContours, then
    /// records of flags (uint16), glyphIndex (uint16), two arguments (two bytes or two words) and an optional
    /// scale, x/y scale or 2×2 matrix, repeated while MORE_COMPONENTS is set.
    /// </summary>
    private static Result<ImmutableArray<GlyphId>, Error> Components(ReadOnlySpan<byte> data, GlyphId glyph)
    {
        const int HeaderLength = 10;
        if (data.Length < HeaderLength || BinaryPrimitives.ReadInt16BigEndian(data) >= 0)
        {
            return Result<ImmutableArray<GlyphId>, Error>.Success([]);
        }

        var components = ImmutableArray.CreateBuilder<GlyphId>();
        var offset = HeaderLength;
        ushort flags;
        do
        {
            if (offset + 4 > data.Length)
            {
                return MalformedComposite(glyph);
            }

            flags = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
            components.Add(new GlyphId(BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 2)..])));
            offset += 4 + ((flags & Arg1And2AreWords) != 0 ? 4 : 2) + TransformLength(flags);
        }
        while ((flags & MoreComponents) != 0);

        return offset <= data.Length ? Result<ImmutableArray<GlyphId>, Error>.Success(components.ToImmutable()) : MalformedComposite(glyph);
    }

    /// <summary>The bytes of a component's optional transform: one F2DOT14 scale, two, or a 2×2 matrix (four).</summary>
    private static int TransformLength(ushort flags) =>
        (flags & WeHaveAScale) != 0 ? 2
        : (flags & WeHaveAnXAndYScale) != 0 ? 4
        : (flags & WeHaveATwoByTwo) != 0 ? 8
        : 0;

    private static Result<ImmutableArray<GlyphId>, Error> MalformedComposite(GlyphId glyph) =>
        FontParseErrors.InvalidValue<ImmutableArray<GlyphId>>("glyf", $"composite glyph {glyph} ends inside a component record.", glyph);

    /// <summary>The tables of the subset, by tag. 'glyf' and 'loca' are rebuilt, 'head' gets the long 'loca'
    /// format; the others are copied unchanged.</summary>
    private static SortedDictionary<string, byte[]> SubsetTables(TrueTypeFont font, TableDirectory directory, ImmutableSortedSet<GlyphId> kept)
    {
        var (glyf, loca) = GlyphTables(font, kept);
        var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["head"] = Head(directory.Tables["head"].Span),
            ["hhea"] = directory.Tables["hhea"].ToArray(),
            ["maxp"] = directory.Tables["maxp"].ToArray(),
            ["hmtx"] = directory.Tables["hmtx"].ToArray(),
            ["glyf"] = glyf,
            ["loca"] = loca,
        };
        foreach (var tag in HintingTables.Where(directory.Tables.ContainsKey))
        {
            tables[tag] = directory.Tables[tag].ToArray();
        }

        return tables;
    }

    /// <summary>
    /// New 'glyf' and 'loca': kept glyphs are copied byte for byte, every other glyph is empty (equal successive
    /// offsets mean "no outline", OpenType spec "loca"). Each glyph is padded to 4 bytes, which the long 'loca'
    /// format allows and the spec recommends.
    /// </summary>
    private static (byte[] Glyf, byte[] Loca) GlyphTables(TrueTypeFont font, ImmutableSortedSet<GlyphId> kept)
    {
        var glyf = new List<byte>();
        var loca = new byte[(font.GlyphCount + 1) * 4];
        for (var index = 0; index < font.GlyphCount; index++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(loca.AsSpan(index * 4), (uint)glyf.Count);
            var glyph = new GlyphId((ushort)index);
            if (kept.Contains(glyph))
            {
                glyf.AddRange(font.GlyphOutline(glyph).Span);
                glyf.AddRange(new byte[Padding(glyf.Count)]);
            }
        }

        BinaryPrimitives.WriteUInt32BigEndian(loca.AsSpan(font.GlyphCount * 4), (uint)glyf.Count);
        return ([.. glyf], loca);
    }

    /// <summary>A copy of 'head' with indexToLocFormat = 1 (long offsets, offset 50) and checkSumAdjustment
    /// (offset 8) zeroed; the adjustment is filled in once the whole file is known.</summary>
    private static byte[] Head(ReadOnlySpan<byte> original)
    {
        var head = original.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(8), 0);
        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(50), 1);
        return head;
    }

    /// <summary>
    /// Writes the font file (OpenType spec, "Organization of an OpenType font"): the table directory with
    /// records sorted by tag, each with its checksum, offset and unpadded length; then the tables, each starting
    /// on a 4-byte boundary; then head.checkSumAdjustment.
    /// </summary>
    private static byte[] Assemble(SortedDictionary<string, byte[]> tables)
    {
        const int HeaderLength = 12;
        const int RecordLength = 16;
        var count = tables.Count;
        var entrySelector = (int)Math.Floor(Math.Log2(count));
        var searchRange = (1 << entrySelector) * 16;
        var directoryLength = HeaderLength + count * RecordLength;
        var length = directoryLength + tables.Values.Sum(table => table.Length + Padding(table.Length));
        var file = new byte[length];

        BinaryPrimitives.WriteUInt32BigEndian(file, 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(4), (ushort)count);
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(6), (ushort)searchRange);
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(8), (ushort)entrySelector);
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(10), (ushort)(count * 16 - searchRange));

        var record = HeaderLength;
        var offset = directoryLength;
        var headOffset = 0;
        foreach (var (tag, table) in tables)
        {
            for (var index = 0; index < 4; index++)
            {
                file[record + index] = (byte)tag[index];
            }

            BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(record + 4), Checksum(table));
            BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(record + 8), (uint)offset);
            BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(record + 12), (uint)table.Length);
            table.CopyTo(file, offset);
            headOffset = tag == "head" ? offset : headOffset;
            record += RecordLength;
            offset += table.Length + Padding(table.Length);
        }

        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(headOffset + 8), unchecked(ChecksumMagic - Checksum(file)));
        return file;
    }

    /// <summary>
    /// "The table checksum is the unsigned sum of the uint32 units of a given table", the table padded with zero
    /// bytes to a multiple of four (OpenType spec, "Calculating checksums").
    /// </summary>
    public static uint Checksum(ReadOnlySpan<byte> data)
    {
        var sum = 0u;
        for (var offset = 0; offset < data.Length; offset += 4)
        {
            var word = 0u;
            for (var index = 0; index < 4; index++)
            {
                word = (word << 8) | (offset + index < data.Length ? data[offset + index] : 0u);
            }

            sum = unchecked(sum + word);
        }

        return sum;
    }

    private static int Padding(int length) => (4 - (length % 4)) % 4;

    /// <summary>
    /// The subset tag, "exactly six uppercase letters" (ISO 32000-1 §9.6.4), derived from the font and the kept
    /// glyphs so that it is the same on every run and different for different subsets of a document.
    /// </summary>
    private static string Tag(TrueTypeFont font, ImmutableSortedSet<GlyphId> kept)
    {
        var input = string.Join(",", [font.Id.Digest, .. kept.Select(glyph => glyph.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))]);
        var hash = SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(input));
        return new string([.. hash.Take(6).Select(value => (char)('A' + value % 26))]);
    }
}
