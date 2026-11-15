using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.FileSystem
{
    /// <summary>
    /// Provides platform-specific path mapping between canonical paths and system paths.
    /// </summary>
    public interface IPathMapper
    {
        /// <summary>
        /// Maps a canonical path to a system-specific path.
        /// </summary>
        /// <param name="canonicalPath">The canonical path to map.</param>
        /// <returns>A result containing the system-specific path or an error.</returns>
        Result<string, Error> MapToSystemPath(CanonicalPath canonicalPath);
        
        /// <summary>
        /// Maps a system-specific path to a canonical path.
        /// </summary>
        /// <param name="systemPath">The system-specific path to map.</param>
        /// <returns>A result containing the canonical path or an error.</returns>
        Result<CanonicalPath, Error> MapToCanonicalPath(string systemPath);

        /// <summary>
        /// Resolves a relative path against a base canonical path to produce a new canonical path.
        /// </summary>
        /// <param name="relativePath">The relative path to resolve (may contain ., .., and path separators).</param>
        /// <param name="basePath">The base canonical path to resolve against.</param>
        /// <returns>A result containing the resolved canonical path or an error.</returns>
        Result<CanonicalPath, Error> MapToCanonicalPath(string relativePath, CanonicalPath basePath);
    }
}