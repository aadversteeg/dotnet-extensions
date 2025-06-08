using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Hosting;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Local;
using Ave.Extensions.FileSystem.Mapping;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Traversal;
using Ave.Extensions.Functional;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace DirSize
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            var rootCommand = new RootCommand("Calculate directory sizes recursively");

            // Options
            var pathOption = new Option<string>(
                aliases: new[] { "--path", "-p" },
                description: "Path to analyze",
                getDefaultValue: () => Directory.GetCurrentDirectory());

            var topOption = new Option<int>(
                aliases: new[] { "--top", "-t" },
                description: "Show top N largest directories",
                getDefaultValue: () => 10);

            var minSizeOption = new Option<string?>(
                aliases: new[] { "--min-size", "-m" },
                description: "Minimum size to display (e.g., 1MB, 100KB)");

            var depthOption = new Option<int>(
                aliases: new[] { "--depth", "-d" },
                description: "Maximum depth to analyze",
                getDefaultValue: () => int.MaxValue);

            var sortOption = new Option<SortBy>(
                aliases: new[] { "--sort", "-s" },
                description: "Sort results by",
                getDefaultValue: () => SortBy.Size);

            rootCommand.AddOption(pathOption);
            rootCommand.AddOption(topOption);
            rootCommand.AddOption(minSizeOption);
            rootCommand.AddOption(depthOption);
            rootCommand.AddOption(sortOption);

            // Configure host
            var builder = Host.CreateDefaultBuilder(args)
                .ConfigureLogging((context, logging) =>
                {
                    logging.ClearProviders();
                    logging.AddConsole(options =>
                    {
                        options.LogToStandardErrorThreshold = LogLevel.Trace;
                    });
                    logging.SetMinimumLevel(LogLevel.Information);
                })
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton<IPathMapper, WindowsPathMapper>();
                    services.AddSingleton<IFileSystem>(sp => 
                        new LocalFileSystem(sp.GetRequiredService<IPathMapper>()));
                    services.AddScoped<DirSizeHandler>();
                });

            // Set handler
            rootCommand.SetHandler(async (context) =>
            {
                var host = context.GetHost();
                var handler = host.Services.GetRequiredService<DirSizeHandler>();
                
                var path = context.ParseResult.GetValueForOption(pathOption)!;
                var top = context.ParseResult.GetValueForOption(topOption);
                var minSizeStr = context.ParseResult.GetValueForOption(minSizeOption);
                var depth = context.ParseResult.GetValueForOption(depthOption);
                var sortBy = context.ParseResult.GetValueForOption(sortOption);

                var minSize = ParseSize(minSizeStr);

                await handler.ExecuteAsync(path, top, minSize, depth, sortBy);
            });

            var parser = new CommandLineBuilder(rootCommand)
                .UseHost(builder.Build)
                .UseDefaults()
                .Build();

            return await parser.InvokeAsync(args);
        }

        private static long ParseSize(string? sizeStr)
        {
            if (string.IsNullOrEmpty(sizeStr))
                return 0;

            sizeStr = sizeStr.ToUpper();
            
            var multipliers = new Dictionary<string, long>
            {
                ["B"] = 1,
                ["KB"] = 1024,
                ["MB"] = 1024 * 1024,
                ["GB"] = 1024L * 1024 * 1024,
                ["TB"] = 1024L * 1024 * 1024 * 1024
            };

            foreach (var (suffix, multiplier) in multipliers)
            {
                if (sizeStr.EndsWith(suffix))
                {
                    var numberStr = sizeStr.Substring(0, sizeStr.Length - suffix.Length);
                    if (double.TryParse(numberStr, out var number))
                    {
                        return (long)(number * multiplier);
                    }
                }
            }

            // Try parsing as plain number (bytes)
            if (long.TryParse(sizeStr, out var bytes))
            {
                return bytes;
            }

            return 0;
        }
    }

    public enum SortBy
    {
        Size,
        Name,
        FileCount
    }

    public class DirSizeHandler
    {
        private readonly IFileSystem _fileSystem;
        private readonly ILogger<DirSizeHandler> _logger;

        public DirSizeHandler(IFileSystem fileSystem, ILogger<DirSizeHandler> logger)
        {
            _fileSystem = fileSystem;
            _logger = logger;
        }

        public async Task ExecuteAsync(string path, int top, long minSize, int maxDepth, SortBy sortBy)
        {
            try
            {
                _logger.LogInformation("Analyzing directory sizes in: {Path}", path);

                // Create canonical path
                var canonicalResult = CanonicalPath.Create(path.Replace('\\', '/').StartsWith('/') ? path.Replace('\\', '/') : "/" + path.Replace('\\', '/'));
                if (canonicalResult.IsFailure)
                {
                    _logger.LogError("Invalid path: {Error}", canonicalResult.Error.Message);
                    return;
                }

                // Create size calculator handler
                var calculator = new SizeCalculatorHandler();
                var mapper = new DefaultMapper();

                // Configure traversal options
                var options = new TraversalOptions
                {
                    MaxDepth = maxDepth,
                    FollowSymlinks = false,
                    StrategyHint = TraversalStrategyHint.Sequential
                };

                // Perform traversal
                var result = await _fileSystem.TraverseAsync(
                    canonicalResult.Value,
                    calculator,
                    mapper,
                    options);

                if (result.IsSuccess)
                {
                    // Sort and filter results
                    var directories = calculator.DirectorySizes
                        .Where(d => d.Value.TotalSize >= minSize)
                        .Select(d => new DirectoryInfo(d.Key, d.Value.TotalSize, d.Value.FileCount));

                    // Apply sorting
                    directories = sortBy switch
                    {
                        SortBy.Size => directories.OrderByDescending(d => d.Size),
                        SortBy.Name => directories.OrderBy(d => d.Path),
                        SortBy.FileCount => directories.OrderByDescending(d => d.FileCount),
                        _ => directories.OrderByDescending(d => d.Size)
                    };

                    // Take top N
                    var topDirectories = directories.Take(top).ToList();

                    // Output results to stdout
                    Console.WriteLine($"{"Size",-15} {"Files",-10} Directory");
                    Console.WriteLine(new string('-', 60));

                    foreach (var dir in topDirectories)
                    {
                        Console.WriteLine($"{FormatSize(dir.Size),-15} {dir.FileCount,-10} {dir.Path}");
                    }

                    Console.WriteLine();
                    Console.WriteLine($"Total: {FormatSize(calculator.TotalSize)} in {calculator.TotalFiles:N0} files");
                }
                else
                {
                    _logger.LogError("Traversal failed: {Error}", result.Error.Message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during size calculation");
            }
        }

        private static string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private record DirectoryInfo(string Path, long Size, int FileCount);
    }

    // Custom traversal handler for calculating sizes
    public class SizeCalculatorHandler : ITraversalHandler<FileSystemEntry>
    {
        private readonly Dictionary<string, DirectoryData> _directorySizes = new();
        private readonly Stack<string> _directoryStack = new();

        public IReadOnlyDictionary<string, DirectoryData> DirectorySizes => _directorySizes;
        public long TotalSize { get; private set; }
        public int TotalFiles { get; private set; }

        public Task<Result<Unit, FileSystemError>> OnTraversalStartAsync(CanonicalPath rootPath, CancellationToken cancellationToken = default)
        {
            _directoryStack.Push(rootPath.Value);
            _directorySizes[rootPath.Value] = new DirectoryData();
            return Task.FromResult(Result<Unit, FileSystemError>.Success(Unit.Value));
        }

        public Task<Result<TraversalControl, FileSystemError>> OnDirectoryEnterAsync(FileSystemEntry directory, TraversalContext context, CancellationToken cancellationToken = default)
        {
            _directoryStack.Push(directory.Path.Value);
            _directorySizes[directory.Path.Value] = new DirectoryData();
            return Task.FromResult(Result<TraversalControl, FileSystemError>.Success(TraversalControl.Continue));
        }

        public Task<Result<TraversalControl, FileSystemError>> OnFileAsync(FileSystemEntry file, TraversalContext context, CancellationToken cancellationToken = default)
        {
            if (file is FileEntry fileEntry)
            {
                var size = fileEntry.Info.Size;
                TotalSize += size;
                TotalFiles++;

                // Add size to all parent directories
                foreach (var dir in _directoryStack)
                {
                    if (_directorySizes.TryGetValue(dir, out var data))
                    {
                        data.TotalSize += size;
                        data.FileCount++;
                    }
                }
            }

            return Task.FromResult(Result<TraversalControl, FileSystemError>.Success(TraversalControl.Continue));
        }

        public Task<Result<TraversalControl, FileSystemError>> OnDirectoryAsync(FileSystemEntry directory, TraversalContext context, CancellationToken cancellationToken = default)
        {
            // Pop the directory when we're done with it
            if (_directoryStack.Count > 0 && _directoryStack.Peek() == directory.Path.Value)
            {
                _directoryStack.Pop();
            }
            return Task.FromResult(Result<TraversalControl, FileSystemError>.Success(TraversalControl.Continue));
        }

        public Task<Result<Unit, FileSystemError>> OnTraversalCompleteAsync(TraversalSummary summary, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<Unit, FileSystemError>.Success(Unit.Value));
        }

        public class DirectoryData
        {
            public long TotalSize { get; set; }
            public int FileCount { get; set; }
        }
    }

    // Simple mapper that returns FileSystemEntry as-is
    public class DefaultMapper : IFileSystemEntryMapper<FileSystemEntry>
    {
        public Task<Result<FileSystemEntry, FileSystemError>> MapFileAsync(FileEntry file, IFileReader fileReader, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<FileSystemEntry, FileSystemError>.Success(file));
        }

        public Task<Result<FileSystemEntry, FileSystemError>> MapDirectoryAsync(DirectoryEntry directory, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<FileSystemEntry, FileSystemError>.Success(directory));
        }
    }

    // Windows path mapper implementation
    public class WindowsPathMapper : IPathMapper
    {
        public Result<string, Error> MapToSystemPath(CanonicalPath canonicalPath)
        {
            var windowsPath = canonicalPath.Value.Replace('/', '\\');
            if (windowsPath.StartsWith("\\") && !windowsPath.StartsWith("\\\\"))
            {
                windowsPath = windowsPath.Substring(1);
            }
            return Result<string, Error>.Success(windowsPath);
        }

        public Result<CanonicalPath, Error> MapToCanonicalPath(string systemPath)
        {
            var canonical = systemPath.Replace('\\', '/');
            if (!canonical.StartsWith('/'))
            {
                canonical = "/" + canonical;
            }
            return CanonicalPath.Create(canonical);
        }
    }
}
