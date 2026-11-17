using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Fonts;

/// <summary>
/// Property tests: a damaged font file must give an error value, never an exception. Each table parser checks
/// its lengths before it reads, and these tests look for a check that is missing.
/// </summary>
public class MalformedFontTests
{
    private static readonly byte[] Lato = TestFonts.Bytes("Lato-Regular.ttf");

    [Fact]
    public void Every_truncation_gives_a_font_error_and_never_throws()
    {
        var random = new Random(20261115);
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var length = random.Next(0, Lato.Length);

            var result = FontParser.Parse(Lato.AsMemory(0, length));

            result.IsFailure.Should().BeTrue($"a font cut at {length} bytes is incomplete");
            result.Error.Is(FontErrorCodes.Parse._).Should().BeTrue();
        }
    }

    [Fact]
    public void Corrupted_bytes_in_the_directory_and_table_headers_never_throw()
    {
        // The table directory and the first bytes of every table hold the counts, offsets and lengths that the
        // parsers trust; that is where a missing check would turn into an out-of-range read.
        var random = new Random(4);
        var positions = Enumerable.Range(0, 12 + 17 * 16).Concat(TableHeaderPositions());
        foreach (var position in positions)
        {
            var copy = (byte[])Lato.Clone();
            copy[position] = (byte)random.Next(256);

            var act = () => FontParser.Parse(copy);

            act.Should().NotThrow($"byte {position} was changed");
        }
    }

    private static IEnumerable<int> TableHeaderPositions()
    {
        int tableCount = (Lato[4] << 8) | Lato[5];
        for (var index = 0; index < tableCount; index++)
        {
            var record = 12 + index * 16;
            var offset = (Lato[record + 8] << 24) | (Lato[record + 9] << 16) | (Lato[record + 10] << 8) | Lato[record + 11];
            for (var position = offset; position < offset + 48 && position < Lato.Length; position++)
            {
                yield return position;
            }
        }
    }
}
