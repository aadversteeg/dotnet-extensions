using System;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.InMemory;
using Ave.Extensions.FileSystem.Mapping;
using Ave.Extensions.FileSystem.Traversal;
using Benchmarks.Config;
using Benchmarks.Helpers;

namespace Benchmarks.Benchmarks;

[Config(typeof(BenchmarkConfig))]
[MemoryDiagnoser]
public class TraversalStrategyBenchmarks
{
    private InMemoryFileSystem _fileSystem = null!;
    private CanonicalPath _rootPath;

    [Params(100)]
    public int FileCount { get; set; }

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _fileSystem = await TestDataGenerator.CreateInMemoryFileSystemAsync(FileCount);
        _rootPath = CanonicalPath.Create("/test").Value;
    }

    [Benchmark(Baseline = true)]
    public async Task Sequential_Auto()
    {
        var handler = new CountingHandler();
        var options = new TraversalOptions(recursive: true, strategyHint: TraversalStrategyHint.Auto);
        
        var result = await _fileSystem.TraverseAsync(_rootPath, handler, IdentityMapper.Instance, options);
        if (result.IsFailure)
            throw new InvalidOperationException($"Traversal failed: {result.Error.Message}");
    }

    [Benchmark]
    public async Task Sequential_Search()
    {
        var handler = new CountingHandler();
        var options = new TraversalOptions(recursive: true, strategyHint: TraversalStrategyHint.Search);
        
        var result = await _fileSystem.TraverseAsync(_rootPath, handler, IdentityMapper.Instance, options);
        if (result.IsFailure)
            throw new InvalidOperationException($"Traversal failed: {result.Error.Message}");
    }

    [Benchmark]
    public async Task Sequential_FullScan()
    {
        var handler = new CountingHandler();
        var options = new TraversalOptions(recursive: true, strategyHint: TraversalStrategyHint.FullScan);
        
        var result = await _fileSystem.TraverseAsync(_rootPath, handler, IdentityMapper.Instance, options);
        if (result.IsFailure)
            throw new InvalidOperationException($"Traversal failed: {result.Error.Message}");
    }

    [Benchmark]
    public async Task Sequential_MemoryEfficient()
    {
        var handler = new CountingHandler();
        var options = new TraversalOptions(recursive: true, strategyHint: TraversalStrategyHint.MemoryEfficient);
        
        var result = await _fileSystem.TraverseAsync(_rootPath, handler, IdentityMapper.Instance, options);
        if (result.IsFailure)
            throw new InvalidOperationException($"Traversal failed: {result.Error.Message}");
    }

    [Benchmark]
    public async Task Parallel_Hint()
    {
        var handler = new CountingHandler();
        var options = new TraversalOptions(recursive: true, strategyHint: TraversalStrategyHint.Parallel);
        
        var result = await _fileSystem.TraverseAsync(_rootPath, handler, IdentityMapper.Instance, options);
        if (result.IsFailure)
            throw new InvalidOperationException($"Traversal failed: {result.Error.Message}");
    }

    [Benchmark]
    public async Task TopLevelOnly()
    {
        var handler = new CountingHandler();
        var options = TraversalOptions.TopLevelOnly;
        
        var result = await _fileSystem.TraverseAsync(_rootPath, handler, IdentityMapper.Instance, options);
        if (result.IsFailure)
            throw new InvalidOperationException($"Traversal failed: {result.Error.Message}");
    }

    [Benchmark]
    public async Task MaxDepth_2()
    {
        var handler = new CountingHandler();
        var options = new TraversalOptions(maxDepth: 2, recursive: true, strategyHint: TraversalStrategyHint.Auto);
        
        var result = await _fileSystem.TraverseAsync(_rootPath, handler, IdentityMapper.Instance, options);
        if (result.IsFailure)
            throw new InvalidOperationException($"Traversal failed: {result.Error.Message}");
    }
}