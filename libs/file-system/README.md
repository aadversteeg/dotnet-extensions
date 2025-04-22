# Ave.Extensions.FileSystem

Cross-platform filesystem operations for .NET applications running on Windows and Linux.

## Features

- Cross-platform file system operations
- Consistent API across Windows and Linux
- NetStandard 2.0 compatible for maximum compatibility

## Packages

| Package | Description |
|---------|-------------|
| `Ave.Extensions.FileSystem.Abstractions` | Interfaces and abstractions for file system operations |
| `Ave.Extensions.FileSystem` | Implementation of the file system abstractions |

## Getting Started

Install the packages from NuGet:

```shell
dotnet add package Ave.Extensions.FileSystem
```

Basic usage:

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
        if (await _fileSystem.FileExistsAsync(path))
        {
            var content = await _fileSystem.ReadAllTextAsync(path);
            // Process content...
        }
    }
}
```

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
