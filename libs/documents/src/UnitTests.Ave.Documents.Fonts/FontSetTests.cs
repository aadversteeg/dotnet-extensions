using Ave.Documents.Core;
using Ave.Documents.TestSupport;

namespace UnitTests.Ave.Documents.Fonts;

public class FontSetTests
{
    [Fact]
    public void Finds_a_font_by_family_and_weight()
    {
        TestFonts.Set.Find("Lato", FontWeight.Regular).Value.Should().Be(TestFonts.Regular);
        TestFonts.Set.Find("Lato", FontWeight.Bold).Value.Should().Be(TestFonts.Bold);
    }

    [Fact]
    public void Family_names_match_case_insensitively() =>
        TestFonts.Set.Find("LATO", FontWeight.Bold).Value.Should().Be(TestFonts.Bold);

    [Fact]
    public void A_weight_that_is_not_in_the_set_is_not_found_rather_than_substituted()
    {
        var error = TestFonts.Set.Find("Lato", new FontWeight(500)).Error;

        error.Code.Should().Be(DocumentErrorCodes.Font.NotFound);
        error.Message.Should().Contain("Available: Lato 400, Lato 700");
    }
}
