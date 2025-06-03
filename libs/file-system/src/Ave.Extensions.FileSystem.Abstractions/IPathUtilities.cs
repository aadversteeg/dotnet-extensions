namespace Ave.Extensions.FileSystem
{
    /// <summary>
    /// Provides cross-platform path manipulation utilities.
    /// </summary>
    public interface IPathUtilities
    {
        /// <summary>
        /// Combines multiple path segments into a single path.
        /// </summary>
        /// <param name="paths">The path segments to combine.</param>
        /// <returns>The combined path.</returns>
        string Combine(params string[] paths);

        /// <summary>
        /// Gets the file name and extension from a path.
        /// </summary>
        /// <param name="path">The path to extract the file name from.</param>
        /// <returns>The file name with extension.</returns>
        string GetFileName(string path);

        /// <summary>
        /// Gets the file name without the extension from a path.
        /// </summary>
        /// <param name="path">The path to extract the file name from.</param>
        /// <returns>The file name without extension.</returns>
        string GetFileNameWithoutExtension(string path);

        /// <summary>
        /// Gets the extension from a file path.
        /// </summary>
        /// <param name="path">The path to extract the extension from.</param>
        /// <returns>The file extension including the dot, or empty string if no extension.</returns>
        string GetExtension(string path);

        /// <summary>
        /// Gets the directory name from a path.
        /// </summary>
        /// <param name="path">The path to extract the directory from.</param>
        /// <returns>The directory name, or null if the path is a root directory.</returns>
        string? GetDirectoryName(string path);

        /// <summary>
        /// Gets the full path from a relative or absolute path.
        /// </summary>
        /// <param name="path">The path to expand.</param>
        /// <returns>The full absolute path.</returns>
        string GetFullPath(string path);

        /// <summary>
        /// Gets a relative path from one path to another.
        /// </summary>
        /// <param name="relativeTo">The base path to calculate relative to.</param>
        /// <param name="path">The target path.</param>
        /// <returns>The relative path from relativeTo to path.</returns>
        string GetRelativePath(string relativeTo, string path);

        /// <summary>
        /// Determines whether a path is absolute (rooted).
        /// </summary>
        /// <param name="path">The path to check.</param>
        /// <returns>True if the path is absolute; otherwise, false.</returns>
        bool IsPathRooted(string path);

        /// <summary>
        /// Normalizes a path by resolving directory separators and removing redundant elements.
        /// </summary>
        /// <param name="path">The path to normalize.</param>
        /// <returns>The normalized path.</returns>
        string NormalizePath(string path);

        /// <summary>
        /// Determines whether a path is a subpath of another path.
        /// </summary>
        /// <param name="parentPath">The potential parent path.</param>
        /// <param name="childPath">The potential child path.</param>
        /// <returns>True if childPath is a subpath of parentPath; otherwise, false.</returns>
        bool IsSubPathOf(string parentPath, string childPath);

        /// <summary>
        /// Gets the path separator character for the current platform.
        /// </summary>
        char DirectorySeparatorChar { get; }

        /// <summary>
        /// Gets the alternate path separator character for the current platform.
        /// </summary>
        char AltDirectorySeparatorChar { get; }

        /// <summary>
        /// Gets the volume separator character for the current platform (e.g., ':' on Windows).
        /// </summary>
        char VolumeSeparatorChar { get; }

        /// <summary>
        /// Gets characters that are invalid in file names.
        /// </summary>
        char[] GetInvalidFileNameChars();

        /// <summary>
        /// Gets characters that are invalid in paths.
        /// </summary>
        char[] GetInvalidPathChars();

        /// <summary>
        /// Determines whether a file name contains any invalid characters.
        /// </summary>
        /// <param name="fileName">The file name to validate.</param>
        /// <returns>True if the file name is valid; otherwise, false.</returns>
        bool IsValidFileName(string fileName);

        /// <summary>
        /// Determines whether a path contains any invalid characters.
        /// </summary>
        /// <param name="path">The path to validate.</param>
        /// <returns>True if the path is valid; otherwise, false.</returns>
        bool IsValidPath(string path);

        /// <summary>
        /// Converts any path to canonical Unix-style format
        /// </summary>
        string ToCanonicalPath(string path);

        /// <summary>
        /// Checks if a path is in canonical format
        /// </summary>
        bool IsCanonicalPath(string path);

        /// <summary>
        /// Converts a canonical path to platform-specific format
        /// </summary>
        string FromCanonicalPath(string canonicalPath);
    }
}