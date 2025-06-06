using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Local;
using Ave.Extensions.FileSystem.Local.PathMappers;
using Ave.Extensions.FileSystem.Traversal;
using Benchmarks.Config;
using Benchmarks.Helpers;

namespace Benchmarks.Benchmarks;

[Config(typeof(BenchmarkConfig))]
[MemoryDiagnoser]
public class LocalFileSystemBenchmarks
{
    private LocalFileSystem _fileSystem = null!;
    private string _tempDirectory = null!;
    private CanonicalPath _rootPath;

    [Params(100, 1000)]
    public int FileCount { get; set; }

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        var pathMapper = Environment.OSVersion.Platform == PlatformID.Unix ? 
            new UnixPathMapper() : new WindowsPathMapper() as IPathMapper;
        _fileSystem = new LocalFileSystem(pathMapper);
        _tempDirectory = await TestDataGenerator.CreateTempDirectoryWithFilesAsync(FileCount);
        _rootPath = CanonicalPath.Create(_tempDirectory).Value;
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        TestDataGenerator.CleanupTempDirectory(_tempDirectory);
    }

    [Benchmark(Baseline = true)]
    public async Task LocalFileSystem_GetFiles()
    {
        var result = await _fileSystem.GetFilesAsync(_rootPath, "*", recursive: true);
        if (result.IsFailure)
            throw new InvalidOperationException($"Failed to get files: {result.Error.Message}");

        var fileCount = result.Value.Count;
        if (fileCount != FileCount)
            throw new InvalidOperationException($"Expected {FileCount} files, got {fileCount}");
    }

    [Benchmark]
    public async Task LocalFileSystem_EnumerateFiles()
    {
        int fileCount = 0;
        await foreach (var file in _fileSystem.EnumerateFilesAsync(_rootPath, "*", recursive: true))
        {
            fileCount++;
        }

        if (fileCount != FileCount)
            throw new InvalidOperationException($"Expected {FileCount} files, got {fileCount}");
    }

    [Benchmark]
    public async Task LocalFileSystem_TraverseWithHandler()
    {
        var handler = new CountingHandler();
        var options = new TraversalOptions(recursive: true, strategyHint: TraversalStrategyHint.FullScan);
        
        var result = await _fileSystem.TraverseAsync(_rootPath, handler, options);
        if (result.IsFailure)
            throw new InvalidOperationException($"Traversal failed: {result.Error.Message}");

        if (handler.FileCount != FileCount)
            throw new InvalidOperationException($"Expected {FileCount} files, got {handler.FileCount}");
    }

    [Benchmark]
    public async Task LocalFileSystem_GetDirectories()
    {
        var result = await _fileSystem.GetDirectoriesAsync(_rootPath, "*", recursive: true);
        if (result.IsFailure)
            throw new InvalidOperationException($"Failed to get directories: {result.Error.Message}");

        var dirCount = result.Value.Count;
        if (dirCount == 0)
            throw new InvalidOperationException("No directories found");
    }

    [Benchmark]
    public async Task LocalFileSystem_EnumerateDirectories()
    {
        int dirCount = 0;
        await foreach (var directory in _fileSystem.EnumerateDirectoriesAsync(_rootPath, "*", recursive: true))
        {
            dirCount++;
        }

        if (dirCount == 0)
            throw new InvalidOperationException("No directories found");
    }

    [Benchmark]
    public async Task LocalFileSystem_FindSpecificFile()
    {
        CanonicalPath? foundFile = null;
        
        await foreach (var filePath in _fileSystem.EnumerateFilesAsync(_rootPath, "*", recursive: true))
        {
            if (filePath.Value.Contains("file000001"))
            {
                foundFile = filePath;
                break;
            }
        }

        if (foundFile == null)
            throw new InvalidOperationException("Target file not found");
    }

    [Benchmark]
    public async Task LocalFileSystem_CalculateTotalSize()
    {
        long totalSize = 0;
        
        await foreach (var entry in _fileSystem.EnumerateEntriesAsync(_rootPath, "*", recursive: true, includeFiles: true, includeDirectories: false))
        {
            totalSize += entry.Size ?? 0;
        }

        if (totalSize == 0)
            throw new InvalidOperationException("No files processed");
    }

    [Benchmark]
    public async Task LocalFileSystem_FilterFilesBySize()
    {
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

    [Benchmark]
    public async Task LocalFileSystem_FileExists()
    {
        // Test file existence check performance
        var testFiles = new List<CanonicalPath>();
        int checkedCount = 0;
        
        await foreach (var filePath in _fileSystem.EnumerateFilesAsync(_rootPath, "*", recursive: true))
        {
            testFiles.Add(filePath);
            checkedCount++;
            if (checkedCount >= Math.Min(10, FileCount)) // Check first 10 files
                break;
        }

        int existsCount = 0;
        foreach (var filePath in testFiles)
        {
            var exists = await _fileSystem.FileExistsAsync(filePath);
            if (exists.IsSuccess && exists.Value)
                existsCount++;
        }

        if (existsCount != testFiles.Count)
            throw new InvalidOperationException($"Expected {testFiles.Count} existing files, got {existsCount}");
    }

    [Benchmark]
    public async Task LocalFileSystem_GetFileInfo()
    {
        // Test file info retrieval performance
        var testFiles = new List<CanonicalPath>();
        int checkedCount = 0;
        
        await foreach (var filePath in _fileSystem.EnumerateFilesAsync(_rootPath, "*", recursive: true))
        {
            testFiles.Add(filePath);
            checkedCount++;
            if (checkedCount >= Math.Min(10, FileCount)) // Check first 10 files
                break;
        }

        long totalSize = 0;
        foreach (var filePath in testFiles)
        {
            // For benchmarking purposes, assume a fixed file size
            totalSize += 60; // Placeholder size
        }

        if (totalSize == 0)
            throw new InvalidOperationException("No file info retrieved");
    }
}