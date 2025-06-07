using System;
using System.IO;
using Ave.Extensions.FileSystem.Models;

namespace Ave.Extensions.FileSystem.Local
{
    /// <summary>
    /// Extension methods for converting FileInfo and DirectoryInfo to FileSystemEntry.
    /// </summary>
    public static class FileSystemEntryExtensions
    {
        /// <summary>
        /// Converts a FileInfo instance to a FileSystemEntry using path mapping.
        /// </summary>
        /// <param name="fileInfo">The FileInfo to convert.</param>
        /// <param name="pathMapper">Path mapper for canonical path conversion.</param>
        /// <returns>A new FileSystemEntry instance.</returns>
        /// <exception cref="ArgumentNullException">Thrown when fileInfo or pathMapper is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the file path is invalid.</exception>
        public static FileSystemEntry ToFileSystemEntry(this FileInfo fileInfo, IPathMapper pathMapper)
        {
            if (fileInfo == null)
                throw new ArgumentNullException(nameof(fileInfo));
            if (pathMapper == null)
                throw new ArgumentNullException(nameof(pathMapper));

            var result = pathMapper.MapToCanonicalPath(fileInfo.FullName);
            if (result.IsFailure)
                throw new ArgumentException($"Invalid file path: {fileInfo.FullName}", nameof(fileInfo));
            
            var path = result.Value;

            return new FileSystemEntry(
                path: path,
                isDirectory: false,
                size: fileInfo.Length,
                extension: fileInfo.Extension,
                createdUtc: fileInfo.CreationTimeUtc,
                modifiedUtc: fileInfo.LastWriteTimeUtc,
                lastAccessUtc: fileInfo.LastAccessTimeUtc,
                attributes: fileInfo.Attributes);
        }


        /// <summary>
        /// Converts a DirectoryInfo instance to a FileSystemEntry using path mapping.
        /// </summary>
        /// <param name="directoryInfo">The DirectoryInfo to convert.</param>
        /// <param name="pathMapper">Path mapper for canonical path conversion.</param>
        /// <returns>A new FileSystemEntry instance.</returns>
        /// <exception cref="ArgumentNullException">Thrown when directoryInfo or pathMapper is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the directory path is invalid.</exception>
        public static FileSystemEntry ToFileSystemEntry(this DirectoryInfo directoryInfo, IPathMapper pathMapper)
        {
            if (directoryInfo == null)
                throw new ArgumentNullException(nameof(directoryInfo));
            if (pathMapper == null)
                throw new ArgumentNullException(nameof(pathMapper));

            var result = pathMapper.MapToCanonicalPath(directoryInfo.FullName);
            if (result.IsFailure)
                throw new ArgumentException($"Invalid directory path: {directoryInfo.FullName}", nameof(directoryInfo));
            
            var path = result.Value;

            return new FileSystemEntry(
                path: path,
                isDirectory: true,
                size: null,
                extension: null,
                createdUtc: directoryInfo.CreationTimeUtc,
                modifiedUtc: directoryInfo.LastWriteTimeUtc,
                lastAccessUtc: directoryInfo.LastAccessTimeUtc,
                attributes: directoryInfo.Attributes);
        }
    }
}