using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Ave.Extensions.Documents.TestSupport;

/// <summary>
/// The image of the reference tools (qpdf, poppler, MuPDF, fontTools, PyMuPDF), built locally from
/// <c>tools/reference/tools.Dockerfile</c>, whose every input is pinned (see that file). The tag is the first 12 hex
/// digits of the Dockerfile's SHA-256, the same tag <c>tools/verify-samples.ps1</c> and the Chromium measurement script
/// build, so all of them run one image, and a changed Dockerfile builds a new one.
/// </summary>
public static class ReferenceTools
{
    private static readonly Lazy<string> Built = new(Build);

    /// <summary>The image's tag, built on first use (a few minutes the first time on a machine, then cached by Docker).</summary>
    /// <exception cref="InvalidOperationException">The build failed.</exception>
    public static string Image => Built.Value;

    private static string Build()
    {
        var dockerfile = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "tools", "tools.Dockerfile"));
        var tag = "ave-extensions-documents-tools:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(dockerfile)))[..12];
        var (exitCode, output) = Docker.Run(["build", "-q", "-t", tag, "-"], TimeSpan.FromMinutes(20), dockerfile);
        return exitCode == 0 ? tag : throw new InvalidOperationException($"docker build of {tag} failed:\n{output}");
    }
}
