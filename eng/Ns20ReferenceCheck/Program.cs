using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;

namespace DotnetExtensions.Ns20ReferenceCheck
{
    /// <summary>
    /// Checks the assembly references of the netstandard2.0 build of every library in libs/*/Ave.Extensions.*.
    ///
    /// A library may reference only assemblies, at versions, that its declared package dependencies guarantee
    /// to a consumer. The check restores a throwaway netstandard2.0 consumer project with a project reference to
    /// the library, so NuGet resolves exactly what a consumer gets: the dependencies the package declares, at
    /// their lowest versions, without the build-only (PrivateAssets="All") packages of the library itself. Every
    /// assembly reference of the built netstandard2.0 DLL must be found in that graph (or in the netstandard2.0
    /// reference assemblies) with an assembly version at least as high as the one referenced.
    ///
    /// Why: a build-only package can put a newer assembly (for example System.Memory 4.6.x) into the compile graph.
    /// A netstandard2.1 API can then compile on netstandard2.0 through an extension method and leave a reference
    /// to that newer assembly, which a .NET Framework consumer does not get and fails to load at run time.
    ///
    /// Usage (after a build of the libraries in the same configuration and reference mode):
    ///   dotnet run --project eng/Ns20ReferenceCheck -c Release -- [--configuration Release]
    ///       [--repo-references true|false] [--verbose]
    /// Exit code 0 = every reference is guaranteed, 1 = at least one is not, 2 = the check could not run.
    /// </summary>
    internal static class Program
    {
        private const string NetStandard20 = "netstandard2.0";

        private static int Main(string[] args)
        {
            var configuration = "Release";
            var repoReferences = "true";
            var verbose = false;
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--configuration" when i + 1 < args.Length:
                        configuration = args[++i];
                        break;
                    case "--repo-references" when i + 1 < args.Length:
                        repoReferences = args[++i];
                        break;
                    case "--verbose":
                        verbose = true;
                        break;
                    default:
                        Console.Error.WriteLine($"Unknown argument: {args[i]}");
                        return 2;
                }
            }

            var repository = FindRepository();
            if (repository == null)
            {
                Console.Error.WriteLine("Run this from inside the repository (no dotnet-extensions.slnx found above the current directory).");
                return 2;
            }

