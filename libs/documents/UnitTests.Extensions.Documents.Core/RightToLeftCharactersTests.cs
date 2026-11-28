using Ave.Extensions.Documents.Core;

namespace UnitTests.Extensions.Documents.Core;

/// <summary>
/// The characters that ask for right-to-left text, in one rule (<see cref="RightToLeftCharacters"/>): Bidi_Class R, AL and AN,
/// and the controls RLE, RLO and RLI. Both front-ends refuse them; these tests pin the rule itself.
/// </summary>
public class RightToLeftCharactersTests
{
    /// <summary>The twelve explicit formatting characters and marks of UAX #9 §2: five ask for right-to-left text, seven
    /// leave Chromium's print as it is without them (cases <c>bidi-lrm</c>, <c>bidi-lre</c>, <c>bidi-lri</c>, <c>bidi-fsi</c>).</summary>
    [Theory]
    [InlineData(0x202A, false)]
    [InlineData(0x202B, true)]
    [InlineData(0x202C, false)]
    [InlineData(0x202D, false)]
    [InlineData(0x202E, true)]
    [InlineData(0x2066, false)]
    [InlineData(0x2067, true)]
    [InlineData(0x2068, false)]
    [InlineData(0x2069, false)]
    [InlineData(0x200E, false)]
    [InlineData(0x200F, true)]
    [InlineData(0x061C, true)]
    public void Of_the_formatting_characters_only_the_right_to_left_ones_ask_for_right_to_left_text(int codePoint, bool asks)
    {
        RightToLeftCharacters.Is(codePoint).Should().Be(asks);
        RightToLeftCharacters.Find("ab" + char.ConvertFromUtf32(codePoint) + "c").Should().Be(asks ? (2, codePoint, 1) : null);
    }

    [Theory]
    [InlineData(0x05D0)] // HEBREW LETTER ALEF, R
    [InlineData(0x05BE)] // HEBREW PUNCTUATION MAQAF, R
    [InlineData(0xFB1D)] // HEBREW LETTER YOD WITH HIRIQ, R
    [InlineData(0x0627)] // ARABIC LETTER ALEF, AL
    [InlineData(0x0710)] // SYRIAC LETTER ALAPH, AL
    [InlineData(0xFEFC)] // ARABIC LIGATURE LAM WITH ALEF FINAL FORM, AL
    [InlineData(0x0660)] // ARABIC-INDIC DIGIT ZERO, AN
    [InlineData(0x0669)] // ARABIC-INDIC DIGIT NINE, AN
    [InlineData(0x0600)] // ARABIC NUMBER SIGN, AN
    [InlineData(0x10800)] // CYPRIOT SYLLABLE A, R, outside the Basic Multilingual Plane
    [InlineData(0x1E900)] // ADLAM CAPITAL LETTER ALIF, R
    public void A_letter_or_digit_of_a_right_to_left_class_asks_for_right_to_left_text(int codePoint) =>
        RightToLeftCharacters.Is(codePoint).Should().BeTrue();

    [Theory]
    [InlineData(0x0041)] // L
    [InlineData(0x0030)] // EN
    [InlineData(0x06F0)] // EXTENDED ARABIC-INDIC DIGIT ZERO, EN
    [InlineData(0x0021)] // ON
    [InlineData(0x0591)] // HEBREW ACCENT ETNAHTA, NSM
    [InlineData(0x05FF)] // unassigned in the Hebrew block: no entry in UnicodeData.txt
    [InlineData(0x10FFFF)]
    public void Any_other_code_point_does_not(int codePoint) =>
        RightToLeftCharacters.Is(codePoint).Should().BeFalse();

    [Fact]
    public void Find_gives_the_first_such_character_and_how_many_the_text_holds()
    {
        RightToLeftCharacters.Find("plain text 123").Should().BeNull();
        RightToLeftCharacters.Find(string.Empty).Should().BeNull();
        RightToLeftCharacters.Find("a\U0001F600b\u05D0c\u0660").Should().Be((4, 0x05D0, 2));
        RightToLeftCharacters.Find("\U00010800x").Should().Be((0, 0x10800, 1));
    }

    [Fact]
    public void Half_of_a_surrogate_pair_alone_is_not_such_a_character()
    {
        // Built in the test: xUnit replaces a lone surrogate in a test case's arguments.
        RightToLeftCharacters.Find($"a{(char)0xD802}b{(char)0xDC00}").Should().BeNull();
    }

    [Fact]
    public void The_message_names_the_character_says_left_to_right_only_and_what_to_do()
    {
        RightToLeftCharacters.Message(0x202E).Should().Be(
            "The character U+202E, the RIGHT-TO-LEFT OVERRIDE, asks for right-to-left text, and the engine lays out left to right only: remove it.");
        RightToLeftCharacters.Message(0x05D0, 3).Should().Be(
            "The character U+05D0, a right-to-left character (Bidi_Class R, such as a Hebrew letter), asks for right-to-left text " +
            "(3 such characters in this text), and the engine lays out left to right only: remove it.");
        RightToLeftCharacters.Message(0x0627).Should().Contain("Bidi_Class AL");
        RightToLeftCharacters.Message(0x0660).Should().Contain("Bidi_Class AN");
        RightToLeftCharacters.Message(0x200F).Should().Contain("the RIGHT-TO-LEFT MARK");
        RightToLeftCharacters.Message(0x061C).Should().Contain("the ARABIC LETTER MARK");
        RightToLeftCharacters.Message(0x202B).Should().Contain("the RIGHT-TO-LEFT EMBEDDING");
        RightToLeftCharacters.Message(0x2067).Should().Contain("the RIGHT-TO-LEFT ISOLATE");
    }
}
