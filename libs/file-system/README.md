# Ave.Extensions.FileSystem

Cross-platform filesystem operations for .NET applications with advanced path mapping and security features.

## Features

- **Cross-platform file system operations** - Consistent API across Windows, Linux, and macOS
- **Canonical path mapping** - Use Unix-style paths (`/mnt/c/folder/file.txt`) on all platforms
- **Path security validation** - Restrict file access to allowed directories with wildcard support
- **Functional error handling** - Uses `Result<T, Error>` pattern instead of exceptions
- **Async enumeration** - Stream large directory listings efficiently
- **File system monitoring** - Real-time change notifications with reactive streams
- **High-performance move and copy operations** - Native platform optimizations with progress reporting
- **Dependency injection ready** - Full integration with Microsoft.Extensions.DependencyInjection
- **NetStandard 2.1 compatible** - Works with .NET Core 3.0+ and .NET 5+

## Packages

| Package | Description |
|---------|-------------|
| `Ave.Extensions.FileSystem.Abstractions` | Interfaces and abstractions for file system operations |
| `Ave.Extensions.FileSystem` | Core implementation with path mapping and security features |
| `Ave.Extensions.FileSystem.Local` | Local file system implementation using System.IO |
| `Ave.Extensions.FileSystem.InMemory` | In-memory file system implementation for testing |
| `Ave.Extensions.FileSystem.AzureFileStorage` | Azure File Storage implementation |
| `Ave.Extensions.FileSystem.MimeDetective` | MIME type detection extensions |

## Getting Started

Install the packages from NuGet:

```shell
dotnet add package Ave.Extensions.FileSystem.Local
```

### Basic Usage

```csharp
using Ave.Extensions.FileSystem.Local;
using Microsoft.Extensions.DependencyInjection;

// Setup with DI
services.AddFileSystem();

// Usage
public class MyService 
{
    private readonly IFileSystem _fileSystem;
    
    public MyService(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }
    
    public async Task ProcessFile(string path)
    {
        var result = await _fileSystem.FileExistsAsync(path);
        if (result.IsSuccess && result.Value)
        {
            var contentResult = await _fileSystem.ReadAllTextAsync(path);
            if (contentResult.IsSuccess)
            {
                var content = contentResult.Value;
                // Process content...
            }
        }
    }
}
```

## Move and Copy Operations

The library provides high-performance move and copy operations with native platform optimizations, progress reporting, and comprehensive error handling.

### File Operations

```csharp
// Copy a file
var copyResult = await fileSystem.CopyFileAsync(
    source: "/data/original.txt",
    destination: "/backup/copy.txt",
    overwrite: true
);

if (copyResult.IsSuccess)
{
    Console.WriteLine("File copied successfully");
}

// Move a file
var moveResult = await fileSystem.MoveFileAsync(
    source: "/temp/upload.dat",
    destination: "/processed/upload.dat",
    overwrite: false
);
```

### Directory Operations

```csharp
// Copy entire directory structure
var copyDirResult = await fileSystem.CopyDirectoryAsync(
    source: "/source/project",
    destination: "/backup/project"
);

// Move directory
var moveDirResult = await fileSystem.MoveDirectoryAsync(
    source: "/temp/staging",
    destination: "/production/live"
);
```

### Progress Reporting

Monitor progress for large file operations:

```csharp
using Ave.Extensions.FileSystem;

// Create progress reporter
var progress = new Progress<ICopyProgress>(p => 
{
    Console.WriteLine($"Progress: {p.BytesTransferred:N0} / {p.TotalBytes:N0} bytes " +
                     $"({p.PercentComplete:F1}%) - {p.TransferRate:N0} bytes/sec");
});

// Copy with progress reporting
var result = await fileSystem.CopyFileAsync(
    source: "/data/large-file.zip",
    destination: "/backup/large-file.zip",
    progress: progress
);
```

### Advanced Copy/Move Features

#### Cross-Volume Operations

The library automatically detects and handles cross-volume moves efficiently:

```csharp
// Cross-volume move (automatically uses copy + delete)
var result = await fileSystem.MoveFileAsync(
    source: "/mnt/c/data/file.txt",     // C: drive
    destination: "/mnt/d/backup/file.txt" // D: drive
);
```

#### Cancellation Support

All operations support cancellation tokens:

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

