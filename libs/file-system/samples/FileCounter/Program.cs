using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Hosting;
using System.CommandLine.Parsing;
using System.IO;
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

namespace FileCounter
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            var rootCommand = new RootCommand("Count files and directories with filtering options");

            // Options
            var pathOption = new Option<string>(
                aliases: new[] { "--path", "-p" },
                description: "Path to count files in",
                getDefaultValue: () => Directory.GetCurrentDirectory());

            var extensionOption = new Option<string?>(
                aliases: new[] { "--extension", "-e" },
                description: "Filter by file extension (e.g., .cs, .txt)");

            var minSizeOption = new Option<long>(
                aliases: new[] { "--min-size" },
                description: "Minimum file size in bytes",
                getDefaultValue: () => 0);

            var maxSizeOption = new Option<long>(
                aliases: new[] { "--max-size" },
                description: "Maximum file size in bytes",
                getDefaultValue: () => long.MaxValue);

            var recursiveOption = new Option<bool>(
                aliases: new[] { "--recursive", "-r" },
                description: "Count recursively",
                getDefaultValue: () => true);

            rootCommand.AddOption(pathOption);
            rootCommand.AddOption(extensionOption);
            rootCommand.AddOption(minSizeOption);
            rootCommand.AddOption(maxSizeOption);
            rootCommand.AddOption(recursiveOption);

            // Configure host
            var builder = Host.CreateDefaultBuilder(args)
                .ConfigureLogging((context, logging) =>
                {
                    logging.ClearProviders();
                    logging.AddConsole(options =>
                    {
                        // Configure console logger to use stderr
                        options.LogToStandardErrorThreshold = LogLevel.Trace;
                    });
                    logging.SetMinimumLevel(LogLevel.Information);
                })
                .ConfigureServices((context, services) =>
                {
                    // Register services
                    services.AddSingleton<IPathMapper, WindowsPathMapper>();
                    services.AddSingleton<IFileSystem>(sp => 
                        new LocalFileSystem(sp.GetRequiredService<IPathMapper>()));
                    services.AddScoped<FileCounterHandler>();
                });

            // Set handler
            rootCommand.SetHandler(async (context) =>
            {
                var host = context.GetHost();
                var handler = host.Services.GetRequiredService<FileCounterHandler>();
                
                var path = context.ParseResult.GetValueForOption(pathOption)!;
                var extension = context.ParseResult.GetValueForOption(extensionOption);
                var minSize = context.ParseResult.GetValueForOption(minSizeOption);
                var maxSize = context.ParseResult.GetValueForOption(maxSizeOption);
                var recursive = context.ParseResult.GetValueForOption(recursiveOption);

                await handler.ExecuteAsync(path, extension, minSize, maxSize, recursive);
            });

            var parser = new CommandLineBuilder(rootCommand)
                .UseHost(builder.Build)
                .UseDefaults()
                .Build();

            return await parser.InvokeAsync(args);
        }
    }

    public class FileCounterHandler
    {
        private readonly IFileSystem _fileSystem;
        private readonly ILogger<FileCounterHandler> _logger;

        public FileCounterHandler(IFileSystem fileSystem, ILogger<FileCounterHandler> logger)
        {
            _fileSystem = fileSystem;
            _logger = logger;
        }

        public async Task ExecuteAsync(string path, string? extension, long minSize, long maxSize, bool recursive)
        {
            try
            {
                _logger.LogInformation("Starting file count in: {Path}", path);

                // Create canonical path
                var canonicalResult = CanonicalPath.Create(path.Replace('\\', '/').StartsWith('/') ? path.Replace('\\', '/') : "/" + path.Replace('\\', '/'));
                if (canonicalResult.IsFailure)
                {
                    _logger.LogError("Invalid path: {Error}", canonicalResult.Error.Message);
                    return;
                }

                // Create counter handler
                var counter = new CounterHandler(extension, minSize, maxSize);
                var mapper = new DefaultMapper();

                // Configure traversal options
                var options = new TraversalOptions
                {
                    MaxDepth = recursive ? int.MaxValue : 1,
                    FollowSymlinks = false,
                    StrategyHint = TraversalStrategyHint.Sequential
                };

                // Perform traversal
                var result = await _fileSystem.TraverseAsync(
                    canonicalResult.Value,
                    counter,
                    mapper,
                    options);

                if (result.IsSuccess)
                {
                    // Output results to stdout
                    Console.WriteLine($"Files:       {counter.FileCount:N0}");
                    Console.WriteLine($"Directories: {counter.DirectoryCount:N0}");
                    Console.WriteLine($"Total Size:  {FormatSize(counter.TotalSize)}");
                    
                    if (extension != null)
                    {
                        Console.WriteLine($"Matching:    {counter.MatchingCount:N0} files with extension {extension}");
                    }
                }
                else
                {
                    _logger.LogError("Traversal failed: {Error}", result.Error.Message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during file counting");
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
    }

    // Custom traversal handler for counting
    public class CounterHandler : ITraversalHandler<FileSystemEntry>
    {
        private readonly string? _extension;
        private readonly long _minSize;
        private readonly long _maxSize;

        public int FileCount { get; private set; }
        public int DirectoryCount { get; private set; }
        public long TotalSize { get; private set; }
        public int MatchingCount { get; private set; }

        public CounterHandler(string? extension, long minSize, long maxSize)
        {
            _extension = extension;
            _minSize = minSize;
            _maxSize = maxSize;
        }

        public Task<Result<Unit, FileSystemError>> OnTraversalStartAsync(CanonicalPath rootPath, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<Unit, FileSystemError>.Success(Unit.Value));
        }

        public Task<Result<TraversalControl, FileSystemError>> OnDirectoryEnterAsync(FileSystemEntry directory, TraversalContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<TraversalControl, FileSystemError>.Success(TraversalControl.Continue));
        }

        public Task<Result<TraversalControl, FileSystemError>> OnFileAsync(FileSystemEntry file, TraversalContext context, CancellationToken cancellationToken = default)
        {
            if (file is FileEntry fileEntry)
            {
                var size = fileEntry.Info.Size;
                
                // Check size constraints
                if (size >= _minSize && size <= _maxSize)
                {
                    FileCount++;
                    TotalSize += size;

                    // Check extension if specified
                    if (_extension != null && fileEntry.Info.Extension.Equals(_extension, StringComparison.OrdinalIgnoreCase))
                    {
                        MatchingCount++;
                    }
                    else if (_extension == null)
                    {
                        MatchingCount++;
                    }
                }
            }

            return Task.FromResult(Result<TraversalControl, FileSystemError>.Success(TraversalControl.Continue));
        }

        public Task<Result<TraversalControl, FileSystemError>> OnDirectoryAsync(FileSystemEntry directory, TraversalContext context, CancellationToken cancellationToken = default)
        {
            if (context.CurrentDepth > 0) // Don't count the root directory
            {
                DirectoryCount++;
            }
            return Task.FromResult(Result<TraversalControl, FileSystemError>.Success(TraversalControl.Continue));
        }

        public Task<Result<Unit, FileSystemError>> OnTraversalCompleteAsync(TraversalSummary summary, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<Unit, FileSystemError>.Success(Unit.Value));
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
            // Convert canonical path to Windows path
            var windowsPath = canonicalPath.Value.Replace('/', '\\');
            
            // Remove leading backslash if it's not a UNC path
            if (windowsPath.StartsWith("\\") && !windowsPath.StartsWith("\\\\"))
            {
                windowsPath = windowsPath.Substring(1);
            }
            
            return Result<string, Error>.Success(windowsPath);
        }

        public Result<CanonicalPath, Error> MapToCanonicalPath(string systemPath)
        {
            // Convert Windows path to canonical path
            var canonical = systemPath.Replace('\\', '/');
            
            // Ensure it starts with /
            if (!canonical.StartsWith('/'))
            {
                canonical = "/" + canonical;
            }
            
            return CanonicalPath.Create(canonical);
        }
    }
}
