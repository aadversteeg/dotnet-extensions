using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.FileSystem
{
    /// <summary>
    /// Contains hierarchical error codes for file system operations.
    /// These codes extend the well-known error code hierarchies from <see cref="ErrorCodes"/>.
    /// </summary>
    public static class FileSystemErrorCodes
    {
        /// <summary>
        /// Contains error codes related to CanonicalPath operations.
        /// </summary>
        public static class CanonicalPath
        {
            /// <summary>
            /// Root error code for all canonical path validation errors.
            /// </summary>
            public static readonly ErrorCode _ = ErrorCodes.Validation._ / "CanonicalPath";

            /// <summary>
            /// Error code when path is null or empty.
            /// </summary>
            public static readonly ErrorCode PathNullOrEmpty = _ / "NullOrEmpty";

            /// <summary>
            /// Error code when path does not start with '/'.
            /// </summary>
            public static readonly ErrorCode PathMustStartWithSlash = _ / "MustStartWithSlash";

            /// <summary>
            /// Error code when path contains backslashes.
            /// </summary>
            public static readonly ErrorCode PathContainsBackslashes = _ / "ContainsBackslashes";

            /// <summary>
            /// Error code when path contains consecutive forward slashes.
            /// </summary>
            public static readonly ErrorCode PathContainsConsecutiveSlashes = _ / "ConsecutiveSlashes";

            /// <summary>
            /// Error code when a segment is null or empty.
            /// </summary>
            public static readonly ErrorCode SegmentNullOrEmpty = _ / "SegmentNullOrEmpty";

            /// <summary>
            /// Error code when a segment contains path separators.
            /// </summary>
            public static readonly ErrorCode SegmentContainsPathSeparators = _ / "SegmentContainsPathSeparators";

            /// <summary>
            /// Error code when segment is null or empty for combine operation.
            /// </summary>
            public static readonly ErrorCode CombineSegmentNullOrEmpty = _ / "CombineSegmentNullOrEmpty";

            /// <summary>
            /// Error code when segment contains path separators for combine operation.
            /// </summary>
            public static readonly ErrorCode CombineSegmentContainsPathSeparators = _ / "CombineSegmentContainsPathSeparators";

            /// <summary>
            /// Error code when trying to get parent of root path.
            /// </summary>
            public static readonly ErrorCode RootHasNoParent = _ / "RootHasNoParent";

            /// <summary>
            /// Error code when segment is null or empty for append operation.
            /// </summary>
            public static readonly ErrorCode AppendSegmentNullOrEmpty = _ / "AppendSegmentNullOrEmpty";

            /// <summary>
            /// Error code when segment contains path separators for append operation.
            /// </summary>
            public static readonly ErrorCode AppendSegmentContainsPathSeparators = _ / "AppendSegmentContainsPathSeparators";

            /// <summary>
            /// Error code when segments array is null for append operation.
            /// </summary>
            public static readonly ErrorCode AppendSegmentsNull = _ / "AppendSegmentsNull";

            /// <summary>
            /// Error code when segments array is empty for append operation.
            /// </summary>
            public static readonly ErrorCode AppendSegmentsEmpty = _ / "AppendSegmentsEmpty";

            /// <summary>
            /// Error code when relative path is null or empty.
            /// </summary>
            public static readonly ErrorCode RelativePathNullOrEmpty = _ / "RelativePathNullOrEmpty";

            /// <summary>
            /// Error code when relative path navigates above the root.
            /// </summary>
            public static readonly ErrorCode RelativePathEscapesRoot = _ / "RelativePathEscapesRoot";

            /// <summary>
            /// Error code when Resolve is called with a non-absolute base path.
            /// </summary>
            public static readonly ErrorCode ResolveRequiresAbsoluteBase = _ / "ResolveRequiresAbsoluteBase";
        }

        /// <summary>
        /// Contains error codes for file not found scenarios.
        /// </summary>
        public static class NotFound
        {
            /// <summary>
            /// A file was not found.
            /// </summary>
            public static readonly ErrorCode File = ErrorCodes.NotFound.File;

            /// <summary>
            /// A directory was not found.
            /// </summary>
            public static readonly ErrorCode Directory = ErrorCodes.NotFound._ / "Directory";

            /// <summary>
            /// A path (file or directory) was not found.
            /// </summary>
            public static readonly ErrorCode Path = ErrorCodes.NotFound._ / "Path";
        }

        /// <summary>
        /// Contains error codes for file system I/O operations.
        /// </summary>
        public static class IO
        {
            /// <summary>
            /// Root error code for all file system I/O errors.
            /// </summary>
            public static readonly ErrorCode _ = ErrorCodes.IO.FileSystem;

            /// <summary>
            /// Access to the file or directory was denied.
            /// </summary>
            public static readonly ErrorCode AccessDenied = _ / "AccessDenied";

            /// <summary>
            /// The path is too long for the file system.
            /// </summary>
            public static readonly ErrorCode PathTooLong = _ / "PathTooLong";

            /// <summary>
            /// The path contains invalid characters or is malformed.
            /// </summary>
            public static readonly ErrorCode InvalidPath = _ / "InvalidPath";

            /// <summary>
            /// A general I/O error occurred.
            /// </summary>
            public static readonly ErrorCode General = _ / "General";

            /// <summary>
            /// Insufficient space for the operation.
            /// </summary>
            public static readonly ErrorCode InsufficientSpace = _ / "InsufficientSpace";

            /// <summary>
            /// Move operation across different volumes is not supported.
            /// </summary>
            public static readonly ErrorCode CrossVolumeMove = _ / "CrossVolumeMove";

            /// <summary>
            /// Operation partially failed with some items processed successfully.
            /// </summary>
            public static readonly ErrorCode PartialFailure = _ / "PartialFailure";

            /// <summary>
            /// The destination file already exists and overwrite was not specified.
            /// </summary>
            public static readonly ErrorCode FileAlreadyExists = _ / "FileAlreadyExists";

            /// <summary>
            /// The destination directory already exists.
            /// </summary>
            public static readonly ErrorCode DirectoryAlreadyExists = _ / "DirectoryAlreadyExists";

            /// <summary>
            /// Cannot overwrite a non-empty directory.
            /// </summary>
            public static readonly ErrorCode DirectoryNotEmpty = _ / "DirectoryNotEmpty";
        }

        /// <summary>
        /// The operation is not supported by the file system.
        /// </summary>
        public static readonly ErrorCode NotSupported = ErrorCodes.Internal._ / "NotSupported";

        /// <summary>
        /// An unknown or unspecified error.
        /// </summary>
        public static readonly ErrorCode Unknown = ErrorCodes.Internal.Unexpected;
    }
}
