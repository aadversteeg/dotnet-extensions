using System;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.FileSystem
{
    /// <summary>
    /// Factory methods for creating file system errors.
    /// </summary>
    public static class FileSystemErrors
    {
        /// <summary>
        /// Contains errors related to CanonicalPath operations.
        /// </summary>
        public static class CanonicalPath
        {
            /// <summary>
            /// Error when path is null or empty.
            /// </summary>
            public static readonly Error PathNullOrEmpty = new Error(FileSystemErrorCodes.CanonicalPath.PathNullOrEmpty, "Path cannot be null or empty");

            /// <summary>
            /// Error when path does not start with '/'.
            /// </summary>
            public static readonly Error PathMustStartWithSlash = new Error(FileSystemErrorCodes.CanonicalPath.PathMustStartWithSlash, "Path must start with '/'");

            /// <summary>
            /// Error when path contains backslashes.
            /// </summary>
            public static readonly Error PathContainsBackslashes = new Error(FileSystemErrorCodes.CanonicalPath.PathContainsBackslashes, "Path must not contain backslashes");

            /// <summary>
            /// Error when path contains consecutive forward slashes.
            /// </summary>
            public static readonly Error PathContainsConsecutiveSlashes = new Error(FileSystemErrorCodes.CanonicalPath.PathContainsConsecutiveSlashes, "Path must not contain consecutive forward slashes");

            /// <summary>
            /// Error when a segment is null or empty.
            /// </summary>
            /// <param name="index">The index of the invalid segment.</param>
            /// <returns>An error indicating the segment is null or empty.</returns>
            public static Error SegmentNullOrEmpty(int index) => new Error(FileSystemErrorCodes.CanonicalPath.SegmentNullOrEmpty, $"Segment at index {index} cannot be null or empty").With("segmentIndex", index);

            /// <summary>
            /// Error when a segment contains path separators.
            /// </summary>
            /// <param name="index">The index of the invalid segment.</param>
            /// <returns>An error indicating the segment contains path separators.</returns>
            public static Error SegmentContainsPathSeparators(int index) => new Error(FileSystemErrorCodes.CanonicalPath.SegmentContainsPathSeparators, $"Segment at index {index} cannot contain path separators").With("segmentIndex", index);

            /// <summary>
            /// Error when segment is null or empty for combine operation.
            /// </summary>
            public static readonly Error CombineSegmentNullOrEmpty = new Error(FileSystemErrorCodes.CanonicalPath.CombineSegmentNullOrEmpty, "Segment cannot be null or empty");

            /// <summary>
            /// Error when segment contains path separators for combine operation.
            /// </summary>
            public static readonly Error CombineSegmentContainsPathSeparators = new Error(FileSystemErrorCodes.CanonicalPath.CombineSegmentContainsPathSeparators, "Segment cannot contain path separators");

            /// <summary>
            /// Error when trying to get parent of root path.
            /// </summary>
            public static readonly Error RootHasNoParent = new Error(FileSystemErrorCodes.CanonicalPath.RootHasNoParent, "Root path has no parent");

            /// <summary>
            /// Error when segment is null or empty for append operation.
            /// </summary>
            public static readonly Error AppendSegmentNullOrEmpty = new Error(FileSystemErrorCodes.CanonicalPath.AppendSegmentNullOrEmpty, "Segment cannot be null or empty");

            /// <summary>
            /// Error when segment contains path separators for append operation.
            /// </summary>
            public static readonly Error AppendSegmentContainsPathSeparators = new Error(FileSystemErrorCodes.CanonicalPath.AppendSegmentContainsPathSeparators, "Segment cannot contain path separators");

            /// <summary>
            /// Error when segments array is null for append operation.
            /// </summary>
            public static readonly Error AppendSegmentsNull = new Error(FileSystemErrorCodes.CanonicalPath.AppendSegmentsNull, "Segments array cannot be null");

            /// <summary>
            /// Error when segments array is empty for append operation.
            /// </summary>
            public static readonly Error AppendSegmentsEmpty = new Error(FileSystemErrorCodes.CanonicalPath.AppendSegmentsEmpty, "Segments array cannot be empty");

            /// <summary>
            /// Error when relative path is null or empty.
            /// </summary>
            public static readonly Error RelativePathNullOrEmpty = new Error(FileSystemErrorCodes.CanonicalPath.RelativePathNullOrEmpty, "Relative path cannot be null or empty");

            /// <summary>
            /// Error when relative path navigates above the root.
            /// </summary>
            /// <param name="relativePath">The relative path that escapes root.</param>
            /// <returns>An error indicating the relative path navigates above the root.</returns>
            public static Error RelativePathEscapesRoot(string relativePath) => new Error(
                FileSystemErrorCodes.CanonicalPath.RelativePathEscapesRoot,
                "Relative path navigates above the root")
                .With("relativePath", relativePath);
        }

        /// <summary>
        /// Creates a file system error from an exception, mapping to the appropriate error code.
        /// </summary>
        /// <param name="exception">The exception to convert.</param>
        /// <param name="path">The path related to the error.</param>
        /// <returns>A new Error instance.</returns>
        public static Error FromException(Exception exception, string? path = null)
        {
            var code = exception switch
            {
                System.IO.FileNotFoundException => FileSystemErrorCodes.NotFound.File,
                System.IO.DirectoryNotFoundException => FileSystemErrorCodes.NotFound.Directory,
                UnauthorizedAccessException => FileSystemErrorCodes.IO.AccessDenied,
                System.IO.PathTooLongException => FileSystemErrorCodes.IO.PathTooLong,
                ArgumentException when exception.Message.Contains("path") => FileSystemErrorCodes.IO.InvalidPath,
                System.IO.IOException => FileSystemErrorCodes.IO.General,
                NotSupportedException => FileSystemErrorCodes.NotSupported,
                _ => FileSystemErrorCodes.Unknown
            };

            var error = new Error(code, exception.Message);
            if (path != null)
            {
                error = error.With("path", path);
            }
            return error;
        }
    }
}
