using Ave.Extensions.Functional;
using Ave.Extensions.FileSystem.Models;

namespace Ave.Extensions.FileSystem
{
    /// <summary>
    /// Maps between canonical (Unix-style) paths and system-native paths
    /// </summary>
    public interface IPathMapper
    {
        /// <summary>
        /// Maps a canonical path to the system-native path format
        /// </summary>
        /// <param name="canonicalPath">Unix-style path (e.g., /d/folder/file.txt)</param>
        /// <returns>System path (e.g., D:\folder\file.txt on Windows)</returns>
        Result<string, FileSystemError> MapToSystemPath(string canonicalPath);
        
        /// <summary>
        /// Maps a system path back to the canonical format
        /// </summary>
        /// <param name="systemPath">System-native path</param>
        /// <returns>Canonical Unix-style path</returns>
        Result<string, FileSystemError> MapToCanonicalPath(string systemPath);
        
        /// <summary>
        /// Validates if the path format is valid
        /// </summary>
        bool IsValidPathFormat(string path);
    }
}