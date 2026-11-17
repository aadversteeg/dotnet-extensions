using System.Runtime.InteropServices;
using System.Text;
using Ave.Documents.Fonts;
using Ave.Documents.Fonts.OpenType;
using Ave.Documents.Fonts.Tables;
using Ave.Documents.TestSupport;

namespace UnitTests.Ave.Documents.Fonts;

/// <summary>
/// Font rules that Lato never exercises — it has no 2×2 component transforms, no 'kern' table and only US English
/// names — so no test of a real font would notice a change to them. The tables are built or patched here to hit
/// exactly the case the rule is about.
/// </summary>
public class FontRuleTests
{
    [Fact]
    public void A_component_with_a_two_by_two_transform_is_followed_by_the_next_component_eight_bytes_later()
    {
        // OpenType 'glyf', composite glyph description: WE_HAVE_A_TWO_BY_TWO (0x0080) adds four F2DOT14 values (8 bytes).
        // The first composite glyph of Lato with room for 30 bytes is rewritten as: component 3 ('A') with byte
        // arguments and the identity matrix, then component 173. Read with a 4-byte matrix, the second record would
        // start at the matrix's third value and name glyph 0x4000, which the font does not have.
        var bytes = TestFonts.Bytes("Lato-Regular.ttf");
        var font = FontParser.Parse(bytes).Value;
        var composite = Enumerable.Range(0, font.GlyphCount).Select(id => new GlyphId((ushort)id))
            .First(glyph => font.GlyphOutline(glyph) is { Length: >= 30 } data && data.Span[0] >= 0x80);
        MemoryMarshal.TryGetArray(font.GlyphOutline(composite), out var outline).Should().BeTrue();
        const ushort ArgsAreXyValues = 0x0002, MoreComponents = 0x0020, TwoByTwo = 0x0080;
        var records = new BigEndianBuilder()
            .UInt16(MoreComponents | TwoByTwo | ArgsAreXyValues).UInt16(3).Bytes([0, 0]).UInt16(0x4000).UInt16(0).UInt16(0).UInt16(0x4000)
            .UInt16(ArgsAreXyValues).UInt16(173).Bytes([0, 0])
            .ToArray();
        records.CopyTo(bytes, outline.Offset + 10);

        var subset = FontSubsetter.Subset(FontParser.Parse(bytes).Value, [composite]);

        subset.IsSuccess.Should().BeTrue(subset.IsFailure ? subset.Error.ToString() : string.Empty);
        subset.Value.Glyphs.Should().Equal(new[] { 0, 3, 173, composite.Value }.Order().Select(id => new GlyphId((ushort)id)));
    }

    [Fact]
    public void A_kern_subtable_of_another_format_than_0_is_not_read_as_pairs()
    {
        // OpenType 'kern': the high byte of coverage is the subtable format; only format 0 holds the pair list (the
        // fields nPairs, searchRange, entrySelector, rangeShift and kernPairs). This format-2 subtable has bytes that would read as
        // the pair (5, 7) → -40 if its format were ignored.
        var table = new BigEndianBuilder()
            .UInt16(0).UInt16(1)
            .UInt16(0).UInt16(14 + 6).UInt16(0x0201)
            .UInt16(1).UInt16(6).UInt16(0).UInt16(0).UInt16(5).UInt16(7).UInt16(unchecked((ushort)-40))
            .ToArray();

        var kerning = LegacyKerning.Parse(table).Value;

        kerning.Subtables.Should().BeEmpty();
        kerning.Adjustment(new GlyphId(5), new GlyphId(7)).Should().Be(0);
    }

    [Fact]
    public void A_US_English_name_wins_over_another_Windows_language_that_comes_first()
    {
        // OpenType 'name': Windows language ID 0x0409 is English (United States), the language a Windows font provides.
        var strings = new[] { (0x0413, 1, "Familie"), (0x0409, 1, "Family"), (0x0409, 6, "Family-Regular") };
        var storage = new List<byte>();
        var table = new BigEndianBuilder().UInt16(0).UInt16(strings.Length).UInt16(6 + strings.Length * 12);
        foreach (var (language, nameId, text) in strings)
        {
            var encoded = Encoding.BigEndianUnicode.GetBytes(text);
            table.UInt16(3).UInt16(1).UInt16(language).UInt16(nameId).UInt16(encoded.Length).UInt16(storage.Count);
            storage.AddRange(encoded);
        }

        var names = NameTable.Parse(table.Bytes(storage).ToArray()).Value;

        names.Family.Should().Be("Family");
        names.PostScriptName.Should().Be("Family-Regular");
    }
}
