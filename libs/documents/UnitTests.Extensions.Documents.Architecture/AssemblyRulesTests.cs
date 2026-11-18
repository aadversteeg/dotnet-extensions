using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace UnitTests.Extensions.Documents.Architecture;

/// <summary>
/// Rules checked on the compiled assemblies, so they cannot be broken by accident:
/// (1) pure managed code: no P/Invoke, no native imports, IL only, no unsafe code;
/// (2) layering: each layer references only the layers below it (the layer list in docs/architecture.md).
/// </summary>
public class AssemblyRulesTests
{
    /// <summary>The allowed engine references per library: the layer diagram in code.</summary>
    private static readonly Dictionary<string, string[]> AllowedReferences = new()
    {
        ["Ave.Extensions.Documents.Core"] = [],
        ["Ave.Extensions.Documents.Fonts"] = ["Core"],
        ["Ave.Extensions.Documents.Images"] = ["Core"],
        ["Ave.Extensions.Documents.Pdf"] = ["Core"],
        ["Ave.Extensions.Documents.Typesetting"] = ["Core", "Fonts"],
        ["Ave.Extensions.Documents.DocumentModel"] = ["Core", "Images"],
        ["Ave.Extensions.Documents.PageModel"] = ["Core", "Fonts", "Images"],
        ["Ave.Extensions.Documents.Layout"] = ["Core", "Fonts", "Images", "Typesetting", "DocumentModel", "PageModel"],
        ["Ave.Extensions.Documents.Render.Pdf"] = ["Core", "Fonts", "Images", "Pdf", "PageModel"],
        ["Ave.Extensions.Documents.Fluent"] = ["Core", "Fonts", "Images", "DocumentModel"],
        ["Ave.Extensions.Documents.Html.Parsing"] = ["Core"],
        ["Ave.Extensions.Documents.Css"] = ["Core"],
        ["Ave.Extensions.Documents.Html"] = ["Core", "Html.Parsing", "Css"],

        // The composition package: the one place a front-end, the layout and a back-end meet.
        ["Ave.Extensions.Documents"] = ["Core", "Fonts", "Images", "DocumentModel", "PageModel", "Typesetting", "Pdf", "Layout", "Render.Pdf", "Fluent"],
    };

    /// <summary>The names of the product libraries (the layer list), for the rules on other things than assemblies.</summary>
    internal static IReadOnlyCollection<string> LibraryNames => AllowedReferences.Keys;

    /// <summary>The engine libraries a library may depend on, by full name.</summary>
    /// <param name="library">The library.</param>
    /// <returns>The allowed names.</returns>
    internal static IEnumerable<string> AllowedFor(string library) => AllowedReferences[library].Select(name => "Ave.Extensions.Documents." + name);

    public static TheoryData<string> Libraries => [.. AllowedReferences.Keys];

    private static string PathOf(string assembly) => Path.Combine(AppContext.BaseDirectory, assembly + ".dll");

    [Fact]
    public void Every_library_is_checked()
    {
        var built = Directory.GetFiles(AppContext.BaseDirectory, "Ave.Extensions.Documents.*.dll").Append(PathOf("Ave.Extensions.Documents")).Select(Path.GetFileNameWithoutExtension);

        built.Where(name => name != "Ave.Extensions.Documents.TestSupport").Should().BeEquivalentTo(AllowedReferences.Keys);
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

    [Fact]
    public void The_front_end_references_neither_the_layout_nor_a_back_end()
    {
        // A second back-end or a second front-end is added beside the others, joined in the composition package
        // (Ave.Extensions.Documents), never by widening the front-end. Read from the compiled front-end itself.
        using var stream = File.OpenRead(PathOf("Ave.Extensions.Documents.Fluent"));
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var references = metadata.AssemblyReferences.Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name)).ToList();

