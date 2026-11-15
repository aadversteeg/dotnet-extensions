using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace UnitTests.Ave.Documents.Architecture;

/// <summary>
/// Rules checked on the compiled assemblies, so they cannot be broken by accident:
/// (1) pure managed code: no P/Invoke, no native imports, IL only, no unsafe code (Aad's rule 1, brief §1);
/// (2) layering: each layer references only the layers below it (brief §3; architecture "v1 CONSOLIDATED").
/// </summary>
public class AssemblyRulesTests
{
    /// <summary>The allowed engine references per library: the layer diagram in code.</summary>
    private static readonly Dictionary<string, string[]> AllowedReferences = new()
    {
        ["Ave.Documents.Core"] = [],
        ["Ave.Documents.Fonts"] = ["Core"],
        ["Ave.Documents.Pdf"] = ["Core"],
        ["Ave.Documents.Text"] = ["Core", "Fonts"],
        ["Ave.Documents.Model"] = ["Core"],
        ["Ave.Documents.Page"] = ["Core", "Fonts"],
        ["Ave.Documents.Layout"] = ["Core", "Fonts", "Text", "Model", "Page"],
        ["Ave.Documents.Render.Pdf"] = ["Core", "Fonts", "Pdf", "Page"],
        ["Ave.Documents.Fluent"] = ["Core", "Fonts", "Model", "Page", "Layout", "Render.Pdf"],
    };

    public static TheoryData<string> Libraries => [.. AllowedReferences.Keys];

    private static string PathOf(string assembly) => Path.Combine(AppContext.BaseDirectory, assembly + ".dll");

    [Fact]
    public void Every_library_is_checked()
    {
        var built = Directory.GetFiles(AppContext.BaseDirectory, "Ave.Documents.*.dll").Select(Path.GetFileNameWithoutExtension);

        built.Where(name => name != "Ave.Documents.TestSupport").Should().BeEquivalentTo(AllowedReferences.Keys);
    }

    [Theory]
    [MemberData(nameof(Libraries))]
    public void Library_is_pure_managed_code(string assembly) =>
        NativeCodeFindings(PathOf(assembly)).Should().BeEmpty();

    [Fact]
    public void The_scan_finds_native_code_where_there_is_some()
    {
        // Positive control: the runtime's core library calls into the runtime through P/Invoke, so a scan that
        // reports nothing for it would be a scan that does not work.
        var findings = NativeCodeFindings(typeof(object).Assembly.Location);

        findings.Should().Contain(finding => finding.StartsWith("P/Invoke", StringComparison.Ordinal));
        findings.Should().Contain(finding => finding.StartsWith("module references", StringComparison.Ordinal));
    }

    /// <summary>
    /// Everything in an assembly that means native code: a P/Invoke method (MethodAttributes.PinvokeImpl), a
    /// module reference (how metadata names a native library, ECMA-335 §II.22.31), a PE image that is not IL
    /// only (mixed-mode C++/CLI), or the UnverifiableCode module attribute the C# compiler adds for unsafe code.
    /// </summary>
    private static List<string> NativeCodeFindings(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var findings = new List<string>();

        if (!pe.PEHeaders.CorHeader!.Flags.HasFlag(CorFlags.ILOnly))
        {
            findings.Add("not IL only");
        }

        var moduleReferences = metadata.GetTableRowCount(TableIndex.ModuleRef);
        if (moduleReferences > 0)
        {
            findings.Add($"module references: {moduleReferences}");
        }

        findings.AddRange(metadata.MethodDefinitions
            .Select(handle => metadata.GetMethodDefinition(handle))
            .Where(method => (method.Attributes & MethodAttributes.PinvokeImpl) != 0)
            .Select(method => $"P/Invoke: {metadata.GetString(method.Name)}"));

        findings.AddRange(metadata.GetModuleDefinition().GetCustomAttributes()
            .Select(handle => AttributeName(metadata, metadata.GetCustomAttribute(handle)))
            .Where(name => name == "UnverifiableCodeAttribute")
            .Select(_ => "unsafe code (UnverifiableCodeAttribute)"));

        return findings;
    }

    [Theory]
    [MemberData(nameof(Libraries))]
    public void Library_references_only_lower_layers_and_the_allowed_packages(string assembly)
    {
        using var stream = File.OpenRead(PathOf(assembly));
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var references = metadata.AssemblyReferences
            .Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))
            .ToList();

        references.Where(name => name.StartsWith("Ave.Documents.", StringComparison.Ordinal))
            .Select(name => name["Ave.Documents.".Length..])
            .Should().BeSubsetOf(AllowedReferences[assembly]);
        references.Where(name => !name.StartsWith("Ave.Documents.", StringComparison.Ordinal))
            .Should().OnlyContain(name => name == "System.Runtime" || name.StartsWith("System.", StringComparison.Ordinal)
                || name.StartsWith("Ave.Extensions.Functional", StringComparison.Ordinal) || name.StartsWith("Ave.Extensions.ErrorPaths", StringComparison.Ordinal));
    }

    private static string AttributeName(MetadataReader metadata, CustomAttribute attribute) =>
        attribute.Constructor.Kind == HandleKind.MemberReference
            ? metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent is { Kind: HandleKind.TypeReference } parent
                ? metadata.GetString(metadata.GetTypeReference((TypeReferenceHandle)parent).Name)
                : string.Empty
            : string.Empty;
}