            var work = Path.Combine(Path.GetTempPath(), "ns20-reference-check-" + Guid.NewGuid().ToString("N"));
            try
            {
                return Run(repository, work, configuration, repoReferences, verbose);
            }
            finally
            {
                if (Directory.Exists(work))
                {
                    Directory.Delete(work, true);
                }
            }
        }

        private static int Run(string repository, string work, string configuration, string repoReferences, bool verbose)
        {
            Directory.CreateDirectory(work);

            // Keep the consumer projects free of any Directory.Build.* above the temp folder; same package source
            // as the repository.
            File.WriteAllText(Path.Combine(work, "Directory.Build.props"), "<Project />");
            File.WriteAllText(Path.Combine(work, "Directory.Build.targets"), "<Project />");
            File.WriteAllText(Path.Combine(work, "Directory.Packages.props"), "<Project />");
            File.Copy(Path.Combine(repository, "NuGet.config"), Path.Combine(work, "NuGet.config"));

            var libraries = FindNetStandard20Libraries(repository, work, repoReferences);
            if (libraries == null)
            {
                return 2;
            }

            var solution = new StringBuilder("<Solution>\n");
            foreach (var library in libraries)
            {
                var name = Path.GetFileNameWithoutExtension(library);
                var folder = Path.Combine(work, name);
                Directory.CreateDirectory(folder);
                File.WriteAllText(
                    Path.Combine(folder, name + ".Consumer.csproj"),
                    "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
                    "  <PropertyGroup><TargetFramework>" + NetStandard20 + "</TargetFramework></PropertyGroup>\n" +
                    "  <ItemGroup><ProjectReference Include=\"" + library + "\" /></ItemGroup>\n" +
                    "</Project>\n");
                solution.Append("  <Project Path=\"" + name + "/" + name + ".Consumer.csproj\" />\n");
            }

            solution.Append("</Solution>\n");
            File.WriteAllText(Path.Combine(work, "consumers.slnx"), solution.ToString());

            if (Dotnet(work, "restore", "consumers.slnx", "-nologo", "-p:UseRepoProjectReferences=" + repoReferences) != 0)
            {
                Console.Error.WriteLine("Restore of the consumer projects failed (output above).");
                return 2;
            }

            var failures = 0;
            foreach (var library in libraries)
            {
                var name = Path.GetFileNameWithoutExtension(library);
                var dll = Path.Combine(Path.GetDirectoryName(library)!, "bin", configuration, NetStandard20, name + ".dll");
                if (!File.Exists(dll))
                {
                    Console.Error.WriteLine($"{name}: {dll} does not exist. Build the libraries with -c {configuration} first.");
                    return 2;
                }

                var guaranteed = ReadConsumerGraph(Path.Combine(work, name, "obj", "project.assets.json"));
                var references = ReadAssemblyReferences(dll);
                var problems = new List<string>();
                foreach (var reference in references)
                {
                    if (!guaranteed.TryGetValue(reference.Key, out var source))
                    {
                        problems.Add($"{reference.Key} {reference.Value}: no declared dependency provides this assembly");
                    }
                    else if (source.Version != null && source.Version < reference.Value)
                    {
                        problems.Add($"{reference.Key} {reference.Value}: the declared dependencies guarantee only {source.Version} ({source.Package})");
                    }
                    else if (verbose)
                    {
                        Console.WriteLine($"  {name} -> {reference.Key} {reference.Value}: {source.Package} ({source.Version?.ToString() ?? "project"})");
                    }
                }

                if (problems.Count == 0)
                {
                    Console.WriteLine($"OK    {name} (netstandard2.0): {references.Count} assembly references, all guaranteed by its dependencies");
                }
                else
                {
                    failures++;
                    Console.WriteLine($"FAIL  {name} (netstandard2.0):");
                    foreach (var problem in problems)
                    {
                        Console.WriteLine("        " + problem);
                    }
                }
            }

            Console.WriteLine(failures == 0
                ? $"{libraries.Count} netstandard2.0 libraries checked, no hidden references."
                : $"{failures} of {libraries.Count} netstandard2.0 libraries reference assemblies their dependencies do not guarantee.");
            return failures == 0 ? 0 : 1;
        }

        private static string? FindRepository()
        {
            var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "dotnet-extensions.slnx")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName;
        }

        /// <summary>
        /// The project files of the libraries whose TargetFrameworks include netstandard2.0, asked from MSBuild.
        /// </summary>
        private static List<string>? FindNetStandard20Libraries(string repository, string work, string repoReferences)
        {
            var list = Path.Combine(work, "libraries.txt");
            var project = Path.Combine(work, "libraries.proj");
            File.WriteAllText(project,
                "<Project>\n" +
                "  <ItemGroup><Library Include=\"" + Path.Combine(repository, "libs") + "/*/Ave.Extensions.*/Ave.Extensions.*.csproj\" /></ItemGroup>\n" +
                "  <Target Name=\"Build\">\n" +
                "    <MSBuild Projects=\"@(Library)\" Targets=\"GetTargetFrameworks\">\n" +
                "      <Output TaskParameter=\"TargetOutputs\" ItemName=\"Framework\" />\n" +
                "    </MSBuild>\n" +
                "    <WriteLinesToFile File=\"" + list + "\" Lines=\"@(Framework->'%(FullPath)|%(TargetFrameworks)')\" Overwrite=\"true\" />\n" +
                "  </Target>\n" +
                "</Project>\n");

            if (Dotnet(work, "msbuild", project, "-nologo", "-v:q", "-p:UseRepoProjectReferences=" + repoReferences) != 0)
            {
                Console.Error.WriteLine("Could not read the target frameworks of the libraries (output above).");
                return null;
            }

            var libraries = new List<string>();
            foreach (var line in File.ReadAllLines(list))
            {
                var parts = line.Split('|');
                if (Array.IndexOf(parts[1].Split(';'), NetStandard20) >= 0)
                {
                    libraries.Add(parts[0]);
                }
            }

            libraries.Sort(StringComparer.Ordinal);
            if (libraries.Count == 0)
            {
                Console.Error.WriteLine("No library targets netstandard2.0; nothing to check is suspicious.");
                return null;
            }

            return libraries;
        }

        /// <summary>
        /// Every assembly a consumer of the library gets on netstandard2.0, with its assembly version and the package
        /// it comes from. Projects (our own libraries in head mode) have no version: any version is accepted.
        /// </summary>
        private static Dictionary<string, Source> ReadConsumerGraph(string assetsFile)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(assetsFile));
            var root = document.RootElement;

            var packageFolders = new List<string>();
            foreach (var folder in root.GetProperty("packageFolders").EnumerateObject())
            {
                packageFolders.Add(folder.Name);
            }

            var target = default(JsonElement);
            foreach (var candidate in root.GetProperty("targets").EnumerateObject())
            {
                if (candidate.Name.IndexOf('/') < 0)
                {
                    target = candidate.Value;
                }
            }

            var libraries = root.GetProperty("libraries");
            var assemblies = new Dictionary<string, Source>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in target.EnumerateObject())
            {
                var id = entry.Name.Substring(0, entry.Name.IndexOf('/'));
                if (entry.Value.GetProperty("type").GetString() == "project")
                {
                    assemblies[id] = new Source(id, null);
                    continue;
                }

                var packageFolder = FindPackageFolder(packageFolders, libraries.GetProperty(entry.Name).GetProperty("path").GetString()!);
                var files = new List<string>();
                if (entry.Value.TryGetProperty("compile", out var compile))
                {
                    foreach (var item in compile.EnumerateObject())
                    {
                        if (!item.Name.EndsWith("/_._", StringComparison.Ordinal))
                        {
                            files.Add(Path.Combine(packageFolder, item.Name));
                        }
                    }
                }

                // The netstandard2.0 reference assemblies (netstandard.dll and its facades) come with this package
                // through its build targets, not as compile items.
                if (string.Equals(id, "NETStandard.Library", StringComparison.OrdinalIgnoreCase))
                {
                    files.AddRange(Directory.GetFiles(Path.Combine(packageFolder, "build", NetStandard20, "ref"), "*.dll"));
                }

                foreach (var file in files)
                {
                    var name = ReadAssemblyName(file);
                    if (!assemblies.TryGetValue(name.Key, out var existing) || (existing.Version != null && existing.Version < name.Value))
                    {
                        assemblies[name.Key] = new Source(entry.Name, name.Value);
                    }
                }
            }

            return assemblies;
        }

        private static string FindPackageFolder(List<string> packageFolders, string path)
        {
            foreach (var folder in packageFolders)
            {
                var candidate = Path.Combine(folder, path);
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new DirectoryNotFoundException($"Package folder {path} not found in {string.Join(", ", packageFolders)}");
        }

        private static KeyValuePair<string, Version> ReadAssemblyName(string file)
        {
            using var stream = File.OpenRead(file);
            using var pe = new PEReader(stream);
            var metadata = pe.GetMetadataReader();
            var definition = metadata.GetAssemblyDefinition();
            return new KeyValuePair<string, Version>(metadata.GetString(definition.Name), definition.Version);
        }

        private static SortedDictionary<string, Version> ReadAssemblyReferences(string file)
        {
            using var stream = File.OpenRead(file);
            using var pe = new PEReader(stream);
            var metadata = pe.GetMetadataReader();
            var references = new SortedDictionary<string, Version>(StringComparer.OrdinalIgnoreCase);
            foreach (var handle in metadata.AssemblyReferences)
            {
                var reference = metadata.GetAssemblyReference(handle);
                references[metadata.GetString(reference.Name)] = reference.Version;
            }

            return references;
        }

        private static int Dotnet(string workingDirectory, params string[] arguments)
        {
            var start = new ProcessStartInfo("dotnet") { WorkingDirectory = workingDirectory, UseShellExecute = false };
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start)!;
            process.WaitForExit();
            return process.ExitCode;
        }

        private sealed class Source
        {
            public Source(string package, Version? version)
            {
                Package = package;
                Version = version;
            }

            public string Package { get; }

            public Version? Version { get; }
        }
    }
}
