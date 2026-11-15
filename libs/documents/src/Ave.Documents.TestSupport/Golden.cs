using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace Ave.Documents.TestSupport;

/// <summary>
/// Golden-file comparison for the text serialisations. A golden file lives in a <c>Golden</c> folder next to
/// the test source file. To accept new output after a deliberate change, run the tests with the environment
/// variable <c>UPDATE_GOLDEN=1</c>, review the diff with git, and commit it.
/// </summary>
public static class Golden
{
    /// <summary>
    /// Compares <paramref name="actual"/> with the golden file. Returns null when equal; otherwise a message that
    /// says where they first differ. With UPDATE_GOLDEN=1 the file is (re)written and null is returned.
    /// </summary>
    /// <param name="name">The golden file name, for example <c>letter.model.txt</c>.</param>
    /// <param name="actual">The text produced by the test.</param>
    /// <param name="testFile">Filled in by the compiler: locates the Golden folder.</param>
    /// <returns>Null when the text matches, or a description of the first difference.</returns>
    public static string? Compare(string name, string actual, [CallerFilePath] string testFile = "")
    {
        var path = Path.Combine(Path.GetDirectoryName(testFile)!, "Golden", name);
        if (Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
            return null;
        }

        if (!File.Exists(path))
        {
            return $"Golden file {path} does not exist; run with UPDATE_GOLDEN=1 to create it.";
        }

        var expected = File.ReadAllText(path).ReplaceLineEndings("\n");
        if (expected == actual)
        {
            return null;
        }

        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');
        for (var index = 0; index < Math.Max(expectedLines.Length, actualLines.Length); index++)
        {
            var left = index < expectedLines.Length ? expectedLines[index] : "<end of file>";
            var right = index < actualLines.Length ? actualLines[index] : "<end of file>";
            if (left != right)
            {
                return $"{name} differs at line {index + 1}:\n  expected: {left}\n  actual:   {right}";
            }
        }

        return $"{name} differs (line endings only).";
    }
}
