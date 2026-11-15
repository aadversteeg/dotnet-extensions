using System.IO;
using System.Reflection;
using System.Xml.Linq;
using Ave.Extensions.ErrorPaths;

namespace UnitTests.Ave.Documents.Architecture;

/// <summary>
/// The error-code tree: Core holds the root and the shared input branch; every library defines its own branch next to
/// the code that raises it. Every code of every library is listed here by reflection and must be unique, sit under
/// its class's branch, and be documented.
/// </summary>
public class ErrorCodeTests
{
    private static readonly string[] Libraries = ["Ave.Documents.Core", "Ave.Documents.Fonts", "Ave.Documents.Images", "Ave.Documents.Layout", "Ave.Documents.Pdf"];

    private sealed record Code(string Library, Type Owner, FieldInfo Field, string Path);

    private static IEnumerable<Type> Nested(Type type) => type.GetNestedTypes().SelectMany(Nested).Prepend(type);

    private static List<Code> AllCodes() =>
    [
        .. Libraries.SelectMany(library => Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, library + ".dll")).GetExportedTypes()
            .Where(type => type.IsClass && type.IsAbstract && type.IsSealed && type.DeclaringType is null && type.Name.EndsWith("ErrorCodes", StringComparison.Ordinal))
            .SelectMany(Nested)
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static).Where(field => field.FieldType == typeof(ErrorCode)))
            .Select(field => new Code(library, field.DeclaringType!, field, ((ErrorCode)field.GetValue(null)!).ToString()))),
    ];

    [Fact]
    public void Every_library_that_raises_errors_has_its_own_branch()
    {
        var codes = AllCodes();

        codes.Select(code => code.Library).Distinct().Should().BeEquivalentTo(Libraries);
        codes.Should().HaveCountGreaterThan(50);
    }

    [Fact]
    public void Every_code_is_unique()
    {
        var duplicates = AllCodes().GroupBy(code => code.Path).Where(group => group.Count() > 1).Select(group => group.Key);

        duplicates.Should().BeEmpty();
    }

    [Fact]
    public void Every_code_lies_under_the_branch_of_its_class()
    {
        foreach (var code in AllCodes().Where(code => code.Field.Name != "_"))
        {
            var branch = ((ErrorCode)code.Owner.GetField("_")!.GetValue(null)!).ToString();
            code.Path.Should().StartWith(branch + ".", $"{code.Owner.FullName}.{code.Field.Name}");
            code.Path.Should().StartWith("Documents.");
        }
    }

    [Fact]
    public void Every_code_is_documented()
    {
        foreach (var library in Libraries)
        {
            var documented = XDocument.Load(Path.Combine(AppContext.BaseDirectory, library + ".xml")).Descendants("member")
                .Where(member => !string.IsNullOrWhiteSpace(member.Element("summary")?.Value))
                .Select(member => (string)member.Attribute("name")!)
                .ToHashSet();
            foreach (var code in AllCodes().Where(code => code.Library == library))
            {
                documented.Should().Contain($"F:{code.Owner.FullName!.Replace('+', '.')}.{code.Field.Name}", code.Path);
            }
        }
    }
}