        references.Should().Contain("Ave.Extensions.Documents.DocumentModel", "the scan reads real references");
        references.Should().NotContain(["Ave.Extensions.Documents.Layout", "Ave.Extensions.Documents.PageModel", "Ave.Extensions.Documents.Pdf", "Ave.Extensions.Documents.Render.Pdf", "Ave.Extensions.Documents.Typesetting"]);
    }

    /// <summary>
    /// No friend assemblies between product packages: the packages ship separately, so an internal member one of them
    /// used from another would be an unversioned contract. A library opens its internals to its own test project only;
    /// what another library needs is public, documented API.
    /// </summary>
    [Theory]
    [MemberData(nameof(Libraries))]
    public void Library_shows_its_internals_to_test_projects_only(string assembly)
    {
        var friends = Assembly.LoadFrom(PathOf(assembly)).GetCustomAttributes<System.Runtime.CompilerServices.InternalsVisibleToAttribute>()
            .Select(attribute => attribute.AssemblyName)
            .ToList();

        friends.Where(name => !name.StartsWith("UnitTests.", StringComparison.Ordinal)).Should().BeEmpty();
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

        references.Should().NotContain("Ave.Extensions.Documents", "only applications reference the composition package");
        references.Where(name => name.StartsWith("Ave.Extensions.Documents.", StringComparison.Ordinal))
            .Select(name => name["Ave.Extensions.Documents.".Length..])
            .Should().BeSubsetOf(AllowedReferences[assembly]);
        references.Where(name => !name.StartsWith("Ave.Extensions.Documents.", StringComparison.Ordinal))
            .Should().OnlyContain(name => name == "System.Runtime" || name.StartsWith("System.", StringComparison.Ordinal)
                || name.StartsWith("Ave.Extensions.Functional", StringComparison.Ordinal) || name.StartsWith("Ave.Extensions.ErrorPaths", StringComparison.Ordinal));
    }

    /// <summary>Packages that may only ever be used by tests: the PDF reader and the WebAssembly runtime that runs
    /// the reference shaper (reference engines are allowed in tests only).</summary>
    private static readonly string[] TestOnlyAssemblies = ["UglyToad.PdfPig", "WebAssembly"];

    [Theory]
    [MemberData(nameof(Libraries))]
    public void Library_does_not_reference_a_test_only_package(string assembly)
    {
        using var stream = File.OpenRead(PathOf(assembly));
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var references = metadata.AssemblyReferences.Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name));

        references.Should().NotContain(name => TestOnlyAssemblies.Any(testOnly => name.StartsWith(testOnly, StringComparison.Ordinal)));
    }

    [Fact]
    public void The_test_only_packages_are_really_used_by_the_tests()
    {
        // Positive control for the test above: the reference shaper's test assembly does reference WebAssembly.
        // Same configuration and framework folders, next project: UnitTests.Extensions.Documents.Typesetting/bin/<config>/net10.0.
        var testAssembly = Path.Combine(
            AppContext.BaseDirectory.Replace("UnitTests.Extensions.Documents.Architecture", "UnitTests.Extensions.Documents.Typesetting", StringComparison.Ordinal),
            "UnitTests.Extensions.Documents.Typesetting.dll");
        using var stream = File.OpenRead(testAssembly);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();

        metadata.AssemblyReferences.Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name)).Should().Contain("WebAssembly");
    }

    [Fact]
    public void No_webassembly_module_is_copied_next_to_the_libraries()
    {
        Directory.GetFiles(AppContext.BaseDirectory, "*.wasm", SearchOption.AllDirectories).Should().BeEmpty();
    }

    private static string AttributeName(MetadataReader metadata, CustomAttribute attribute) =>
        attribute.Constructor.Kind == HandleKind.MemberReference
            ? metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent is { Kind: HandleKind.TypeReference } parent
                ? metadata.GetString(metadata.GetTypeReference((TypeReferenceHandle)parent).Name)
                : string.Empty
            : string.Empty;
}
