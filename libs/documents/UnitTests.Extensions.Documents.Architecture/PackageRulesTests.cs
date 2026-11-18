using System.IO;
using System.Text.Json;
using System.Xml.Linq;

namespace UnitTests.Extensions.Documents.Architecture;

/// <summary>
/// Rules for what the packages bring with them. A package's dependencies are written by <c>dotnet pack</c> from the
/// restore graph in the project's <c>obj/project.assets.json</c>: every project reference becomes a dependency on that
/// project's package, and every package reference that is not private (<c>PrivateAssets="all"</c>) becomes a package
/// dependency. So the rules on dependencies are checked on that file, the evaluated input of pack, rather than on the
/// project files, which do not show what the build files and the SDK add. In this repository a library references
/// another package of the repository with a package reference, which the reference switch in Directory.Build.targets
/// turns into a project reference in head mode (the default); the restore graph shows it as a project reference then.
/// </summary>
public class PackageRulesTests
{
    /// <summary>
    /// The only packages outside the engine a library package may depend on: errors as values. Only the lowest layer
    /// (Core) references them; the other libraries get them through Core.
    /// </summary>
    private static readonly string[] AllowedPackages =
    [
        "Ave.Extensions.Functional",
        "Ave.Extensions.Functional.Core",
        "Ave.Extensions.ErrorPaths",
        "Ave.Extensions.ErrorPaths.Functional",
    ];

    private const string Core = "Ave.Extensions.Documents.Core";

    /// <summary>What one project's restore graph says its package will depend on.</summary>
    /// <param name="ProjectReferences">The names of the referenced projects (each becomes a package dependency).</param>
    /// <param name="Packages">The package references that flow to the package's users.</param>
    private sealed record PackageInputs(IReadOnlyList<string> ProjectReferences, IReadOnlyList<string> Packages)
    {
        /// <summary>Everything the package will depend on, by package id.</summary>
        public IEnumerable<string> Dependencies => ProjectReferences.Concat(Packages);
    }

    private static PackageInputs InputsOf(string project)
    {
        var assets = Path.Combine(ProjectRulesTests.FamilyRoot(), project, "obj", "project.assets.json");
        using var json = JsonDocument.Parse(File.ReadAllText(assets));
        var section = json.RootElement.GetProperty("project");

        // One target framework (net10.0, set in every project of the family), so the first and only framework entry is the one.
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

        return new PackageInputs(projectReferences, packages);
    }

    /// <summary>The project file of a project in the family folder.</summary>
    private static XDocument ProjectFile(string project) =>
        XDocument.Load(Path.Combine(ProjectRulesTests.FamilyRoot(), project, project + ".csproj"));

    /// <summary>
    /// The projects that are packed: every Ave.Extensions.Documents project in the family folder whose project file does
    /// not switch packing off (the test-support helper does; test projects and the samples are not named so).
    /// </summary>
    private static IEnumerable<string> PackedProjectNames() =>
        Directory.GetDirectories(ProjectRulesTests.FamilyRoot(), "Ave.Extensions.Documents*")
            .Select(Path.GetFileName)
            .Select(name => name!)
            .Where(name => File.Exists(Path.Combine(ProjectRulesTests.FamilyRoot(), name, name + ".csproj")))
            .Where(name => !ProjectFile(name).Descendants("IsPackable").Any(element => element.Value == "false"));

    [Fact]
    public void The_packed_projects_are_the_libraries_of_the_layer_list() =>
        PackedProjectNames().Should().BeEquivalentTo(AssemblyRulesTests.LibraryNames);

    [Theory]
    [MemberData(nameof(AssemblyRulesTests.Libraries), MemberType = typeof(AssemblyRulesTests))]
    public void Library_package_depends_only_on_engine_packages_and_the_allowed_packages(string library)
    {
        var inputs = InputsOf(library);

        inputs.Dependencies.Where(dependency => !dependency.StartsWith("Ave.Extensions.Documents", StringComparison.Ordinal))
            .Should().BeSubsetOf(AllowedPackages);
        inputs.Dependencies.Where(dependency => dependency.StartsWith("Ave.Extensions.Documents", StringComparison.Ordinal))
            .Should().BeSubsetOf(PackedProjectNames(), "a dependency on a project that is not packed is a dependency nobody can restore");
    }

    /// <summary>
    /// The layer rule for what a package will depend on, not only for the compiled references: the compiler drops a
    /// reference no type of which is used, so a forbidden project or package reference would pass the assembly rule and
    /// still become a dependency of the package (review 39 A-1).
    /// </summary>
    [Theory]
    [MemberData(nameof(AssemblyRulesTests.Libraries), MemberType = typeof(AssemblyRulesTests))]
    public void Library_package_depends_only_on_the_engine_libraries_of_its_layers(string library) =>
        InputsOf(library).Dependencies.Where(dependency => dependency.StartsWith("Ave.Extensions.Documents", StringComparison.Ordinal))
            .Should().BeSubsetOf(AssemblyRulesTests.AllowedFor(library));

    [Theory]
    [MemberData(nameof(AssemblyRulesTests.Libraries), MemberType = typeof(AssemblyRulesTests))]
    public void Only_core_references_the_packages_outside_the_engine(string library)
    {
        var outside = InputsOf(library).Dependencies.Where(dependency => AllowedPackages.Contains(dependency));

        if (library == Core)
        {
            outside.Should().NotBeEmpty("Core is where errors as values come in");
        }
        else
        {
            outside.Should().BeEmpty("the libraries above Core get the errors-as-values packages through Core");
        }
    }

    [Theory]
    [MemberData(nameof(AssemblyRulesTests.Libraries), MemberType = typeof(AssemblyRulesTests))]
    public void Library_references_the_other_packages_as_packages(string library)
    {
        // A project reference in a library's project file would make its package depend on the head build of the other
        // package at the release version, whatever was published; the reference switch needs a package reference.
        var project = ProjectFile(library);

        project.Descendants("ProjectReference").Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(AssemblyRulesTests.Libraries), MemberType = typeof(AssemblyRulesTests))]
    public void Every_package_a_library_references_has_a_minimum_version(string library)
    {
        // In head mode the reference switch removes the package reference before restore, so a missing version shows
        // only in a release build. Directory.Packages.props must name it, or the reference carries a VersionOverride.
        var central = XDocument.Load(Path.Combine(ProjectRulesTests.FamilyRoot(), "..", "..", "Directory.Packages.props"))
            .Descendants("PackageVersion")
            .Select(version => (string?)version.Attribute("Include"))
            .ToHashSet();
        var withoutVersion = ProjectFile(library).Descendants("PackageReference")
            .Where(reference => reference.Attribute("VersionOverride") is null)
            .Select(reference => (string?)reference.Attribute("Include"))
            .Where(package => !central.Contains(package));

        withoutVersion.Should().BeEmpty();
    }

    [Fact]
    public void The_rule_sees_packages_and_project_references()
    {
        // Positive controls: this test project's own restore graph holds its test packages and a project reference, and
        // Core's holds the errors-as-values packages (as projects of this repository in head mode, as packages in release
        // mode); a reader that found none of them would let every library pass.
        var inputs = InputsOf("UnitTests.Extensions.Documents.Architecture");

        inputs.Packages.Should().Contain(["xunit", "FluentAssertions"]).And.NotContain("xunit.runner.visualstudio", "that one is private");
        inputs.ProjectReferences.Should().Contain("Ave.Extensions.Documents");
        InputsOf(Core).Dependencies.Should().Contain(AllowedPackages);
    }
}
