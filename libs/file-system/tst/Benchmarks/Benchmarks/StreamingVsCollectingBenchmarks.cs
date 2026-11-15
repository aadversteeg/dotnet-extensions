using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.InMemory;
using Benchmarks.Config;
using Benchmarks.Helpers;

namespace Benchmarks.Benchmarks;

[Config(typeof(BenchmarkConfig))]
[MemoryDiagnoser]
public class StreamingVsCollectingBenchmarks
{
    private InMemoryFileSystem _fileSystem = null!;
    private CanonicalPath _rootPath;

    [Params(1000, 10000)]
    public int FileCount { get; set; }

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _fileSystem = await TestDataGenerator.CreateInMemoryFileSystemAsync(FileCount);
        _rootPath = CanonicalPath.Create("/test").Value;
    }

    [Benchmark(Baseline = true)]
    public async Task CollectAllThenProcess()
    {
        // Traditional approach: collect all files first, then process
        var files = await _fileSystem.GetFilesAsync(_rootPath, "*", recursive: true);
        if (files.IsFailure)
            throw new InvalidOperationException($"Failed to get files: {files.Error.Message}");

        long totalSize = 0;
        int processedCount = 0;
        
        foreach (var filePath in files.Value)
        {
            // For benchmarking purposes, assume a fixed file size
            totalSize += 60; // Placeholder size
            processedCount++;
        }

        // Simulate some processing work
        if (processedCount != FileCount)
            throw new InvalidOperationException($"Expected {FileCount} files, got {processedCount}");
    }

    [Benchmark]
    public async Task StreamAndProcess()
    {
        // Streaming approach: process files as they're discovered
        long totalSize = 0;
        int processedCount = 0;

        await foreach (var entry in _fileSystem.EnumerateEntriesAsync(_rootPath, "*", recursive: true, includeFiles: true, includeDirectories: false))
        {
            totalSize += entry.Size ?? 0;
            processedCount++;
        }

        // Simulate some processing work
        if (processedCount != FileCount)
            throw new InvalidOperationException($"Expected {FileCount} files, got {processedCount}");
    }

    [Benchmark]
    public async Task CollectAllFindFirst()
    {
        // Find first file matching pattern using collection approach
        var files = await _fileSystem.GetFilesAsync(_rootPath, "*", recursive: true);
        if (files.IsFailure)
            throw new InvalidOperationException($"Failed to get files: {files.Error.Message}");

        var firstFile = files.Value.FirstOrDefault(f => f.Value.Contains("file000001"));
        if (firstFile == default)
            throw new InvalidOperationException("Target file not found");
    }

    [Benchmark]
    public async Task StreamFindFirst()
    {
        // Find first file matching pattern using streaming approach
        CanonicalPath? firstFile = null;
        
        await foreach (var filePath in _fileSystem.EnumerateFilesAsync(_rootPath, "*", recursive: true))
        {
            if (filePath.Value.Contains("file000001"))
            {
                firstFile = filePath;
                break;
            }
        }

        if (firstFile == null)
            throw new InvalidOperationException("Target file not found");
    }

    [Benchmark]
    public async Task CollectAllCalculateSize()
    {
        // Calculate total size using collection approach
        var files = await _fileSystem.GetFilesAsync(_rootPath, "*", recursive: true);
        if (files.IsFailure)
            throw new InvalidOperationException($"Failed to get files: {files.Error.Message}");

        long totalSize = 0;
        foreach (var filePath in files.Value)
        {
            // For benchmarking purposes, assume a fixed file size
            totalSize += 60; // Placeholder size
        }

        if (totalSize == 0)
            throw new InvalidOperationException("No files processed");
    }

    [Benchmark]
    public async Task StreamCalculateSize()
    {
        // Calculate total size using streaming approach
        long totalSize = 0;
        
        await foreach (var entry in _fileSystem.EnumerateEntriesAsync(_rootPath, "*", recursive: true, includeFiles: true, includeDirectories: false))
        {
            totalSize += entry.Size ?? 0;
        }

        if (totalSize == 0)
            throw new InvalidOperationException("No files processed");
    }

    [Benchmark]
    public async Task CollectAllFilterLargeFiles()
    {
        // Filter large files using collection approach
        var files = await _fileSystem.GetFilesAsync(_rootPath, "*", recursive: true);
        if (files.IsFailure)
            throw new InvalidOperationException($"Failed to get files: {files.Error.Message}");

        var largeFiles = new List<CanonicalPath>();
        foreach (var filePath in files.Value)
        {
            // For benchmarking purposes, assume all files are larger than 50 bytes
            largeFiles.Add(filePath);
        }

        if (largeFiles.Count == 0)
            throw new InvalidOperationException("No large files found");
    }

    [Benchmark]
    public async Task StreamFilterLargeFiles()
    {
        // Filter large files using streaming approach
        var largeFiles = new List<CanonicalPath>();
        
        await foreach (var entry in _fileSystem.EnumerateEntriesAsync(_rootPath, "*", recursive: true, includeFiles: true, includeDirectories: false))
        {
            if ((entry.Size ?? 0) > 50) // Files larger than 50 bytes
            {
                largeFiles.Add(entry.Path);
            }
        }

        if (largeFiles.Count == 0)
            throw new InvalidOperationException("No large files found");
    }
}