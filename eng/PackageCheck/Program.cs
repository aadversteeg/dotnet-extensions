using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DotnetExtensions.PackageCheck
{
    /// <summary>
    /// Verifies a packed release package before it is published.
    ///
    ///   dotnet run --project eng/PackageCheck -c Release -- --package artifacts/X.1.2.3.nupkg --id X --version 1.2.3
    ///       --frameworks "netstandard2.0 net10.0" --own-dependencies "Ave.Extensions.Y=0.1.0 Ave.Extensions.Z=0.2.0"
    ///       --repository-url https://github.com/aadversteeg/dotnet-extensions [--commit &lt;sha&gt;]
    ///
    /// The expected values come from eng/release-tags.proj, target ResolveRelease. Checks:
    /// - the file name, the nuspec id and the nuspec version are the expected ones;
    /// - there is exactly one dependency group per target framework;
    /// - in every group the dependencies on our own packages (Ave.Extensions.*) are exactly the expected ones, at the
    ///   expected minimum version (a project reference or a leaked -p:Version shows up here);
    /// - no dependency has a local development version (-dev);
    /// - lib/&lt;framework&gt;/ has the assembly and its XML documentation for every framework;
    /// - README.md and LICENSE are in the package and referenced by the nuspec;
    /// - the repository element has the expected URL and, when given, the expected commit;
    /// - the symbols package (.snupkg) next to it has the PDB for every framework.
    /// Exit code 0 = all checks pass, 1 = at least one fails, 2 = wrong arguments.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i + 1 < args.Length; i += 2)
            {
                options[args[i]] = args[i + 1];
            }

            if (args.Length % 2 != 0 || !HasAll(options, "--package", "--id", "--version", "--frameworks", "--own-dependencies", "--repository-url"))
            {
                Console.Error.WriteLine("Usage: --package <nupkg> --id <id> --version <version> --frameworks \"<tfm> ...\" " +
                    "--own-dependencies \"<id>=<version> ...\" --repository-url <url> [--commit <sha>]");
                return 2;
            }

            var check = new Checker();
            Verify(check, options);
            Console.WriteLine(check.Failures == 0
                ? $"Package check passed: {options["--package"]}"
                : $"Package check FAILED ({check.Failures} problems): {options["--package"]}");
            return check.Failures == 0 ? 0 : 1;
        }

        private static void Verify(Checker check, Dictionary<string, string> options)
        {
            var package = options["--package"];
            var id = options["--id"];
            var version = options["--version"];
            var frameworks = Split(options["--frameworks"]);
            var ownDependencies = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in Split(options["--own-dependencies"]))
            {
                var parts = item.Split('=');
                ownDependencies[parts[0]] = parts[1];
            }

            check.That(File.Exists(package), $"package file exists: {package}");
            if (check.Failures > 0)
            {
                return;
            }

            check.That(Path.GetFileName(package) == $"{id}.{version}.nupkg", $"file name is {id}.{version}.nupkg (is {Path.GetFileName(package)})");

            using var archive = ZipFile.OpenRead(package);
            var entries = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in archive.Entries)
            {
                entries.Add(entry.FullName);
            }

            var nuspecEntry = archive.GetEntry(id + ".nuspec");
            check.That(nuspecEntry != null, $"nuspec {id}.nuspec is in the package");
            if (nuspecEntry == null)
            {
                return;
            }

            XDocument nuspec;
            using (var stream = nuspecEntry.Open())
            {
                nuspec = XDocument.Load(stream);
            }

            var metadata = Child(nuspec.Root!, "metadata")!;
            check.That(Value(metadata, "id") == id, $"nuspec id is {id} (is {Value(metadata, "id")})");
            check.That(Value(metadata, "version") == version, $"nuspec version is {version} (is {Value(metadata, "version")})");

            // Dependency groups: one per target framework, own dependencies exactly as expected
            var expectedGroups = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var framework in frameworks)
            {
                expectedGroups[NuspecFramework(framework)] = framework;
            }

            var groups = new SortedDictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
            var dependencies = Child(metadata, "dependencies");
            if (dependencies != null)
            {
                foreach (var group in dependencies.Elements())
                {
                    if (group.Name.LocalName == "group")
                    {
                        groups[(string?)group.Attribute("targetFramework") ?? "(none)"] = group;
                    }
                    else
                    {
                        check.That(false, $"dependency outside a group: {group}");
                    }
                }
            }

            check.That(
                string.Join(", ", groups.Keys) == string.Join(", ", expectedGroups.Keys),
                $"dependency groups are {string.Join(", ", expectedGroups.Keys)} (are {string.Join(", ", groups.Keys)})");

            foreach (var group in groups)
            {
                var own = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var development = new List<string>();
                foreach (var dependency in group.Value.Elements())
                {
                    var dependencyId = (string?)dependency.Attribute("id") ?? string.Empty;
                    var dependencyVersion = (string?)dependency.Attribute("version") ?? string.Empty;
                    if (dependencyVersion.IndexOf("-dev", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        development.Add(dependencyId + " " + dependencyVersion);
                    }

                    if (dependencyId.StartsWith("Ave.Extensions.", StringComparison.Ordinal))
                    {
                        own[dependencyId] = dependencyVersion;
                    }
                }

                check.That(
                    development.Count == 0,
                    $"{group.Key}: no dependency has a development version (found [{string.Join(", ", development)}])");
                check.That(
                    Describe(own) == Describe(ownDependencies),
                    $"{group.Key}: own dependencies are [{Describe(ownDependencies)}] (are [{Describe(own)}])");
            }

            // Files
            foreach (var framework in frameworks)
            {
                check.That(entries.Contains($"lib/{framework}/{id}.dll"), $"lib/{framework}/{id}.dll is in the package");
                check.That(entries.Contains($"lib/{framework}/{id}.xml"), $"lib/{framework}/{id}.xml is in the package");
            }

            check.That(entries.Contains("README.md") && Value(metadata, "readme") == "README.md", "README.md is in the package and is its readme");
            check.That(entries.Contains("LICENSE") && Value(metadata, "license") == "LICENSE", "LICENSE is in the package and is its license file");

            // Repository
            var repository = Child(metadata, "repository");
            var url = (string?)repository?.Attribute("url");
            var commit = (string?)repository?.Attribute("commit");
            check.That(url == options["--repository-url"], $"repository url is {options["--repository-url"]} (is {url ?? "missing"})");
            check.That(commit != null && Regex.IsMatch(commit, "^[0-9a-f]{40}$"), $"repository commit is a full commit hash (is {commit ?? "missing"})");
            if (options.TryGetValue("--commit", out var expectedCommit))
            {
                check.That(commit == expectedCommit, $"repository commit is {expectedCommit} (is {commit ?? "missing"})");
            }

            // Symbols
            var symbols = Path.ChangeExtension(package, ".snupkg");
            check.That(File.Exists(symbols), $"symbols package exists: {symbols}");
            if (File.Exists(symbols))
            {
                using var symbolArchive = ZipFile.OpenRead(symbols);
                foreach (var framework in frameworks)
                {
                    check.That(symbolArchive.GetEntry($"lib/{framework}/{id}.pdb") != null, $"symbols package has lib/{framework}/{id}.pdb");
                }
            }
        }

        /// <summary>
        /// The targetFramework attribute NuGet writes for a target framework moniker.
        /// </summary>
        private static string NuspecFramework(string framework)
        {
            if (framework.StartsWith("netstandard", StringComparison.Ordinal))
            {
                return ".NETStandard" + framework.Substring("netstandard".Length);
            }

            if (Regex.IsMatch(framework, "^net[0-9]+\\.[0-9]+$"))
            {
                return framework;
            }

            throw new ArgumentException($"Unknown target framework form: {framework}");
        }

        private static bool HasAll(Dictionary<string, string> options, params string[] names)
        {
            foreach (var name in names)
            {
                if (!options.ContainsKey(name))
                {
                    return false;
                }
            }

            return true;
        }

        private static string[] Split(string value)
        {
            return value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string Describe(SortedDictionary<string, string> dependencies)
        {
            var parts = new List<string>();
            foreach (var dependency in dependencies)
            {
                parts.Add(dependency.Key + " " + dependency.Value);
            }

            return string.Join(", ", parts);
        }

        private static XElement? Child(XElement element, string name)
        {
            foreach (var child in element.Elements())
            {
                if (child.Name.LocalName == name)
                {
                    return child;
                }
            }

            return null;
        }

        private static string? Value(XElement element, string name)
        {
            return Child(element, name)?.Value;
        }

        private sealed class Checker
        {
            public int Failures { get; private set; }

            public void That(bool condition, string description)
            {
                Console.WriteLine((condition ? "ok    " : "FAIL  ") + description);
                if (!condition)
                {
                    Failures++;
                }
            }
        }
    }
}
