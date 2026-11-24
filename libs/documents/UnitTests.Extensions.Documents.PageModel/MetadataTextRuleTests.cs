using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.ErrorPaths;

namespace UnitTests.Extensions.Documents.PageModel;

/// <summary>The page-model rule for the document's title, author and description: THE RULE FOR A DOCUMENT'S METADATA TEXT
/// (<see cref="MetadataTexts"/>), from any producer.</summary>
public class MetadataTextRuleTests
{
    private static Ave.Extensions.Functional.Result<PageDocument, Error> Checked(DocumentMetadata metadata) =>
        PageModelRules.Check(new PageDocument(metadata, [new Page(new Size(100, 100), [])]), DocumentErrorCodes._ / "Test" / "Invalid");

    [Fact]
    public void A_title_an_author_or_a_description_the_rule_does_not_accept_is_a_breach()
    {
        Checked(new DocumentMetadata("Hours", "Ann") { Description = "A week." }).IsSuccess.Should().BeTrue();
        Checked(new DocumentMetadata("Hours\u0001", null)).Error.Message.Should().Contain("title is refused: it holds U+0001 at position 5");
        Checked(new DocumentMetadata(null, "Ann\u009F")).Error.Message.Should().Contain("author is refused: it holds U+009F at position 3");
        Checked(new DocumentMetadata(null, null) { Description = new string('a', 4_097) }).Error.Message.Should().Contain("description is refused: it is 4097 characters long");
    }
}
