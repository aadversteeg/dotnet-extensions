namespace UnitTests.Extensions.Documents.Core;

/// <summary>THE RULE FOR A LANGUAGE TAG (<see cref="Ave.Extensions.Documents.Core.LanguageTags"/>): BCP 47 well-formed, at
/// most 64 characters.</summary>
public class LanguageTagsTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("nl-NL")]
    [InlineData("NL-nl")]
    [InlineData("zh-Hant-TW")]
    [InlineData("sr-Latn-RS")]
    [InlineData("es-419")]
    [InlineData("de-CH-1996")]
    [InlineData("zh-yue-HK")]
    [InlineData("en-a-bbb-x-a-ccc")]
    [InlineData("x-private")]
    [InlineData("i-klingon")]
    [InlineData("en-GB-oed")]
    [InlineData("english")]
    public void A_well_formed_tag_is_accepted(string tag) => Ave.Extensions.Documents.Core.LanguageTags.IsWellFormed(tag).Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("e")]
    [InlineData("en_GB")]
    [InlineData("en-")]
    [InlineData("-en")]
    [InlineData("en--GB")]
    [InlineData("en-toolongsubtag")]
    [InlineData("en-x")]
    [InlineData("toolonglanguage")]
    [InlineData("en GB")]
    [InlineData("nl-NL\n")]
    [InlineData("en-a")]
    public void A_tag_that_is_not_well_formed_is_refused(string tag) => Ave.Extensions.Documents.Core.LanguageTags.IsWellFormed(tag).Should().BeFalse();

    [Fact]
    public void A_tag_longer_than_the_limit_is_refused_and_one_at_the_limit_accepted()
    {
        var atLimit = "x" + string.Concat(Enumerable.Repeat("-abcdefg", 7)) + "-abcdefg"[..(64 - 1 - 56)];

        atLimit.Length.Should().Be(Ave.Extensions.Documents.Core.LanguageTags.MaxLength);
        Ave.Extensions.Documents.Core.LanguageTags.IsWellFormed(atLimit).Should().BeTrue();
        Ave.Extensions.Documents.Core.LanguageTags.IsWellFormed(atLimit + "a").Should().BeFalse();
    }
}
