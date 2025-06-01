namespace Ave.Extensions.FileSystem
{
    /// <summary>
    /// Contains predefined errors for the FileSystem extensions.
    /// </summary>
    public static class Errors
    {
        /// <summary>
        /// Contains errors related to CanonicalPath operations.
        /// </summary>
        public static class CanonicalPath
        {
            /// <summary>
            /// Error when path is null or empty.
            /// </summary>
            public static readonly Error PathNullOrEmpty = new(ErrorCodes.CanonicalPath.PathNullOrEmpty, "Path cannot be null or empty");

            /// <summary>
            /// Error when path does not start with '/'.
            /// </summary>
            public static readonly Error PathMustStartWithSlash = new(ErrorCodes.CanonicalPath.PathMustStartWithSlash, "Path must start with '/'");

            /// <summary>
            /// Error when path contains backslashes.
            /// </summary>
            public static readonly Error PathContainsBackslashes = new(ErrorCodes.CanonicalPath.PathContainsBackslashes, "Path must not contain backslashes");

            /// <summary>
            /// Error when path contains consecutive forward slashes.
            /// </summary>
            public static readonly Error PathContainsConsecutiveSlashes = new(ErrorCodes.CanonicalPath.PathContainsConsecutiveSlashes, "Path must not contain consecutive forward slashes");

            /// <summary>
            /// Error when a segment is null or empty.
            /// </summary>
            /// <param name="index">The index of the invalid segment.</param>
            /// <returns>An error indicating the segment is null or empty.</returns>
            public static Error SegmentNullOrEmpty(int index) => new(ErrorCodes.CanonicalPath.SegmentNullOrEmpty, $"Segment at index {index} cannot be null or empty");

            /// <summary>
            /// Error when a segment contains path separators.
            /// </summary>
            /// <param name="index">The index of the invalid segment.</param>
            /// <returns>An error indicating the segment contains path separators.</returns>
            public static Error SegmentContainsPathSeparators(int index) => new(ErrorCodes.CanonicalPath.SegmentContainsPathSeparators, $"Segment at index {index} cannot contain path separators");

            /// <summary>
            /// Error when segment is null or empty for combine operation.
            /// </summary>
            public static readonly Error CombineSegmentNullOrEmpty = new(ErrorCodes.CanonicalPath.CombineSegmentNullOrEmpty, "Segment cannot be null or empty");

            /// <summary>
            /// Error when segment contains path separators for combine operation.
            /// </summary>
            public static readonly Error CombineSegmentContainsPathSeparators = new(ErrorCodes.CanonicalPath.CombineSegmentContainsPathSeparators, "Segment cannot contain path separators");

            /// <summary>
            /// Error when trying to get parent of root path.
            /// </summary>
            public static readonly Error RootHasNoParent = new(ErrorCodes.CanonicalPath.RootHasNoParent, "Root path has no parent");

            /// <summary>
            /// Error when segment is null or empty for append operation.
            /// </summary>
            public static readonly Error AppendSegmentNullOrEmpty = new(ErrorCodes.CanonicalPath.AppendSegmentNullOrEmpty, "Segment cannot be null or empty");

            /// <summary>
            /// Error when segment contains path separators for append operation.
            /// </summary>
            public static readonly Error AppendSegmentContainsPathSeparators = new(ErrorCodes.CanonicalPath.AppendSegmentContainsPathSeparators, "Segment cannot contain path separators");

            /// <summary>
            /// Error when segments array is null for append operation.
            /// </summary>
            public static readonly Error AppendSegmentsNull = new(ErrorCodes.CanonicalPath.AppendSegmentsNull, "Segments array cannot be null");

            /// <summary>
            /// Error when segments array is empty for append operation.
            /// </summary>
            public static readonly Error AppendSegmentsEmpty = new(ErrorCodes.CanonicalPath.AppendSegmentsEmpty, "Segments array cannot be empty");
        }
    }
}