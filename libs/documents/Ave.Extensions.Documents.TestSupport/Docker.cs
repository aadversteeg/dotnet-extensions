using System;
using System.Collections.Generic;
using System.Diagnostics;
using Xunit;

namespace Ave.Extensions.Documents.TestSupport;

/// <summary>
/// Runs reference tools that live in Docker images (veraPDF, qpdf). Tests that need them are marked with
/// <see cref="DockerFactAttribute"/> or <see cref="DockerTheoryAttribute"/> and carry the trait <c>Category=Reference</c>,
/// so <c>dotnet test --filter Category!=Reference</c> leaves them out; on a machine without Docker they are skipped with
/// a message that says so, instead of failing.
/// </summary>
public static class Docker
{
    /// <summary>
    /// Why Docker cannot be used here, or null when it can: the daemon answers <c>docker version</c> and runs Linux
    /// containers (the reference images are Linux images; a Windows runner's Docker runs Windows containers only).
    /// </summary>
    public static string? UnavailableReason { get; } = Probe();

    /// <summary>
    /// The <c>docker run</c> arguments that run a container as the current user (<c>--user uid:gid</c>) on Linux and
    /// macOS, empty on Windows. A container runs as its image's user otherwise: files it writes into a mounted folder
    /// then belong to that user (root, for most images), and it cannot read a folder only the current user may read,
    /// such as one made by <see cref="System.IO.Directory.CreateTempSubdirectory(string)"/> (mode 0700). Docker Desktop
    /// on Windows maps mounted folders itself.
    /// </summary>
    public static IReadOnlyList<string> UserArguments { get; } = CurrentUser();

    /// <summary>Runs <c>docker</c> with the arguments and waits for it.</summary>
    /// <param name="arguments">The arguments, each passed as is (no shell).</param>
    /// <param name="timeout">How long to wait.</param>
    /// <param name="standardInput">Text for the command's standard input (a Dockerfile for <c>docker build -</c>), or
    /// null for none.</param>
    /// <returns>The exit code and standard output; standard error is appended after a marker line.</returns>
    /// <exception cref="TimeoutException">The command did not finish in time.</exception>
    public static (int ExitCode, string Output) Run(IEnumerable<string> arguments, TimeSpan timeout, string? standardInput = null)
    {
        var start = new ProcessStartInfo("docker")
        {
            RedirectStandardInput = standardInput is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("docker did not start.");
        if (standardInput is not null)
        {
            process.StandardInput.Write(standardInput);
            process.StandardInput.Close();
        }

        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeout))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"docker {string.Join(' ', start.ArgumentList)} did not finish within {timeout}.");
        }

        var errors = error.Result;
        return (process.ExitCode, errors.Length == 0 ? output.Result : output.Result + "\n--- stderr ---\n" + errors);
    }

    private static string? Probe()
    {
        try
        {
            var (exitCode, output) = Run(["version", "--format", "{{.Server.Os}}"], TimeSpan.FromSeconds(30));
            return exitCode != 0
                ? $"Skipped: Docker is installed but its daemon does not answer ({output.Trim()})."
                : output.Trim() != "linux"
                    ? $"Skipped: Docker runs {output.Trim()} containers here; the reference checks run Linux images."
                    : null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or TimeoutException or InvalidOperationException)
        {
            return $"Skipped: Docker is not available ({exception.Message}); reference checks run the tools in Docker images.";
        }
    }

    private static string[] CurrentUser() =>
        OperatingSystem.IsWindows() ? [] : ["--user", $"{Id("-u")}:{Id("-g")}"];

    /// <summary>The output of <c>id</c> with one option: the current user's numeric id (<c>-u</c>) or group id (<c>-g</c>).</summary>
    private static string Id(string option)
    {
        var start = new ProcessStartInfo("id", option) { RedirectStandardOutput = true, UseShellExecute = false };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("id did not start.");
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        return process.ExitCode == 0 && output.Length > 0 ? output : throw new InvalidOperationException($"id {option} failed.");
    }
}

/// <summary>A fact that needs Docker: skipped with a clear message when Docker is not available.</summary>
public sealed class DockerFactAttribute : FactAttribute
{
    /// <summary>Sets <see cref="FactAttribute.Skip"/> when Docker cannot be used.</summary>
    public DockerFactAttribute() => Skip = Docker.UnavailableReason;
}

/// <summary>A theory that needs Docker: skipped with a clear message when Docker is not available.</summary>
public sealed class DockerTheoryAttribute : TheoryAttribute
{
    /// <summary>Sets <see cref="FactAttribute.Skip"/> when Docker cannot be used.</summary>
    public DockerTheoryAttribute() => Skip = Docker.UnavailableReason;
}
