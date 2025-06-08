using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using FileSystemSamples.Common.Export;
using FileSystemSamples.Common.Export.Formatters;
using FileSystemSamples.Common.Options;

namespace FileSystemSamples.Common.Benchmarks;

/// <summary>
/// Benchmarks for export performance across different formats and data sizes.
/// </summary>
[MemoryDiagnoser]
[SimpleJob]
public class ExportPerformanceBenchmarks
{
    private TestData _smallData = null!;
    private TestData _mediumData = null!;
    private TestData _largeData = null!;
    
    [GlobalSetup]
    public void Setup()
    {
        _smallData = GenerateTestData(100);
        _mediumData = GenerateTestData(1000);
        _largeData = GenerateTestData(10000);
    }

    [Benchmark]
    [Arguments("Small")]
    [Arguments("Medium")]
    [Arguments("Large")]
    public async Task JsonExport(string size)
    {
        var data = GetDataBySize(size);
        var formatter = new JsonFormatter<TestData>();
        var exporter = new ExporterFactory<TestData>(_ => formatter);
        
        await exporter.ExportAsync(data, ExportFormat.Json);
    }

    [Benchmark]
    [Arguments("Small")]
    [Arguments("Medium")]
    [Arguments("Large")]
    public async Task CsvExport(string size)
    {
        var data = GetDataBySize(size);
        var formatter = new TestDataCsvFormatter();
        var exporter = new ExporterFactory<TestData>(_ => formatter);
        
        await exporter.ExportAsync(data, ExportFormat.Csv);
    }

    [Benchmark]
    [Arguments("Small")]
    [Arguments("Medium")]
    [Arguments("Large")]
    public async Task TextExport(string size)
    {
        var data = GetDataBySize(size);
        var formatter = new TestDataTextFormatter();
        var exporter = new ExporterFactory<TestData>(_ => formatter);
        
        await exporter.ExportAsync(data, ExportFormat.Text);
    }

    private TestData GetDataBySize(string size) => size switch
    {
        "Small" => _smallData,
        "Medium" => _mediumData,
        "Large" => _largeData,
        _ => throw new ArgumentException($"Unknown size: {size}")
    };

    private static TestData GenerateTestData(int count)
    {
        var random = new Random(42); // Fixed seed for consistent results
        var files = Enumerable.Range(0, count)
            .Select(i => new TestFileInfo($"file{i}.txt", random.Next(1024, 1024 * 1024)))
            .ToList();
            
        return new TestData(files, count / 10, files.Sum(f => f.Size));
    }
}

public record TestData(List<TestFileInfo> Files, int DirectoryCount, long TotalSize);
public record TestFileInfo(string Name, long Size);

public class TestDataCsvFormatter : CsvFormatter<TestData>
{
    public override string Format(TestData data)
    {
        return $"Type,Value\nFiles,{data.Files.Count}\nDirectories,{data.DirectoryCount}\nTotal Size,{data.TotalSize}";
    }
}

public class TestDataTextFormatter : TextFormatter<TestData>
{
    public override string Format(TestData data)
    {
        return $"Files: {data.Files.Count:N0}\nDirectories: {data.DirectoryCount:N0}\nTotal Size: {FormatBytes(data.TotalSize)}";
    }
}