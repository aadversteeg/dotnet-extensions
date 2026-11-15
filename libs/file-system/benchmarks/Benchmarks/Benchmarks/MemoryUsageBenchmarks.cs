using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.InMemory;
using Ave.Extensions.FileSystem.Traversal;
using Ave.Extensions.FileSystem.Mapping;
using Benchmarks.Config;
using Benchmarks.Helpers;

namespace Benchmarks.Benchmarks;

[Config(typeof(BenchmarkConfig))]
[MemoryDiagnoser]
public class MemoryUsageBenchmarks
{
    private InMemoryFileSystem _fileSystem = null!;
    private CanonicalPath _rootPath;

    [Params(10000, 100000)]
    public int FileCount { get; set; }

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _fileSystem = await TestDataGenerator.CreateInMemoryFileSystemAsync(FileCount);
        _rootPath = CanonicalPath.Create("/test").Value;
    }

    [Benchmark(Baseline = true)]
    public async Task GetFiles_CollectAll()
    {
        // Traditional approach that collects all files in memory
        var result = await _fileSystem.GetFilesAsync(_rootPath, "*", recursive: true);
        if (result.IsFailure)
            throw new InvalidOperationException($"Failed to get files: {result.Error.Message}");

        var fileCount = result.Value.Count;
        if (fileCount != FileCount)
            throw new InvalidOperationException($"Expected {FileCount} files, got {fileCount}");
    }

    [Benchmark]
    public async Task EnumerateFiles_Streaming()
    {
        // Streaming approach that processes files one by one
        int fileCount = 0;
        await foreach (var file in _fileSystem.EnumerateFilesAsync(_rootPath, "*", recursive: true))
        {
            fileCount++;
        }

        if (fileCount != FileCount)
            throw new InvalidOperationException($"Expected {FileCount} files, got {fileCount}");
    }

    [Benchmark]
    public async Task GetDirectories_CollectAll()
    {
        // Traditional approach that collects all directories in memory
        var result = await _fileSystem.GetDirectoriesAsync(_rootPath, "*", recursive: true);
        if (result.IsFailure)
            throw new InvalidOperationException($"Failed to get directories: {result.Error.Message}");

        var dirCount = result.Value.Count;
        // We expect some directories based on the test data structure
        if (dirCount == 0)
            throw new InvalidOperationException("No directories found");
    }

    [Benchmark]
    public async Task EnumerateDirectories_Streaming()
    {
        // Streaming approach that processes directories one by one
        int dirCount = 0;
        await foreach (var directory in _fileSystem.EnumerateDirectoriesAsync(_rootPath, "*", recursive: true))
        {
            dirCount++;
        }

        if (dirCount == 0)
            throw new InvalidOperationException("No directories found");
    }

    [Benchmark]
    public async Task TraverseWithHandler_MemoryEfficient()
    {
        // Using custom handler with memory-efficient hint
        var handler = new CountingHandler();
        var options = new TraversalOptions(recursive: true, strategyHint: TraversalStrategyHint.MemoryEfficient);
        
        var result = await _fileSystem.TraverseAsync(_rootPath, handler, IdentityMapper.Instance, options);
        if (result.IsFailure)
            throw new InvalidOperationException($"Traversal failed: {result.Error.Message}");

        if (handler.FileCount != FileCount)
            throw new InvalidOperationException($"Expected {FileCount} files, got {handler.FileCount}");
    }

    [Benchmark]
    public async Task TraverseWithHandler_FullScan()
    {
        // Using custom handler with full scan hint
        var handler = new CountingHandler();
        var options = new TraversalOptions(recursive: true, strategyHint: TraversalStrategyHint.FullScan);
        
        var result = await _fileSystem.TraverseAsync(_rootPath, handler, IdentityMapper.Instance, options);
        if (result.IsFailure)
            throw new InvalidOperationException($"Traversal failed: {result.Error.Message}");

        if (handler.FileCount != FileCount)
            throw new InvalidOperationException($"Expected {FileCount} files, got {handler.FileCount}");
    }

    [Benchmark]
    public async Task EnumerateEntries_StreamingWithMetadata()
    {
        // Streaming approach that includes metadata
        int entryCount = 0;
        long totalSize = 0;
        
        await foreach (var entry in _fileSystem.EnumerateEntriesAsync(_rootPath, "*", recursive: true, includeFiles: true, includeDirectories: true))
        {
            entryCount++;
            totalSize += entry.Size ?? 0;
        }

        if (entryCount == 0)
            throw new InvalidOperationException("No entries found");
    }

    [Benchmark]
    public async Task ProcessLargeDataset_CollectFirst()
    {
        // Collect all files first, then process them - simulates high memory usage
        var files = await _fileSystem.GetFilesAsync(_rootPath, "*", recursive: true);
        if (files.IsFailure)
            throw new InvalidOperationException($"Failed to get files: {files.Error.Message}");

        // Create additional data structures
        var fileInfos = new List<(CanonicalPath path, long size)>();
        var sizesByExtension = new Dictionary<string, long>();

        foreach (var filePath in files.Value)
        {
            // For benchmarking purposes, assume a fixed file size
            var size = 60L;
            fileInfos.Add((filePath, size));
            
            var extension = Path.GetExtension(filePath.Value) ?? ".none";
            sizesByExtension[extension] = sizesByExtension.GetValueOrDefault(extension) + size;
        }

        if (fileInfos.Count != FileCount)
            throw new InvalidOperationException($"Expected {FileCount} files, got {fileInfos.Count}");
    }

    [Benchmark]
    public async Task ProcessLargeDataset_Streaming()
    {
        // Stream and process immediately - minimal memory usage
        int processedCount = 0;
        var sizesByExtension = new Dictionary<string, long>();

        await foreach (var entry in _fileSystem.EnumerateEntriesAsync(_rootPath, "*", recursive: true, includeFiles: true, includeDirectories: false))
        {
            processedCount++;
            
            var extension = Path.GetExtension(entry.Path.Value) ?? ".none";
            sizesByExtension[extension] = sizesByExtension.GetValueOrDefault(extension) + (entry.Size ?? 0);
        }

        if (processedCount != FileCount)
            throw new InvalidOperationException($"Expected {FileCount} files, got {processedCount}");
    }
}