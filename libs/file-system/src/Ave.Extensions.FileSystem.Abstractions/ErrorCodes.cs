namespace Ave.Extensions.FileSystem
{
    /// <summary>
    /// Contains hierarchical error codes for the FileSystem extensions.
    /// </summary>
    public static class ErrorCodes
    {
        /// <summary>
        /// Contains error codes related to CanonicalPath operations.
        /// </summary>
        public static class CanonicalPath
        {
            /// <summary>
            /// Error code when path is null or empty.
            /// </summary>
            public const string PathNullOrEmpty = "CANONICAL_PATH_NULL_OR_EMPTY";

            /// <summary>
            /// Error code when path does not start with '/'.
            /// </summary>
            public const string PathMustStartWithSlash = "CANONICAL_PATH_MUST_START_WITH_SLASH";

            /// <summary>
            /// Error code when path contains backslashes.
            /// </summary>
            public const string PathContainsBackslashes = "CANONICAL_PATH_CONTAINS_BACKSLASHES";

            /// <summary>
            /// Error code when path contains consecutive forward slashes.
            /// </summary>
            public const string PathContainsConsecutiveSlashes = "CANONICAL_PATH_CONTAINS_CONSECUTIVE_SLASHES";

            /// <summary>
            /// Error code when a segment is null or empty.
            /// </summary>
            public const string SegmentNullOrEmpty = "CANONICAL_PATH_SEGMENT_NULL_OR_EMPTY";

            /// <summary>
            /// Error code when a segment contains path separators.
            /// </summary>
            public const string SegmentContainsPathSeparators = "CANONICAL_PATH_SEGMENT_CONTAINS_PATH_SEPARATORS";

            /// <summary>
            /// Error code when segment is null or empty for combine operation.
            /// </summary>
            public const string CombineSegmentNullOrEmpty = "CANONICAL_PATH_COMBINE_SEGMENT_NULL_OR_EMPTY";

            /// <summary>
            /// Error code when segment contains path separators for combine operation.
            /// </summary>
            public const string CombineSegmentContainsPathSeparators = "CANONICAL_PATH_COMBINE_SEGMENT_CONTAINS_PATH_SEPARATORS";

            /// <summary>
            /// Error code when trying to get parent of root path.
            /// </summary>
            public const string RootHasNoParent = "CANONICAL_PATH_ROOT_HAS_NO_PARENT";

            /// <summary>
            /// Error code when segment is null or empty for append operation.
            /// </summary>
            public const string AppendSegmentNullOrEmpty = "CANONICAL_PATH_APPEND_SEGMENT_NULL_OR_EMPTY";

            /// <summary>
            /// Error code when segment contains path separators for append operation.
            /// </summary>
            public const string AppendSegmentContainsPathSeparators = "CANONICAL_PATH_APPEND_SEGMENT_CONTAINS_PATH_SEPARATORS";

            /// <summary>
            /// Error code when segments array is null for append operation.
            /// </summary>
            public const string AppendSegmentsNull = "CANONICAL_PATH_APPEND_SEGMENTS_NULL";

            /// <summary>
            /// Error code when segments array is empty for append operation.
            /// </summary>
            public const string AppendSegmentsEmpty = "CANONICAL_PATH_APPEND_SEGMENTS_EMPTY";
        }
    }
}