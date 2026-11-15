using System.IO;
using System.Xml.Linq;

namespace UnitTests.Ave.Documents.Architecture;

/// <summary>
/// Rules checked on the project files. Only the <c>UnitTests.*</c> projects are test projects: <c>dotnet test</c> on
/// the solution runs every project marked as one, and a helper project marked by mistake aborts the run with exit
/// code 1 although every test passed.
/// </summary>
public class ProjectRulesTests
{
    /// <summary>The repository root: the folder above this assembly that holds Directory.Build.props.</summary>
    private static string RepositoryRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Directory.Build.props")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? throw new InvalidOperationException("Directory.Build.props not found above " + AppContext.BaseDirectory);
    }

    private static IEnumerable<string> NonTestProjectNames() =>
        Directory.GetFiles(RepositoryRoot(), "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(path => Path.GetFileNameWithoutExtension(path)!)
            .Where(name => !name.StartsWith("UnitTests.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

    public static TheoryData<string> NonTestProjects => [.. NonTestProjectNames()];

    [Fact]
    public void The_helper_and_sample_projects_are_found() =>
        NonTestProjectNames().Should().Contain(["Ave.Documents.TestSupport", "Ave.Documents.Samples", "Ave.Documents.Core"]);

    [Theory]
    [MemberData(nameof(NonTestProjects))]
    public void A_project_that_is_not_a_test_project_does_not_reference_the_xunit_package(string project)
    {
        // The xunit package brings xunit.core, whose build props set IsTestProject to true and add the TestContainer
        // capability; xunit.extensibility.core holds the same attributes without that.
        var file = Directory.GetFiles(RepositoryRoot(), project + ".csproj", SearchOption.AllDirectories).Single();
        var packages = XDocument.Load(file).Descendants("PackageReference").Select(reference => (string?)reference.Attribute("Include"));

        packages.Should().NotContain(["xunit", "xunit.core", "Microsoft.NET.Test.Sdk"], $"{project} is not a test project");
    }
}
