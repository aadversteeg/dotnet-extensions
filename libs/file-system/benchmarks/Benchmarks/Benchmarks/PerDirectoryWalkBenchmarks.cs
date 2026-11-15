using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Local;
using Ave.Extensions.FileSystem.Local.PathMappers;
using Benchmarks.Config;

namespace Benchmarks.Benchmarks;

/// <summary>
/// Measures how a recursive cache walk actually uses this library: one non-recursive
/// EnumerateEntriesAsync call per directory, over a real tree on disk.
///
/// The other streaming benchmarks make a single recursive call and amortise whatever the streaming
/// machinery costs to set up and drain across every entry in the tree, so they barely see it. A
/// walk pays that cost once per directory, and a large tree has tens of thousands of directories -
/// which is where a per-call wait turns minutes of work into an hour of it.
/// </summary>
[Config(typeof(BenchmarkConfig))]
[MemoryDiagnoser]
public class PerDirectoryWalkBenchmarks
{
    private LocalFileSystem _fileSystem = null!;
    private string _tempDirectory = null!;
    private List<CanonicalPath> _directories = null!;

    [Params(200, 1000)]
    public int DirectoryCount { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        IPathMapper pathMapper = Environment.OSVersion.Platform == PlatformID.Unix
            ? new UnixPathMapper()
            : new WindowsPathMapper();
        _fileSystem = new LocalFileSystem(pathMapper);

        _tempDirectory = Path.Combine(Path.GetTempPath(), "ave-walk-bench-" + Guid.NewGuid().ToString("N").Substring(0, 12));
        Directory.CreateDirectory(_tempDirectory);

        _directories = new List<CanonicalPath>(DirectoryCount);

        // A wide, shallow tree: the walk visits each directory once regardless of shape, and a flat
        // layout keeps the setup cost off the measurement.
        for (var i = 0; i < DirectoryCount; i++)
        {
            var systemPath = Path.Combine(_tempDirectory, "dir" + i.ToString("D5"));
            Directory.CreateDirectory(systemPath);

            for (var f = 0; f < 3; f++)
            {
                File.WriteAllText(Path.Combine(systemPath, "file" + f + ".txt"), "benchmark content");
            }

            var mapped = pathMapper.MapToCanonicalPath(systemPath);
            if (mapped.IsFailure)
                throw new InvalidOperationException("Failed to map " + systemPath + ": " + mapped.Error.Message);

            _directories.Add(mapped.Value);
        }
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        try
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
        catch
        {
            // Best-effort temp cleanup.
        }
    }

    [Benchmark(Baseline = true)]
    public async Task PerDirectory_GetFiles()
    {
        // The collecting call, as a control: it never went through the streaming handler, so it is
        // the part of the cost the streaming change cannot have moved.
        var count = 0;
        foreach (var directory in _directories)
        {
            var result = await _fileSystem.GetFilesAsync(directory, "*", recursive: false);
            if (result.IsFailure)
                throw new InvalidOperationException("Failed to list " + directory.Value + ": " + result.Error.Message);
            count += result.Value.Count;
        }

        if (count != DirectoryCount * 3)
            throw new InvalidOperationException("Expected " + (DirectoryCount * 3) + " files, got " + count);
    }

    [Benchmark]
    public async Task PerDirectory_EnumerateEntries()
    {
        // Exactly what FileCacheService.WalkRecursiveAsync does per directory.
        var count = 0;
        foreach (var directory in _directories)
        {
            await foreach (var entry in _fileSystem.EnumerateEntriesAsync(
                directory, "*", recursive: false, includeFiles: true, includeDirectories: true))
            {
                count++;
            }
        }

        if (count != DirectoryCount * 3)
            throw new InvalidOperationException("Expected " + (DirectoryCount * 3) + " entries, got " + count);
    }
}
