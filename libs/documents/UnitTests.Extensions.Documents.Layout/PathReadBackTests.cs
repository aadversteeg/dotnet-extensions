using System.IO;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// The read-back of paths that are not rectangles (<c>tools/reference/chromium/paths.py</c>, through <c>positions.py</c>
/// and <c>rectangles.py</c>), by its unit tests in the reference tools image (<c>paths_test.py</c>, PyMuPDF): a disc, a ring
/// stroked 1 px wide, the same ring filled between two circles, and a square, in the box of a list marker at 14 px. The
/// disc and the square differ, the stroked and the filled ring are the same line, a curve is cut to its clip, and a
/// stroke the read-back does not know stops it. With them, the lines the script's sampler writes for a disc, a ring 1 px
/// wide and a square-cornered path, pinned with the same numbers as the engine's sampler (<c>PathSamplerTests</c>). Needs
/// Docker (trait <c>Category=Reference</c>).
/// </summary>
[Trait("Category", "Reference")]
public sealed class PathReadBackTests
{
    private static readonly string[] Scripts = ["positions.py", "rectangles.py", "visible.py", "paths.py", "paths_test.py"];

    [DockerFact]
    public void The_read_back_writes_a_curve_as_the_area_it_paints()
    {
        var folder = Directory.CreateTempSubdirectory("ave-extensions-documents-paths-").FullName;
        try
        {
            foreach (var script in Scripts)
            {
                File.Copy(Path.Combine(AppContext.BaseDirectory, "tools", "chromium", script), Path.Combine(folder, script));
            }

            var (exitCode, output) = Docker.Run(
                ["run", "--rm", .. Docker.UserArguments, "-e", "PYTHONDONTWRITEBYTECODE=1", "-v", $"{folder}:/work:ro", ReferenceTools.Image, "python3", "/work/paths_test.py"],
                TimeSpan.FromMinutes(5));

            exitCode.Should().Be(0, output);
            output.Should().Contain("Ran 11 tests").And.Contain("OK");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
