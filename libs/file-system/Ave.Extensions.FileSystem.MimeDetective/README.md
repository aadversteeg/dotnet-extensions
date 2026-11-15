# Ave.Extensions.FileSystem.MimeDetective

Content type detection extension for Ave.Extensions.FileSystem using the MimeDetective library.

## Project Status

This project provides a foundation for integrating MIME type detection into the Ave.Extensions.FileSystem framework. The implementation includes:

### Completed Components

1. **Project Structure** - Complete project setup with proper dependencies
2. **Core Models** - `FileSystemEntryWithContentType` record that extends file system entries with content type information
3. **Configuration** - `ContentTypeMapperOptions` for configuring detection behavior
4. **Mapping Interface** - `ContentTypeMapper` that implements `IFileSystemEntryMapper<FileSystemEntryWithContentType>`
5. **Traversal Handlers** - Handlers for filtering files by content type and category
6. **Service Registration** - Dependency injection setup
7. **Extension Methods** - Convenience methods for common operations

### Key Features

- **Content Type Detection**: Detect MIME types from file content using MimeDetective
- **Category-Based Filtering**: Filter files by categories (Image, Document, Archive, etc.)
- **Extension Fallback**: Fallback to extension-based detection when content detection fails
- **Confidence Scoring**: Provides confidence levels for detections
- **Configurable Options**: Customizable detection parameters
- **Async/Streaming Support**: Supports async enumeration for large directory operations

### Basic Usage

```csharp
// Get a single file with content type
var result = await fileSystem.GetFileWithContentTypeAsync("/path/to/file.pdf");
if (result.IsSuccess)
{
    Console.WriteLine($"Content-Type: {result.Value.ContentType}");
    Console.WriteLine($"Confidence: {result.Value.Confidence}");
}

// Enumerate files with content types
await foreach (var file in fileSystem.EnumerateFilesWithContentTypeAsync("/documents"))
{
    Console.WriteLine($"{file.Name}: {file.ContentType}");
}

// Filter by content type
var pdfFiles = await fileSystem.GetFilesByContentTypeAsync("/documents", "application/pdf");

// Filter by category
var images = await fileSystem.GetFilesByCategoryAsync("/photos", FileCategory.Image);
```

### Configuration Options

```csharp
var options = new ContentTypeMapperOptions
{
    MaxBytesToRead = 8192,              // Maximum bytes to read for detection
    FallbackToExtension = true,         // Use extension when content detection fails
    DefaultContentType = "application/octet-stream",
    DefinitionPack = DefinitionPack.Default
};

var result = await fileSystem.GetFileWithContentTypeAsync("/file.txt", options);
```

### Known Issues & Next Steps

The current implementation has some API compatibility issues that need to be resolved:

1. **MimeDetective API Integration** - Need to properly configure the MimeDetective library API
2. **Unit Test Compilation** - Some unit tests need API fixes
3. **Performance Optimization** - Caching and batch processing improvements
4. **Extended Category Support** - More sophisticated category detection

### Dependencies

- **MimeDetective 22.7.15** - Core MIME type detection library
- **Ave.Extensions.FileSystem** - Base file system abstraction
- **Ave.Extensions.Functional** - Functional programming utilities
- **Microsoft.Extensions.DependencyInjection** - Dependency injection support

### Extension Points

The design allows for easy extension:

- **Custom Categories** - Extend the `FileCategory` enum
- **Custom Mappers** - Implement `IFileSystemEntryMapper<T>` for custom data
- **Custom Handlers** - Create specialized traversal handlers
- **Configuration Providers** - Add environment-specific options

### Architecture

The implementation follows the established patterns in Ave.Extensions.FileSystem:

- **Mapper Pattern** - Uses `IFileSystemEntryMapper<T>` for type conversion
- **Traversal Pattern** - Integrates with `ITraversalHandler<T>` for directory processing
- **Result Pattern** - Uses `Result<T, TError>` for error handling
- **Async Enumeration** - Supports `IAsyncEnumerable<T>` for streaming operations

This foundation provides a solid base for MIME type detection while maintaining consistency with the existing file system abstractions.