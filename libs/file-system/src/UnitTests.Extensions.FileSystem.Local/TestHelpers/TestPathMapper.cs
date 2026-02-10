using Ave.Extensions.ErrorPaths;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Abstractions;
using Ave.Extensions.Functional;

namespace UnitTests.Extensions.FileSystem.Local.TestHelpers
{
    public class TestPathMapper : IPathMapper
    {
        public Result<string, Error> MapToSystemPath(CanonicalPath canonicalPath)
        {
            // For testing, just return the canonical path value
            // In real implementation, this would handle OS-specific conversions
            return Result<string, Error>.Success(canonicalPath.Value);
        }
        
        public Result<CanonicalPath, Error> MapToCanonicalPath(string systemPath)
        {
            return CanonicalPath.Create(systemPath);
        }
    }
}