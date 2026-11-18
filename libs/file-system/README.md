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
- **netstandard2.0 and net10.0** - A `netstandard2.0` build for any runtime that implements .NET Standard 2.0, and a `net10.0` build for .NET 10 (see [Supported runtimes](#supported-runtimes))

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

Against the `netstandard2.0` build of the libraries (see the repository README):

```shell
dotnet test extensions.slnx -p:UseNs20=true
```

Against the `netstandard2.1` build of `.InMemory` and `.AzureFileStorage`, with the `netstandard2.0` build of the
other libraries:

```shell
dotnet test extensions.slnx -p:UseNs21=true
```

On Windows, on .NET Framework 4.8 against the `netstandard2.0` build of the libraries:

```shell
dotnet test extensions.slnx -p:UseNet48=true
```

Delete the `bin` and `obj` folders before you switch between these modes (see the repository README).

`UnitTests.Extensions.FileSystem.AzureFileStorage` needs no Azure account, but 9 of its tests call the Azure SDK
against a storage account host name that does not exist and wait for the SDK to give up, so the project takes
about two minutes. Those tests have the trait `Category=Network`; CI leaves them out. To do the same locally:

```shell
dotnet test extensions.slnx --filter "Category!=Network"
```

`IntegrationTests.AzureFileStorage` holds the tests against a real Azure storage account. Without an account its
tests skip themselves. To run them locally, give the account name and key as user secrets (see
`integration-tests/IntegrationTests.AzureFileStorage/USER_SECRETS_SETUP.md`). Use a storage account that is only
for these tests: they empty the file share `integration-tests`.

CI runs them on `main` only, in the three .NET 10 modes below, with the repository secrets
`AZURE_STORAGE_ACCOUNT_NAME` and `AZURE_STORAGE_ACCOUNT_KEY`. That job fails when a secret is missing or a test is
skipped. A pull request does not run them and never gets the account key.

A release of `Ave.Extensions.FileSystem.AzureFileStorage` runs them too, on the release-mode build and in the file
share `integration-tests-release` (see `docs/RELEASING.md`).

### Supported runtimes

The packages target `netstandard2.0` and `net10.0`. `Ave.Extensions.FileSystem.InMemory` and
`Ave.Extensions.FileSystem.AzureFileStorage` also target `netstandard2.1` (see below). What is tested: the test
projects run on .NET 10, in three modes:

- by default, against the `net10.0` build;
- with `-p:UseNs20=true`, against the `netstandard2.0` build of every library;
- with `-p:UseNs21=true`, against the `netstandard2.1` build of `.InMemory` and `.AzureFileStorage` and the
  `netstandard2.0` build of the other libraries. That is the combination an application on .NET Core 3.0 up to
  .NET 9 gets.

On Windows the test projects also run on .NET Framework 4.8, against the `netstandard2.0` build of every library
(`-p:UseNet48=true`).

CI runs the file-system tests in all three .NET 10 modes, on Linux and Windows, and on .NET Framework 4.8 on
Windows. No test runs on .NET Core 3.0 up to .NET 9 itself, or on a .NET Framework older than 4.8, so this
repository makes no promise about a particular version of those runtimes.

### The netstandard2.0 build

The public API is the same as on `net10.0`. One behaviour differs. The write streams of
`Ave.Extensions.FileSystem.InMemory` and `Ave.Extensions.FileSystem.AzureFileStorage` write their content when
they are disposed. On `net10.0` and `netstandard2.1` they override `Stream.DisposeAsync`, so `DisposeAsync()` and
`await using` write the content asynchronously. `netstandard2.0` has no `Stream.DisposeAsync` to override, so there
they implement `IAsyncDisposable` explicitly: disposed through `IAsyncDisposable`, the content is written
asynchronously, as on `net10.0`.

That is why these two packages have a `netstandard2.1` build: .NET Core 3.0 up to .NET 9 load it, so there
`Stream.DisposeAsync()` writes asynchronously as well. Runtimes that support only .NET Standard 2.0, such as .NET
Framework, load the `netstandard2.0` build.

`Dispose()` writes synchronously on every target, as before. For Azure that is a blocking upload.

### Long paths on .NET Framework

On .NET (Core) the runtime handles long paths itself. .NET Framework passes a path to Windows as it is, so there a
file path of 260 characters or more, or a folder path of 248 characters or more, fails unless Windows long paths
are enabled on the machine (`LongPathsEnabled`) and in the manifest of the application.

`Ave.Extensions.FileSystem.Local` makes long paths work on .NET Framework without that machine setting. There it
passes every local path to Windows as a full path with the `\\?\` prefix, which has no such limit. Paths that come
back from Windows lose the prefix again: the paths, entries, events and error messages you get from the library are
the same as without it. On .NET (Core), and on Linux and macOS, the library does not use the prefix.

Limits on .NET Framework:

- An application in legacy path mode does not accept the prefix. That is an application that targets a .NET
  Framework version below 4.6.2, or sets `Switch.System.IO.UseLegacyPathHandling` to `true`. The library detects
  this once per process; there it passes paths as before, and long paths fail as before.
- Network paths (`\\server\share`) get no prefix, so long network paths fail as before.
- `FileSystemWatcher` does not accept the prefix. A monitoring session therefore needs a watched folder path of
  less than 248 characters. Changes below it arrive with their long paths, and the hidden and system filters work
  for them.
- The stream from `OpenReadAsync`, `OpenWriteAsync` or `OpenAsync` is a `FileStream`; its `Name` has the prefix.

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
