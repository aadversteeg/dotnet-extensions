using Ave.Extensions.Functional;
using Ave.Extensions.FileSystem.Models;

namespace Ave.Extensions.FileSystem.Local.PathMappers
{
    /// <summary>
    /// Unix-specific path mapper where canonical paths are already system paths.
    /// </summary>
    public class UnixPathMapper : IPathMapper
    {
        /// <inheritdoc />
        public Result<string, FileSystemError> MapToSystemPath(string canonicalPath)
        {
            // On Unix systems, canonical paths are already system paths
            if (string.IsNullOrWhiteSpace(canonicalPath))
                return Result<string, FileSystemError>.Failure(new FileSystemError("Path cannot be empty"));
                
            return Result<string, FileSystemError>.Success(canonicalPath);
        }
        
        /// <inheritdoc />
        public Result<string, FileSystemError> MapToCanonicalPath(string systemPath)
        {
            // On Unix systems, system paths are already canonical
            if (string.IsNullOrWhiteSpace(systemPath))
                return Result<string, FileSystemError>.Failure(new FileSystemError("Path cannot be empty"));
                
            return Result<string, FileSystemError>.Success(systemPath);
        }
        
        /// <inheritdoc />
        public bool IsValidPathFormat(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;
            
            // Check for null bytes (invalid in Unix paths)
            return !path.Contains('\0');
        }
    }
}