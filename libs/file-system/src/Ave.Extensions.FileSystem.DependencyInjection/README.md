# Ave.Extensions.FileSystem.DependencyInjection

This package provides dependency injection extensions for the Ave.Extensions.FileSystem package.

## Installation

```bash
dotnet add package Ave.Extensions.FileSystem.DependencyInjection
```

## Usage

Register the file system service in your dependency injection container:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Ave.Extensions.FileSystem.DependencyInjection;

// Create a new service collection
var services = new ServiceCollection();

// Register the file system service
services.AddFileSystem();

// Build service provider
var serviceProvider = services.BuildServiceProvider();

// Resolve the file system service
var fileSystem = serviceProvider.GetRequiredService<IFileSystem>();
```

## Dependencies

- Ave.Extensions.FileSystem
- Ave.Extensions.FileSystem.Abstractions
- Microsoft.Extensions.DependencyInjection.Abstractions