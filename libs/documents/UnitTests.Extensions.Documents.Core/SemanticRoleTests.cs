using System.Reflection;
using Ave.Extensions.Documents.Core;

namespace UnitTests.Extensions.Documents.Core;

/// <summary>Every role is written as a standard PDF structure type (ISO 32000-1 §14.8.4), except decoration.</summary>
public class SemanticRoleTests
{
    /// <summary>One instance of every case of <see cref="SemanticRole"/>, found by reflection so a new case is never missed.</summary>
    public static TheoryData<SemanticRole> EveryRole()
    {
        var cases = typeof(SemanticRole).GetNestedTypes().Where(type => type.IsSubclassOf(typeof(SemanticRole))).ToList();
        var singletons = typeof(SemanticRole).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Select(property => (SemanticRole)property.GetValue(null)!).ToList();
        var all = singletons.Concat([SemanticRole.Figure("A chart"), SemanticRole.Heading(1), SemanticRole.Heading(6),
            SemanticRole.List(ListNumbering.Disc), SemanticRole.ListLabel("\u2022"), SemanticRole.ListLabel(null)]).ToList();
        cases.Should().OnlyContain(type => all.Any(role => role.GetType() == type), "every case has an instance in this list");
        return [.. all];
    }

    [Theory]
    [MemberData(nameof(EveryRole))]
    public void Every_role_but_decoration_names_a_standard_structure_type(SemanticRole role)
    {
        string[] standard = ["Document", "Div", "P", "H1", "H2", "H3", "H4", "H5", "H6", "Table", "THead", "TBody", "TR", "TH", "TD", "Figure", "L", "LI", "Lbl", "LBody"];

        if (role == SemanticRole.Decorative)
        {
            role.StandardStructureType.Should().BeNull();
        }
        else
        {
            role.StandardStructureType.Should().BeOneOf(standard);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void A_heading_level_outside_1_to_6_has_no_standard_structure_type(int level) =>
        SemanticRole.Heading(level).StandardStructureType.Should().BeNull();
}
