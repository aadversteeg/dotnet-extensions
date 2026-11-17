using System.IO;
using System.Text.Json;
using System.Xml.Linq;

namespace UnitTests.Extensions.Documents.Architecture;

/// <summary>
/// Rules for what the packages bring with them. A package's dependencies are written by <c>dotnet pack</c> from the
/// restore graph in the project's <c>obj/project.assets.json</c>: every project reference becomes a dependency on that
/// project's package at its version, and every package reference that is not private (<c>PrivateAssets="all"</c>)
/// becomes a package dependency. So the rules are checked on that file, the evaluated input of pack, rather than on the
/// project files, which do not show what Directory.Build.props and the SDK add.
/// </summary>
public class PackageRulesTests
{
    /// <summary>The only packages outside this repository a product package may depend on: errors as values.</summary>
    private static readonly string[] AllowedPackages =
    [
        "Ave.Extensions.Functional",
        "Ave.Extensions.Functional.Core",
        "Ave.Extensions.ErrorPaths",
        "Ave.Extensions.ErrorPaths.Functional",
    ];

    /// <summary>What one project's restore graph says its package will depend on.</summary>
    /// <param name="Version">The project's own version (the package version).</param>
    /// <param name="ProjectReferences">The names of the referenced projects (each becomes a package dependency).</param>
    /// <param name="Packages">The package references that flow to the package's users.</param>
    private sealed record PackageInputs(string Version, IReadOnlyList<string> ProjectReferences, IReadOnlyList<string> Packages);

    private static PackageInputs InputsOf(string project)
    {
        var assets = Path.Combine(ProjectRulesTests.RepositoryRoot(), project, "obj", "project.assets.json");
        using var json = JsonDocument.Parse(File.ReadAllText(assets));
        var section = json.RootElement.GetProperty("project");

        // One target framework (net10.0, Directory.Build.props), so the first and only framework entry is the one.
        var restoreFramework = section.GetProperty("restore").GetProperty("frameworks").EnumerateObject().Single().Value;
        var framework = section.GetProperty("frameworks").EnumerateObject().Single().Value;

        var projectReferences = restoreFramework.TryGetProperty("projectReferences", out var references)
            ? references.EnumerateObject().Select(reference => Path.GetFileNameWithoutExtension(reference.Name)).ToList()
            : [];
        var packages = framework.TryGetProperty("dependencies", out var dependencies)
            ? dependencies.EnumerateObject()
                .Where(dependency => !(dependency.Value.TryGetProperty("suppressParent", out var suppress) && suppress.GetString() == "All"))
                .Select(dependency => dependency.Name)
                .ToList()
            : [];

        return new PackageInputs(section.GetProperty("version").GetString()!, projectReferences, packages);
    }

    /// <summary>The projects that are packed: every project that is neither a test project nor a sample or helper.</summary>
    private static IEnumerable<string> PackedProjectNames() =>
        Directory.GetFiles(ProjectRulesTests.RepositoryRoot(), "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !Path.GetFileName(path).StartsWith("UnitTests.", StringComparison.Ordinal))
            .Where(path => !XDocument.Load(path).Descendants("IsSampleOrSupport").Any(element => element.Value == "true"))
            .Select(path => Path.GetFileNameWithoutExtension(path)!);

    [Fact]
    public void The_packed_projects_are_the_eleven_libraries_of_the_layer_list() =>
        PackedProjectNames().Should().BeEquivalentTo(AssemblyRulesTests.LibraryNames);

    [Theory]
    [MemberData(nameof(AssemblyRulesTests.Libraries), MemberType = typeof(AssemblyRulesTests))]
    public void Library_package_depends_only_on_engine_packages_and_the_allowed_packages(string library)
    {
        var inputs = InputsOf(library);

        // Core references no other project, so an empty list is a pass.
        inputs.ProjectReferences.Where(reference => !reference.StartsWith("Ave.Extensions.Documents", StringComparison.Ordinal)).Should().BeEmpty();
        inputs.ProjectReferences.Should().BeSubsetOf(PackedProjectNames(), "a reference to a project that is not packed is a dependency nobody can restore");
        inputs.Packages.Should().BeSubsetOf(AllowedPackages);
    }

    [Theory]
    [MemberData(nameof(AssemblyRulesTests.Libraries), MemberType = typeof(AssemblyRulesTests))]
    public void Library_package_depends_on_engine_packages_of_its_own_version(string library)
    {
        var inputs = InputsOf(library);

        inputs.ProjectReferences.Select(reference => InputsOf(reference).Version).Should().AllBe(inputs.Version);
    }

    [Fact]
    public void Every_library_package_has_the_same_version()
    {
        var versions = AssemblyRulesTests.LibraryNames.Select(library => InputsOf(library).Version).Distinct().ToList();

        versions.Should().ContainSingle().Which.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void The_rule_sees_packages_that_flow_to_users()
    {
        // Positive control: this test project's own restore graph holds the test packages (added for every UnitTests.*
        // project by Directory.Build.props, not by its .csproj) and a project reference; a reader that found none of them
        // would let every library pass.
        var inputs = InputsOf("UnitTests.Extensions.Documents.Architecture");

        inputs.Packages.Should().Contain(["xunit", "FluentAssertions"]).And.NotContain("xunit.runner.visualstudio", "that one is private");
        inputs.ProjectReferences.Should().Contain("Ave.Extensions.Documents");
    }
}
