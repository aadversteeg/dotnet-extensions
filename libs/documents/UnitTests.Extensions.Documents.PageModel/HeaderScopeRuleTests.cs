using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.ErrorPaths;

namespace UnitTests.Extensions.Documents.PageModel;

/// <summary>The page-model rule for a header cell's scope: one of the three of <see cref="TableHeaderScope"/>, from any
/// producer.</summary>
public class HeaderScopeRuleTests
{
    private static readonly SourceLocation Here = new("HeaderScopeRuleTests.cs", 1);

    private static Ave.Extensions.Functional.Result<PageDocument, Error> Checked(TableHeaderScope scope) =>
        PageModelRules.Check(new PageDocument(new DocumentMetadata("Scopes", null), [new Page(new Size(100, 100),
            [
                new StructureStart(new NodeId(1), SemanticRole.Table, Here), new StructureStart(new NodeId(2), SemanticRole.TableRow, Here),
                new StructureStart(new NodeId(3), new SemanticRole.TableHeaderCellRole(scope), Here),
                new StructureEnd(new NodeId(3)), new StructureEnd(new NodeId(2)), new StructureEnd(new NodeId(1)),
            ])]), DocumentErrorCodes._ / "Test" / "Invalid");

    [Fact]
    public void A_scope_of_the_three_passes_and_any_other_is_a_breach()
    {
        Checked(TableHeaderScope.Column).IsSuccess.Should().BeTrue();
        Checked(TableHeaderScope.Row).IsSuccess.Should().BeTrue();
        Checked(TableHeaderScope.Both).IsSuccess.Should().BeTrue();
        Checked((TableHeaderScope)3).Error.Message.Should().Contain("Header cell #3 on page 1 has the scope 3");
    }

    [Fact]
    public void The_column_scope_is_the_plain_header_cell_and_the_others_say_their_scope_in_the_text_form()
    {
        SemanticRole.TableHeaderCellFor(TableHeaderScope.Column).Should().BeSameAs(SemanticRole.TableHeaderCell);
        SemanticRole.TableHeaderCell.ToString().Should().Be("table-header-cell");
        SemanticRole.TableHeaderCellFor(TableHeaderScope.Row).ToString().Should().Be("table-header-cell scope=row");
        SemanticRole.TableHeaderCellFor(TableHeaderScope.Both).ToString().Should().Be("table-header-cell scope=both");
        SemanticRole.TableHeaderCellFor(TableHeaderScope.Row).Should().NotBe(SemanticRole.TableHeaderCell);
    }
}
