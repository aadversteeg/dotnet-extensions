using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Versioning;
using Xunit;
using Xunit.Abstractions;

namespace DotnetExtensions.Ns20AssetCheck
{
    /// <summary>
    /// Compiled into every test project when the repository is built with -p:UseNs20=true (see
    /// Directory.Build.targets). Proves that the test run really used the netstandard2.0 asset.
    /// </summary>
    public class Ns20AssetCheckTests
    {
        private const string NetStandard20 = ".NETStandard,Version=v2.0";

        private readonly ITestOutputHelper _output;

        /// <summary>
        /// Creates the test class.
        /// </summary>
        /// <param name="output">Receives the list of loaded libraries.</param>
        public Ns20AssetCheckTests(ITestOutputHelper output)
        {
            _output = output;
        }

        /// <summary>
        /// Every Ave.Extensions library the test assembly loads, directly or through another library, is the
        /// netstandard2.0 asset.
        /// </summary>
        [Fact(DisplayName = "NS20-001: Every Ave.Extensions library loaded by this test assembly is the netstandard2.0 asset")]
        public void NS20001()
        {
            var libraries = new SortedDictionary<string, Assembly>(StringComparer.Ordinal);
            Collect(typeof(Ns20AssetCheckTests).Assembly, libraries);

            var wrong = new List<string>();
            foreach (var library in libraries)
            {
                var framework = library.Value.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName ?? "(none)";
                _output.WriteLine($"{library.Key}: {framework} ({library.Value.Location})");
                if (framework != NetStandard20)
                {
                    wrong.Add($"{library.Key}: {framework}");
                }
            }

            Assert.NotEmpty(libraries);
            Assert.True(wrong.Count == 0, "Not the netstandard2.0 asset: " + string.Join(", ", wrong));
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
