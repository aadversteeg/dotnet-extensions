using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.FileSystem.Local.PathMappers
{
    /// <summary>
    /// Unix-specific path mapper where canonical paths are already system paths.
    /// </summary>
    public class UnixPathMapper : PathMapperBase
    {
        /// <inheritdoc />
        public override Result<string, Error> MapToSystemPath(CanonicalPath canonicalPath)
        {
            // On Unix systems, canonical paths are already system paths
            return Result<string, Error>.Success(canonicalPath.Value);
        }

        /// <inheritdoc />
        public override Result<CanonicalPath, Error> MapToCanonicalPath(string systemPath)
        {
            // On Unix systems, system paths are already canonical
            if (string.IsNullOrWhiteSpace(systemPath))
                return Result<CanonicalPath, Error>.Failure(FileSystemErrors.CanonicalPath.PathNullOrEmpty);

            var result = CanonicalPath.Create(systemPath);
            if (result.IsFailure)
                return Result<CanonicalPath, Error>.Failure(result.Error);
            return Result<CanonicalPath, Error>.Success(result.Value);
        }
    }
}