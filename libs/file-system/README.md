# Ave.Extensions.FileSystem

Cross-platform filesystem operations for .NET applications with advanced path mapping and security features.

## Features

- **Cross-platform file system operations** - Consistent API across Windows, Linux, and macOS
- **Canonical path mapping** - Use Unix-style paths (`/mnt/c/folder/file.txt`) on all platforms
- **Path security validation** - Restrict file access to allowed directories with wildcard support
- **Functional error handling** - Uses `Result<T, Error>` pattern instead of exceptions
- **Async enumeration** - Stream large directory listings efficiently
- **File system monitoring** - Real-time change notifications with reactive streams
- **Dependency injection ready** - Full integration with Microsoft.Extensions.DependencyInjection
- **NetStandard 2.1 compatible** - Works with .NET Core 3.0+ and .NET 5+

## Packages

| Package | Description |
|---------|-------------|
| `Ave.Extensions.FileSystem.Abstractions` | Interfaces and abstractions for file system operations |
| `Ave.Extensions.FileSystem` | Core implementation with path mapping and security features |
| `Ave.Extensions.FileSystem.DependencyInjection` | Dependency injection extensions and configuration |

## Getting Started

Install the packages from NuGet:

```shell
dotnet add package Ave.Extensions.FileSystem
dotnet add package Ave.Extensions.FileSystem.DependencyInjection
```

### Basic Usage

```csharp
using Ave.Extensions.FileSystem;
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
    options.RegisterVisitor = true;
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
dotnet build
```

### Running Tests

```shell
dotnet test
```

### Publishing a New Version

1. Create a new tag with the version number:
   ```
   git tag v1.0.0
   git push origin v1.0.0
   ```

2. The GitHub Actions workflow will automatically:
   - Build and test the code
   - Generate NuGet packages
   - Create a GitHub Release
   - Publish packages to NuGet.org (when configured)

## License

MIT