try
{
    var result = await fileSystem.CopyDirectoryAsync(
        source: "/large/dataset",
        destination: "/backup/dataset",
        cancellationToken: cts.Token
    );
}
catch (OperationCanceledException)
{
    Console.WriteLine("Operation was cancelled");
}
```

#### File Attribute Preservation

File attributes, timestamps, and metadata are preserved when supported by the platform:

```csharp
// Attributes are automatically preserved
var result = await fileSystem.CopyFileAsync(
    source: "/data/important.doc",
    destination: "/backup/important.doc"
);

// The copied file will maintain:
// - Creation time
// - Last modified time
// - File attributes (ReadOnly, Hidden, etc.)
// - Extended attributes (when supported)
```

### Error Handling

All operations return `Result<T, FileSystemError>` for functional error handling:

```csharp
var result = await fileSystem.CopyFileAsync(source, destination);

if (result.IsFailure)
{
    switch (result.Error.Type)
    {
        case FileSystemErrorType.FileNotFound:
            Console.WriteLine($"Source file not found: {result.Error.Path}");
            break;
        case FileSystemErrorType.InsufficientSpace:
            Console.WriteLine("Not enough disk space for copy operation");
            break;
        case FileSystemErrorType.AccessDenied:
            Console.WriteLine("Permission denied");
            break;
        case FileSystemErrorType.FileAlreadyExists:
            Console.WriteLine("Destination already exists (use overwrite: true)");
            break;
        default:
            Console.WriteLine($"Copy failed: {result.Error.Message}");
            break;
    }
}
```

### Platform-Specific Optimizations

#### Local File System
- **Windows**: Uses native `MoveFileEx` API for atomic moves within same volume
- **Linux/macOS**: Uses `rename()` system call for atomic moves within same filesystem
- **Cross-volume**: Automatically falls back to copy + delete with transactional cleanup

#### Azure File Storage
- **Server-side copy**: Uses Azure's native copy operations for efficient transfers
- **Parallel uploads**: Large files are transferred using optimized chunking
- **Eventual consistency**: Handles Azure's consistency model for directory operations

#### In-Memory File System
- **Zero-copy moves**: Memory-based moves are instantaneous within same volume
- **Copy-on-write**: Efficient copying using reference counting where possible

### Extension Methods

Convenient extension methods are available in the `Ave.Extensions.FileSystem` namespace:

```csharp
using Ave.Extensions.FileSystem;

// Generic move/copy operations (auto-detects file vs directory)
var result = await fileSystem.MoveAsync(source, destination, overwrite: true);
var result = await fileSystem.CopyAsync(source, destination, overwrite: false);

// Bulk operations with LINQ
var files = new[] { "/temp/file1.txt", "/temp/file2.txt", "/temp/file3.txt" };
var results = await files.ToAsyncEnumerable()
    .SelectAwait(async file => await fileSystem.MoveFileAsync(
        file, 
        file.Replace("/temp/", "/processed/")
    ))
    .ToListAsync();
```

### Canonical Path Mapping

Use Unix-style paths across all platforms:

```csharp
// Setup with canonical path mapping
services.AddFileSystem(options =>
{
    options.UseCanonicalPaths = true;
});

// Now you can use Unix-style paths on Windows
var result = await fileSystem.ReadAllTextAsync("/mnt/c/users/documents/file.txt");
// Automatically maps to C:\users\documents\file.txt on Windows
```

### Path Security

Restrict file system access to specific directories:

```csharp
services.AddFileSystem(options =>
{
    options.EnablePathSecurity = true;
    options.AllowedPaths.Add("/app/data");
    options.AllowedPaths.Add("/tmp/*");
    options.AllowedPaths.Add("/logs");
});

// Only paths under /app/data, /tmp/*, and /logs are accessible
```

### Advanced Configuration

```csharp
services.AddFileSystem(options =>
{
    // Enable canonical path mapping
    options.UseCanonicalPaths = true;
    
    // Enable security validation
    options.EnablePathSecurity = true;
    options.AllowedPaths.Add("/app/**");
    options.AllowedPaths.Add("/data/uploads/*");
    
    // Customize other features
    options.EnableMonitoring = true;
    options.DefaultBufferSize = 8192;
});
```

## Path Mapping Reference

When canonical path mapping is enabled, the library automatically converts between platform-specific and Unix-style paths:

### Windows Path Mapping

| Canonical Path | Windows System Path | Description |
|----------------|-------------------|-------------|
| `/mnt/c/folder/file.txt` | `C:\folder\file.txt` | Primary drive mapping (WSL-style) |
| `/mnt/d/data/logs` | `D:\data\logs` | Alternative drive |
| `/c/folder/file.txt` | `C:\folder\file.txt` | Legacy format (still supported for input) |
| `//server/share/file` | `\\server\share\file` | UNC paths |

