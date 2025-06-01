# Migration Plan: IFileSystem String to CanonicalPath

## Overview
This plan outlines the migration of all path parameters in IFileSystem and IReadOnlyFileSystem interfaces from `string` to `CanonicalPath` value object.

## Impact Scope

### Interfaces to Modify
1. **IReadOnlyFileSystem** (9 methods with path parameters)
   - FileExistsAsync(string path, ...)
   - DirectoryExistsAsync(string path, ...)
   - ReadAllTextAsync(string path, ...)
   - OpenReadAsync(string path, ...)
   - GetFileMetadataAsync(string path, ...)
   - GetDirectoryMetadataAsync(string path, ...)
   - GetFilesAsync(string path, ...) - returns string[]
   - GetDirectoriesAsync(string path, ...) - returns string[]
   - EnumerateFileSystemEntriesAsync(string path, ...)

2. **IFileSystem** (6 methods with path parameters)
   - CreateDirectoryAsync(string path, ...)
   - WriteAllTextAsync(string path, ...)
   - OpenWriteAsync(string path, ...)
   - OpenAsync(string path, ...)
   - DeleteFileAsync(string path, ...)
   - DeleteDirectoryAsync(string path, ...)

### Implementations to Update
1. LocalFileSystem
2. InMemoryFileSystem
3. AzureFileSystem

### Supporting Components
1. FileSystemExtensions
2. FileSystemVisitor
3. ReadOnlyFileSystemWrapper
4. IFileSystemMonitor
5. FileSystemEntry model
6. Unit tests (400+ references)

## Migration Strategy

### Phase 1: Create Supporting Infrastructure
1. **Create conversion utilities**
   - Extension methods for string to CanonicalPath conversion
   - Result<CanonicalPath, Error> handling utilities
   - Backward compatibility helpers

2. **Update models**
   - Modify FileSystemEntry to use CanonicalPath
   - Update DirectoryMetadata and FileMetadata if they contain paths

### Phase 2: Interface Migration
1. **Create new interfaces with CanonicalPath**
   - IReadOnlyFileSystemV2 (temporary name)
   - IFileSystemV2 (temporary name)
   - Keep original interfaces for backward compatibility

2. **Implement adapter pattern**
   - Create adapters that convert between string and CanonicalPath versions
   - This allows gradual migration of consumers

### Phase 3: Implementation Migration
1. **Update each implementation**
   - Start with InMemoryFileSystem (simplest)
   - Then LocalFileSystem
   - Finally AzureFileSystem (most complex)

2. **Update each implementation to:**
   - Implement both old and new interfaces
   - Internal methods use CanonicalPath
   - Old interface methods convert and delegate to new methods

### Phase 4: Consumer Migration
1. **Update direct consumers**
   - FileSystemExtensions
   - FileSystemVisitor
   - ReadOnlyFileSystemWrapper

2. **Update tests**
   - Create test helpers for CanonicalPath creation
   - Migrate tests incrementally

### Phase 5: Cleanup
1. **Remove old interfaces**
   - Mark old interfaces as obsolete
   - Provide migration guide
   - Eventually remove after deprecation period

## Implementation Details

### Key Considerations

1. **Error Handling**
   - Path validation moves to interface boundary
   - Invalid paths fail fast with proper error messages
   - Consider creating CanonicalPath.TryCreate for convenience

2. **Return Values**
   - Methods returning string[] (GetFilesAsync, GetDirectoriesAsync) should return CanonicalPath[]
   - Consider IReadOnlyList<CanonicalPath> for better API design

3. **Performance**
   - CanonicalPath caches hash codes for efficient comparison
   - Consider caching validated paths in implementations
   - Minimize string allocations

4. **Platform-Specific Handling**
   - LocalFileSystem needs path mapping between canonical and OS paths
   - Use IPathMapper interface for conversion
   - Consider Windows vs Unix path differences

### Code Examples

#### Before:
```csharp
Task<Result<bool, FileSystemError>> FileExistsAsync(string path, CancellationToken cancellationToken = default);
```

#### After:
```csharp
Task<Result<bool, FileSystemError>> FileExistsAsync(CanonicalPath path, CancellationToken cancellationToken = default);
```

#### Migration Helper:
```csharp
public static class FileSystemMigrationExtensions
{
    public static async Task<Result<bool, FileSystemError>> FileExistsAsync(
        this IFileSystem fileSystem, 
        string path, 
        CancellationToken cancellationToken = default)
    {
        var canonicalPathResult = CanonicalPath.Create(path);
        if (canonicalPathResult.IsFailure)
        {
            return Result<bool, FileSystemError>.Failure(
                new FileSystemError(canonicalPathResult.Error));
        }
        
        return await fileSystem.FileExistsAsync(
            canonicalPathResult.Value, 
            cancellationToken);
    }
}
```

## Risks and Mitigation

1. **Breaking Changes**
   - Risk: All consumers need updates
   - Mitigation: Adapter pattern and extension methods for compatibility

2. **Performance Impact**
   - Risk: Additional validation overhead
   - Mitigation: Validation only at API boundary, caching

3. **Complex Migration**
   - Risk: Large codebase makes migration error-prone
   - Mitigation: Incremental approach, comprehensive testing

## Success Criteria

1. All path parameters use CanonicalPath
2. No runtime path validation errors
3. Performance impact < 5%
4. All tests passing
5. Clear migration path for consumers

## Timeline Estimate

- Phase 1: 2-3 days
- Phase 2: 3-4 days
- Phase 3: 5-7 days
- Phase 4: 7-10 days
- Phase 5: 2-3 days

Total: 19-27 days of development work
