using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.FileSystem
{
    /// <summary>
    /// Base class for path mappers that provides shared relative path resolution logic.
    /// Platform-specific mappers should inherit from this class and implement the abstract methods.
    /// </summary>
    public abstract class PathMapperBase : IPathMapper
    {
        /// <inheritdoc />
        public abstract Result<string, Error> MapToSystemPath(CanonicalPath canonicalPath);

        /// <inheritdoc />
        public abstract Result<CanonicalPath, Error> MapToCanonicalPath(string systemPath);

        /// <inheritdoc />
        public Result<CanonicalPath, Error> MapToCanonicalPath(string relativePath, CanonicalPath basePath)
        {
            if (string.IsNullOrEmpty(relativePath))
                return Result<CanonicalPath, Error>.Failure(
                    FileSystemErrors.CanonicalPath.RelativePathNullOrEmpty);

            // Normalize backslashes to forward slashes before creating a canonical path
            var normalized = relativePath.Replace('\\', '/');
            var createResult = CanonicalPath.Create(normalized);
            if (createResult.IsFailure)
                return createResult;

            return createResult.Value.Resolve(basePath);
        }
    }
}
