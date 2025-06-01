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
    }
}