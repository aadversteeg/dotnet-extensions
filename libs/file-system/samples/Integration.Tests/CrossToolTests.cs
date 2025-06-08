using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// Integration tests that validate consistency across all filesystem sample tools.
/// </summary>
public class CrossToolTests : IDisposable
{
    private readonly string _testDirectory;
    private bool _disposed;

    public CrossToolTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"fstest_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
        CreateTestFiles();
    }

    [Fact]
    public async Task AllTools_ProduceConsistentFileCounts()
    {
        // Run FileCounter
        var counterResult = await RunToolAsync("FileCounter", $"--path \"{_testDirectory}\" --format json");
        var counterJson = JsonDocument.Parse(counterResult);
        var fileCount = counterJson.RootElement.GetProperty("fileCount").GetInt32();

        // Run FileSearch with wildcard to get all files
        var searchResult = await RunToolAsync("FileSearch", $"--path \"{_testDirectory}\" --pattern * --format json");
        var searchJson = JsonDocument.Parse(searchResult);
        var searchCount = searchJson.RootElement.GetProperty("totalMatches").GetInt32();

        // Both tools should find the same number of files
        fileCount.Should().Be(searchCount, "FileCounter and FileSearch should find the same number of files");
    }

    [Fact]
    public async Task AllTools_HandleMissingDirectory()
    {
        var missingPath = Path.Combine(_testDirectory, "missing");

        var tools = new[] { "FileCounter", "DirSize", "FileSearch", "ContentTypeAnalyzer" };

        foreach (var tool in tools)
        {
            var result = await RunToolAsync(tool, $"--path \"{missingPath}\"", expectSuccess: false);
            result.Should().Contain("does not exist", StringComparison.OrdinalIgnoreCase,
                $"{tool} should report missing directory error");
        }
    }

    [Fact]
    public async Task AllTools_SupportVerboseLogging()
    {
        var tools = new[] { "FileCounter", "DirSize", "FileSearch", "ContentTypeAnalyzer" };

        foreach (var tool in tools)
        {
            var result = await RunToolAsync(tool, $"--path \"{_testDirectory}\" --verbose");
            
            // Should complete successfully with verbose output
            result.Should().NotBeNullOrWhiteSpace($"{tool} should produce output with verbose logging");
        }
    }

    [Fact]
    public async Task AllTools_SupportJsonExport()
    {
        var tools = new[] { "FileCounter", "DirSize", "FileSearch", "ContentTypeAnalyzer" };

        foreach (var tool in tools)
        {
            var result = await RunToolAsync(tool, $"--path \"{_testDirectory}\" --format json");
            
            // Should be valid JSON
            var action = () => JsonDocument.Parse(result);
            action.Should().NotThrow($"{tool} should produce valid JSON output");
        }
    }

    [Fact]
    public async Task FileSearch_FindsSpecificExtensions()
    {
        var result = await RunToolAsync("FileSearch", $"--path \"{_testDirectory}\" --pattern *.cs --format json");
        var json = JsonDocument.Parse(result);
        var matches = json.RootElement.GetProperty("totalMatches").GetInt32();

        matches.Should().Be(1, "Should find exactly one .cs file");
    }

    [Fact]
    public async Task DirSize_CalculatesCorrectTotalSize()
    {
        var result = await RunToolAsync("DirSize", $"--path \"{_testDirectory}\" --format json");
        var json = JsonDocument.Parse(result);
        var totalSize = json.RootElement.GetProperty("totalSize").GetInt64();

        totalSize.Should().BeGreaterThan(0, "Directory should have non-zero size");
    }

    private void CreateTestFiles()
    {
        // Create various test files with known content
        File.WriteAllText(Path.Combine(_testDirectory, "test.txt"), "Hello World");
        File.WriteAllText(Path.Combine(_testDirectory, "data.json"), "{\"test\": true}");
        File.WriteAllBytes(Path.Combine(_testDirectory, "image.jpg"), new byte[] { 0xFF, 0xD8, 0xFF }); // JPEG header

        var subDir = Path.Combine(_testDirectory, "subdir");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "nested.cs"), "public class Test { }");

        // Create a larger file for size testing
        var largeContent = new string('X', 1024); // 1KB file
        File.WriteAllText(Path.Combine(_testDirectory, "large.txt"), largeContent);
    }

    private async Task<string> RunToolAsync(string toolName, string arguments, bool expectSuccess = true)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project {toolName}/{toolName}.csproj -- {arguments}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(_testDirectory.TrimEnd(Path.DirectorySeparatorChar)) ?? _testDirectory
        };

        using var process = Process.Start(startInfo);
        if (process == null)
            throw new InvalidOperationException($"Failed to start {toolName}");

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (expectSuccess)
        {
            process.ExitCode.Should().Be(0, $"{toolName} should complete successfully. Error: {error}");
        }

        return string.IsNullOrEmpty(output) ? error : output;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            try
            {
                if (Directory.Exists(_testDirectory))
                {
                    Directory.Delete(_testDirectory, true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
            _disposed = true;
        }
    }
}