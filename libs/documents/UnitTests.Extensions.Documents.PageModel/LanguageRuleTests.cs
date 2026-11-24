using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.ErrorPaths;

namespace UnitTests.Extensions.Documents.PageModel;

/// <summary>The page-model rule for a structure marker's language: a well-formed tag (<see cref="LanguageTags"/>), from
/// any producer.</summary>
public class LanguageRuleTests
{
    private static readonly SourceLocation Here = new("LanguageRuleTests.cs", 1);

    private static Ave.Extensions.Functional.Result<PageDocument, Error> Checked(string language) =>
        PageModelRules.Check(new PageDocument(new DocumentMetadata("Languages", null), [new Page(new Size(100, 100),
            [new StructureStart(new NodeId(1), SemanticRole.Paragraph, Here) { Language = language }, new StructureEnd(new NodeId(1))])]), DocumentErrorCodes._ / "Test" / "Invalid");

    [Fact]
    public void A_well_formed_language_passes_and_any_other_is_a_breach()
    {
        Checked("fr-CA").IsSuccess.Should().BeTrue();
        Checked("fr CA").Error.Message.Should().Contain("not a well-formed language tag");
        Checked(string.Empty).Error.Message.Should().Contain("not a well-formed language tag");
    }
}
