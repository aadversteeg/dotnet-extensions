using Ave.Extensions.Documents.Core;

namespace UnitTests.Extensions.Documents.Core;

/// <summary>
/// The characters text may hold, in one rule (<see cref="TextCharacters"/>): no control character but tab and the forced
/// line breaks, no noncharacter, and no half of a surrogate pair standing alone. The front-end applies it to every text;
/// these tests pin the rule itself, where a later stage (the shaper) cannot answer for it.
/// </summary>
public class TextCharactersTests
{
    [Fact]
    public void Half_of_a_surrogate_pair_alone_is_forbidden()
    {
        // Built in the test, not as theory data: xUnit replaces a lone surrogate in a test case's arguments.
        const char High = (char)0xD800, Low = (char)0xDC00;
        var cases = new (string Text, int Offset)[] { ($"a{High}b", 1), ($"a{Low}b", 1), ($"ab{High}", 2), ($"{Low}{High}", 0) };

        foreach (var (text, offset) in cases)
        {
            TextCharacters.IndexOfForbidden(text).Should().Be(offset, $"case at offset {offset}");
        }
    }

    [Fact]
    public void A_surrogate_pair_is_one_character_and_allowed() =>
        TextCharacters.IndexOfForbidden("a" + char.ConvertFromUtf32(0x1F600) + "b").Should().BeNull();
}
