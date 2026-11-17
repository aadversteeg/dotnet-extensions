using System.Text;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.Fonts.Tables;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Fonts;

/// <summary>
/// The name table decodes at most one string per name ID the engine uses, so records that point at the same bytes
/// cannot multiply the memory a font takes: 500 records of 60 000 bytes used to decode 30 MB of strings from a 66 KB
/// table. Memory is counted (bytes allocated), not timed.
/// </summary>
public class NameTableTests
{
    private const int WindowsPlatform = 3;
    private const int UnicodeBmp = 1;
    private const int EnglishUnitedStates = 0x0409;

    /// <summary>A name table with a family name, a PostScript name, and <paramref name="copies"/> records of name ID
    /// <paramref name="nameId"/> that all point at one block of 60 000 bytes.</summary>
    private static byte[] Table(int copies, int nameId)
    {
        var family = Encoding.BigEndianUnicode.GetBytes("Family");
        var postScript = Encoding.BigEndianUnicode.GetBytes("Family-Regular");
        var count = 2 + copies;
        var builder = new BigEndianBuilder().UInt16(0).UInt16(count).UInt16(6 + count * 12);
        void Record(int id, int length, int offset) =>
            builder.UInt16(WindowsPlatform).UInt16(UnicodeBmp).UInt16(EnglishUnitedStates).UInt16(id).UInt16(length).UInt16(offset);
        Record(1, family.Length, 0);
        Record(6, postScript.Length, family.Length);
        for (var index = 0; index < copies; index++)
        {
            Record(nameId, 60000, family.Length + postScript.Length);
        }

        return builder.Bytes(family).Bytes(postScript).Bytes(Enumerable.Repeat((byte)'a', 60000)).ToArray();
    }

    private static long AllocatedWhileParsing(byte[] table, out FontNames names)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = NameTable.Parse(table);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        names = result.Value;
        return allocated;
    }

    [Fact]
    public void Records_of_a_name_the_engine_does_not_use_are_not_decoded()
    {
        var allocated = AllocatedWhileParsing(Table(500, nameId: 300), out var names);

        names.PostScriptName.Should().Be("Family-Regular");
        allocated.Should().BeLessThan(2L * 1024 * 1024, "the 500 unused records are skipped; the table itself is 66 KB");
    }

    [Fact]
    public void Of_many_records_of_one_used_name_only_the_chosen_one_is_decoded()
    {
        var allocated = AllocatedWhileParsing(Table(500, nameId: 4), out var names);

        names.FullName.Should().HaveLength(30000);
        allocated.Should().BeLessThan(2L * 1024 * 1024, "one full name of 60 000 bytes is decoded, not 500");
    }
}