### Unix Path Mapping

On Unix systems (Linux/macOS), canonical paths are used directly:

| Canonical Path | Unix System Path |
|----------------|------------------|
| `/home/user/file.txt` | `/home/user/file.txt` |
| `/var/log/app.log` | `/var/log/app.log` |

### Relative Path Resolution

When integrating with external systems — such as RPC clients, CLI tools, or remote file browsers — paths are often relative to a working directory rather than absolute system paths. For example, an RPC client might send `.` to list the current directory, or `../sibling/file.txt` to reference a file relative to its current location. These relative paths cannot be mapped directly to canonical paths because `CanonicalPath.Create()` requires absolute paths starting with `/`.

The library provides two tools to handle this:

**`SystemPath` — Detect whether a path is absolute or relative**

A platform-independent helper that recognizes both Windows and Unix absolute path patterns regardless of the current OS:

```csharp
using Ave.Extensions.FileSystem;

SystemPath.IsAbsolute("/home/user");      // true  (Unix)
SystemPath.IsAbsolute(@"C:\Users");       // true  (Windows drive)
SystemPath.IsAbsolute(@"\\server\share"); // true  (UNC)
SystemPath.IsAbsolute("src/file.txt");    // false (relative)
SystemPath.IsAbsolute("./file.txt");      // false (dot-relative)
SystemPath.IsAbsolute("../file.txt");     // false (parent-relative)

SystemPath.IsRelative("src/file.txt");    // true
SystemPath.IsRelative("/home/user");      // false
```

**`IPathMapper.MapToCanonicalPath(relativePath, basePath)` — Resolve relative paths**

Resolves a relative path against a base canonical path, handling `.`, `..`, and both `/` and `\` separators:

```csharp
var workingDirectory = CanonicalPath.Create("/home/user/projects").Value;

// Simple navigation
pathMapper.MapToCanonicalPath("src/main.cs", workingDirectory);
// => /home/user/projects/src/main.cs

// Current directory
pathMapper.MapToCanonicalPath(".", workingDirectory);
// => /home/user/projects

// Parent navigation
pathMapper.MapToCanonicalPath("../other/file.txt", workingDirectory);
// => /home/user/other/file.txt

// Escaping root returns an error
pathMapper.MapToCanonicalPath("../../../../..", workingDirectory);
// => Failure (RelativePathEscapesRoot)
```

A typical pattern for handling mixed input paths:

```csharp
Result<CanonicalPath, Error> ResolvePath(string inputPath, CanonicalPath workingDirectory)
{
    if (SystemPath.IsAbsolute(inputPath))
        return _pathMapper.MapToCanonicalPath(inputPath);

    return _pathMapper.MapToCanonicalPath(inputPath, workingDirectory);
}
```

### Security Pattern Examples

| Pattern | Matches | Description |
|---------|---------|-------------|
| `/app/data` | `/app/data` and `/app/data/subfolder/file.txt` | Exact path and subdirectories |
| `/tmp/*` | `/tmp/file.txt` but not `/tmp/sub/file.txt` | Single level wildcard |
| `/logs/**` | All files under `/logs` recursively | Recursive wildcard |
| `/config/*.json` | `/config/app.json` but not `/config/sub/app.json` | File extension pattern |

## Development

### Building

```shell
cd libs/file-system
dotnet build extensions.slnx
```

### Running Tests

```shell
cd libs/file-system
dotnet test extensions.slnx
```

### Running Benchmarks

The repository includes performance benchmarks using BenchmarkDotNet:

```shell
cd libs/file-system/benchmarks/Benchmarks && dotnet run -c Release
```

Available benchmark classes:
- `LocalFileSystemBenchmarks` - Local file system operation performance
- `MemoryUsageBenchmarks` - Memory allocation analysis
- `StreamingVsCollectingBenchmarks` - Streaming vs collecting approach comparison
- `TraversalStrategyBenchmarks` - Directory traversal strategy comparison

Run a specific benchmark:
```shell
cd libs/file-system/benchmarks/Benchmarks && dotnet run -c Release -- --filter "*LocalFileSystem*"
```

### Running Platform Tests

Platform-specific tests verify cross-platform behavior:

```shell
cd libs/file-system/integration-tests/PlatformTests.Local && dotnet test
```

### Publishing a New Version

Each package is released on its own. See
[docs/RELEASING.md](https://github.com/aadversteeg/dotnet-extensions/blob/main/docs/RELEASING.md).

## License

MIT
