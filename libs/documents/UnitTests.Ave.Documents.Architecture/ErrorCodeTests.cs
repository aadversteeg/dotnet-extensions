using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;
using Ave.Extensions.ErrorPaths;

namespace UnitTests.Ave.Documents.Architecture;

/// <summary>
/// The error-code tree: Core holds the root and the shared input branch; every library defines its own codes next to
/// the code that raises them. The libraries are the engine assemblies the build put next to these tests (found, not
/// listed); every code of every library is read by reflection and must be unique, sit under its class's branch, and be
/// documented; and the metadata of every library is read to check that it raises only its own codes and Core's.
/// </summary>
public class ErrorCodeTests
{
    private const string CoreLibrary = "Ave.Documents.Core";

    /// <summary>Every engine assembly in the test output, except the test support library.</summary>
    private static IEnumerable<string> Libraries =>
        Directory.GetFiles(AppContext.BaseDirectory, "Ave.Documents*.dll")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .Where(name => name != "Ave.Documents.TestSupport")
            .Order(StringComparer.Ordinal);

    internal sealed record Code(string Library, Type Owner, FieldInfo Field, string Path)
    {
        public bool IsBranch => Field.Name == "_";
    }

    private static IEnumerable<Type> Nested(Type type) => type.GetNestedTypes().SelectMany(Nested).Prepend(type);

    private static Assembly Load(string library) => Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, library + ".dll"));

    /// <summary>Every public static ErrorCode field of a library, wherever it is declared.</summary>
    private static List<Code> CodesOf(string library) =>
    [
        .. Load(library).GetExportedTypes()
            .Where(type => type.DeclaringType is null)
            .SelectMany(Nested)
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly).Where(field => field.FieldType == typeof(ErrorCode)))
            .Select(field => new Code(library, field.DeclaringType!, field, ((ErrorCode)field.GetValue(null)!).ToString())),
    ];

    internal static List<Code> AllCodes() => [.. Libraries.SelectMany(CodesOf)];

    [Fact]
    public void The_libraries_are_found_in_the_build_output()
    {
        Libraries.Should().Contain([CoreLibrary, "Ave.Documents.Fonts", "Ave.Documents.Render.Pdf", "Ave.Documents"]);
        AllCodes().Should().HaveCountGreaterThan(50);
    }

    [Fact]
    public void Every_code_is_declared_in_an_error_codes_class()
    {
        AllCodes().Should().OnlyContain(code => TopLevel(code.Owner).Name.EndsWith("ErrorCodes", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_code_is_unique()
    {
        // A branch (field "_") may be shared: the PDF writer and the PDF back-end both add codes under Documents.Pdf.
        var duplicates = AllCodes().Where(code => !code.IsBranch).GroupBy(code => code.Path).Where(group => group.Count() > 1).Select(group => group.Key);

        duplicates.Should().BeEmpty();
    }

    [Fact]
    public void Every_code_lies_under_the_branch_of_its_class()
    {
        foreach (var code in AllCodes().Where(code => !code.IsBranch))
        {
            var branch = ((ErrorCode)code.Owner.GetField("_")!.GetValue(null)!).ToString();
            code.Path.Should().StartWith(branch + ".", $"{code.Owner.FullName}.{code.Field.Name}");
            code.Path.Should().StartWith("Documents.");
        }
    }

    [Fact]
    public void Every_code_is_documented()
    {
        foreach (var library in AllCodes().Select(code => code.Library).Distinct())
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

    /// <summary>
    /// A library raises a code by loading its field; a field of another assembly appears in the library's metadata as a
    /// member reference whose parent is a type reference into that assembly. So the codes a library uses from elsewhere
    /// are its member references to fields of an <c>…ErrorCodes</c> type. Of the engine's assemblies only Core's are allowed:
    /// they are the shared root and input branch. (The error library's own Aggregate code is not the engine's.)
    /// </summary>
    [Fact]
    public void Every_library_raises_only_its_own_codes_and_Core_s()
    {
        var foreign = Libraries.SelectMany(library => ForeignErrorCodeFields(library).Select(field => $"{library} uses {field}")).ToList();

        foreign.Should().BeEmpty();
    }

    [Fact]
    public void The_foreign_code_scan_sees_a_use_of_Core_s_codes()
    {
        // Positive control: the front-end raises Core's input codes, so the scan must see them as references to Core.
        UsedErrorCodeFields("Ave.Documents.Fluent").Should().Contain(field => field.StartsWith(CoreLibrary + ": DocumentErrorCodes", StringComparison.Ordinal));
    }

    private static IEnumerable<string> ForeignErrorCodeFields(string library) =>
        UsedErrorCodeFields(library).Where(field => field.StartsWith("Ave.Documents", StringComparison.Ordinal) && !field.StartsWith(CoreLibrary + ":", StringComparison.Ordinal));

    /// <summary>"assembly: Type.Field" for every field of an <c>…ErrorCodes</c> type in another assembly that the
    /// library references.</summary>
    private static List<string> UsedErrorCodeFields(string library)
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, library + ".dll"));
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var found = new List<string>();
        foreach (var handle in metadata.MemberReferences)
        {
            var member = metadata.GetMemberReference(handle);
            if (member.GetKind() != MemberReferenceKind.Field || member.Parent.Kind != HandleKind.TypeReference)
            {
                continue;
            }

            // A branch field "_" is not raised: another library's branch is where this one adds its own codes.
            var (assembly, type) = Owner(metadata, (TypeReferenceHandle)member.Parent);
            if (type.Split('.')[0].EndsWith("ErrorCodes", StringComparison.Ordinal) && metadata.GetString(member.Name) != "_")
            {
                found.Add($"{assembly}: {type}.{metadata.GetString(member.Name)}");
            }
        }

        return found;
    }

    /// <summary>The assembly and the dotted name (outer types first) of a type reference.</summary>
    private static (string Assembly, string Type) Owner(MetadataReader metadata, TypeReferenceHandle handle)
    {
        var reference = metadata.GetTypeReference(handle);
        var name = metadata.GetString(reference.Name);
        if (reference.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            var (assembly, outer) = Owner(metadata, (TypeReferenceHandle)reference.ResolutionScope);
            return (assembly, $"{outer}.{name}");
        }

        return reference.ResolutionScope.Kind == HandleKind.AssemblyReference
            ? (metadata.GetString(metadata.GetAssemblyReference((AssemblyReferenceHandle)reference.ResolutionScope).Name), name)
            : (string.Empty, name);
    }

    private static Type TopLevel(Type type) => type.DeclaringType is null ? type : TopLevel(type.DeclaringType);
}
