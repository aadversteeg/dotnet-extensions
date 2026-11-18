using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Versioning;
using Xunit;
using Xunit.Abstractions;

namespace DotnetExtensions.AssetCheck
{
    /// <summary>
    /// Compiled into every test project when the repository is built with -p:UseNs20=true, -p:UseNs21=true or
    /// -p:UseNet48=true (see Directory.Build.targets). Proves that the test run really used the build of each library
    /// that the mode asks for, and with -p:UseNet48=true also the runtime. Only the tests of the active mode are
    /// compiled.
    /// </summary>
    public class AssetCheckTests
    {
        private const string NetStandard20 = ".NETStandard,Version=v2.0";
        private const string NetStandard21 = ".NETStandard,Version=v2.1";

        private readonly ITestOutputHelper _output;

        /// <summary>
        /// Creates the test class.
        /// </summary>
        /// <param name="output">Receives the list of loaded libraries.</param>
        public AssetCheckTests(ITestOutputHelper output)
        {
            _output = output;
        }

#if AVE_TEST_ON_NS21
        private const string TargetFrameworksKey = "AveTargetFrameworks:";

        /// <summary>
        /// Every Ave.Extensions library the test assembly loads, directly or through another library, is the
        /// netstandard2.1 asset when the library has one, and the netstandard2.0 asset otherwise. Which libraries
        /// have a netstandard2.1 asset comes from the build: Directory.Build.targets writes the target frameworks of
        /// every project the test project references into an AssemblyMetadata attribute of the test assembly.
        /// </summary>
        [Fact(DisplayName = "NS21-001: Every Ave.Extensions library loaded by this test assembly is the netstandard2.1 asset where the library has one, otherwise the netstandard2.0 asset")]
        public void NS21001()
        {
            var targetFrameworks = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var metadata in typeof(AssetCheckTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
            {
                if (metadata.Key.StartsWith(TargetFrameworksKey, StringComparison.Ordinal))
                {
                    targetFrameworks[metadata.Key.Substring(TargetFrameworksKey.Length)] = metadata.Value ?? string.Empty;
                }
            }

            Check("the netstandard2.1 asset where the library has one, otherwise the netstandard2.0 asset", name =>
            {
                if (!targetFrameworks.TryGetValue(name, out var frameworks))
                {
                    return null;
                }

                return (";" + frameworks + ";").Contains(";netstandard2.1;") ? NetStandard21 : NetStandard20;
            });
        }
#else
        /// <summary>
        /// Every Ave.Extensions library the test assembly loads, directly or through another library, is the
        /// netstandard2.0 asset.
        /// </summary>
        [Fact(DisplayName = "NS20-001: Every Ave.Extensions library loaded by this test assembly is the netstandard2.0 asset")]
        public void NS20001()
        {
            Check("the netstandard2.0 asset", name => NetStandard20);
        }
#endif

#if NETFRAMEWORK
        /// <summary>
        /// With -p:UseNet48=true the tests run on .NET Framework, not on .NET (Core).
        /// </summary>
        [Fact(DisplayName = "NET48-001: The test host runs on .NET Framework")]
        public void NET48001()
        {
            var description = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
            var process = System.Diagnostics.Process.GetCurrentProcess();
            _output.WriteLine($"Runtime: {description}; CLR {Environment.Version}; process {process.MainModule?.FileName}; 64-bit {Environment.Is64BitProcess}");
            Assert.StartsWith(".NET Framework", description, StringComparison.Ordinal);
        }
#endif

        private void Check(string expectedDescription, Func<string, string?> expectedFramework)
        {
            var libraries = new SortedDictionary<string, Assembly>(StringComparer.Ordinal);
            Collect(typeof(AssetCheckTests).Assembly, libraries);

            var wrong = new List<string>();
            foreach (var library in libraries)
            {
                var framework = library.Value.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName ?? "(none)";
                var expected = expectedFramework(library.Key);
                _output.WriteLine($"{library.Key}: {framework} ({library.Value.Location})");
                if (framework != expected)
                {
                    wrong.Add($"{library.Key}: {framework}, expected {expected ?? "(unknown: not a project the test project references)"}");
                }
            }

            Assert.NotEmpty(libraries);
            Assert.True(wrong.Count == 0, $"Not {expectedDescription}: " + string.Join(", ", wrong));
        }

        private static void Collect(Assembly assembly, IDictionary<string, Assembly> libraries)
        {
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                var name = reference.Name;
                if (name == null || !name.StartsWith("Ave.Extensions.", StringComparison.Ordinal) || libraries.ContainsKey(name))
                {
                    continue;
                }

                var library = Assembly.Load(reference);
                libraries.Add(name, library);
                Collect(library, libraries);
            }
        }
    }
}
