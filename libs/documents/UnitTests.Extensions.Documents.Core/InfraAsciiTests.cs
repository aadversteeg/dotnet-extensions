using Ave.Extensions.Documents.Core;

namespace UnitTests.Extensions.Documents.Core;

/// <summary>The ASCII rules of the Infra Standard and the encoding labels of the Encoding Standard, which the HTML parser
/// and the CSS reader share from Core.</summary>
public class InfraAsciiTests
{
    [Fact]
    public void Only_ascii_letters_have_a_case()
    {
        InfraAscii.ToLower("AbC-İ-Ä").Should().Be("abc-İ-Ä");
        InfraAscii.EqualsIgnoreCase("TEXT/css", "text/CSS").Should().BeTrue();
        InfraAscii.EqualsIgnoreCase("ı", "I").Should().BeFalse();
        InfraAscii.StartsWithIgnoreCase("-WEBKIT-box", "-webkit-").Should().BeTrue();
        InfraAscii.IsWhitespace('\f').Should().BeTrue();
        InfraAscii.IsWhitespace('\v').Should().BeFalse();
    }

    [Theory]
    [InlineData(" UTF8\t", "UTF-8")]
    [InlineData("latin1", "windows-1252")]
    [InlineData("unicode-1-1-utf-8", "UTF-8")]
    [InlineData("no-such-label", null)]
    [InlineData(null, null)]
    public void A_label_names_an_encoding_or_nothing(string? label, string? encoding) =>
        EncodingLabels.Resolve(label).Should().Be(encoding);
}
