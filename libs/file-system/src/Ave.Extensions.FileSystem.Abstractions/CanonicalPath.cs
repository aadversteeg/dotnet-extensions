using System;
using Ave.Extensions.Functional;

namespace Ave.Extensions.FileSystem
{
    /// <summary>
    /// Represents a canonical file system path that is always validated and stored efficiently.
    /// </summary>
    public readonly record struct CanonicalPath : IEquatable<CanonicalPath>
    {
        /// <summary>
        /// Gets the path value as a string.
        /// </summary>
        public string Value { get; }

        /// <summary>
        /// Gets the path segments as an array.
        /// </summary>
        public string[] Segments { get; }

        /// <summary>
        /// Gets a cached hash code for efficient comparisons.
        /// </summary>
        private readonly int _hashCode;

        /// <summary>
        /// Gets the root canonical path ("/").
        /// </summary>
        public static CanonicalPath Root { get; } = new CanonicalPath("/", Array.Empty<string>());

        /// <summary>
        /// Initializes a new instance of the CanonicalPath record.
        /// </summary>
        /// <param name="value">The validated path value.</param>
        /// <param name="segments">The path segments.</param>
        private CanonicalPath(string value, string[] segments)
        {
            Value = value;
            Segments = segments;
            _hashCode = StringComparer.OrdinalIgnoreCase.GetHashCode(value);
        }

        /// <summary>
        /// Creates a new CanonicalPath from the provided path string.
        /// </summary>
        /// <param name="path">The path to validate. Must start with '/' and contain only forward slashes.</param>
        /// <returns>A Result containing the CanonicalPath or an error.</returns>
        public static Result<CanonicalPath, Error> Create(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return Result<CanonicalPath, Error>.Failure(Errors.CanonicalPath.PathNullOrEmpty);
            }

            if (!path.StartsWith('/'))
            {
                return Result<CanonicalPath, Error>.Failure(Errors.CanonicalPath.PathMustStartWithSlash);
            }

            if (path.Contains('\\'))
            {
                return Result<CanonicalPath, Error>.Failure(Errors.CanonicalPath.PathContainsBackslashes);
            }

            if (path.Contains("//"))
            {
                return Result<CanonicalPath, Error>.Failure(Errors.CanonicalPath.PathContainsConsecutiveSlashes);
            }

            // Extract segments (empty for root path)
            var segments = path == "/" 
                ? Array.Empty<string>() 
                : path.Split('/', StringSplitOptions.RemoveEmptyEntries);

            return Result<CanonicalPath, Error>.Success(new CanonicalPath(path, segments));
        }

        /// <summary>
        /// Creates a new CanonicalPath from the provided path segments.
        /// </summary>
        /// <param name="segments">The path segments to combine into a canonical path.</param>
        /// <returns>A Result containing the CanonicalPath or an error.</returns>
        public static Result<CanonicalPath, Error> Create(string[] segments)
        {
            if (segments.Length == 0)
            {
                return Result<CanonicalPath, Error>.Success(Root);
            }

            // Validate segments
            for (int i = 0; i < segments.Length; i++)
            {
                var segment = segments[i];
                if (string.IsNullOrEmpty(segment))
                {
                    return Result<CanonicalPath, Error>.Failure(Errors.CanonicalPath.SegmentNullOrEmpty(i));
                }

                if (segment.Contains('/') || segment.Contains('\\'))
                {
                    return Result<CanonicalPath, Error>.Failure(Errors.CanonicalPath.SegmentContainsPathSeparators(i));
                }
            }

            var path = "/" + string.Join("/", segments);
            return Result<CanonicalPath, Error>.Success(new CanonicalPath(path, segments));
        }

        /// <summary>
        /// Gets the parent path of this canonical path.
        /// </summary>
        /// <returns>A Result containing the parent path or an error if this is the root path.</returns>
        public Result<CanonicalPath, Error> GetParent()
        {
            if (Segments.Length == 0) // Root path
                return Result<CanonicalPath, Error>.Failure(Errors.CanonicalPath.RootHasNoParent);

            if (Segments.Length == 1) // Direct child of root
                return Result<CanonicalPath, Error>.Success(Root);

            var parentSegments = new string[Segments.Length - 1];
            Array.Copy(Segments, 0, parentSegments, 0, parentSegments.Length);
            var parentPath = "/" + string.Join("/", parentSegments);
            return Result<CanonicalPath, Error>.Success(new CanonicalPath(parentPath, parentSegments));
        }

        /// <summary>
        /// Gets the file or directory name from this path.
        /// </summary>
        /// <returns>The name of the file or directory.</returns>
        public string GetName()
        {
            return Segments.Length == 0 ? "/" : Segments[^1];
        }

        /// <summary>
        /// Appends a relative path segment to this path.
        /// </summary>
        /// <param name="segment">The relative path segment to append.</param>
        /// <returns>A Result containing the combined path or an error.</returns>
        public Result<CanonicalPath, Error> Append(string segment)
        {
            if (string.IsNullOrEmpty(segment))
                return Result<CanonicalPath, Error>.Failure(Errors.CanonicalPath.AppendSegmentNullOrEmpty);

            if (segment.Contains('/') || segment.Contains('\\'))
                return Result<CanonicalPath, Error>.Failure(Errors.CanonicalPath.AppendSegmentContainsPathSeparators);

            var newSegments = new string[Segments.Length + 1];
            Array.Copy(Segments, 0, newSegments, 0, Segments.Length);
            newSegments[^1] = segment;

            var combinedPath = "/" + string.Join("/", newSegments);
            return Result<CanonicalPath, Error>.Success(new CanonicalPath(combinedPath, newSegments));
        }

        /// <summary>
        /// Appends multiple relative path segments to this path.
        /// </summary>
        /// <param name="segments">The relative path segments to append.</param>
        /// <returns>A Result containing the combined path or an error.</returns>
        public Result<CanonicalPath, Error> Append(string[] segments)
        {
            if (segments == null)
                return Result<CanonicalPath, Error>.Failure(Errors.CanonicalPath.AppendSegmentsNull);

            if (segments.Length == 0)
                return Result<CanonicalPath, Error>.Failure(Errors.CanonicalPath.AppendSegmentsEmpty);

            // Validate all segments first
            for (int i = 0; i < segments.Length; i++)
            {
                var segment = segments[i];
                if (string.IsNullOrEmpty(segment))
                {
                    return Result<CanonicalPath, Error>.Failure(Errors.CanonicalPath.SegmentNullOrEmpty(i));
                }

                if (segment.Contains('/') || segment.Contains('\\'))
                {
                    return Result<CanonicalPath, Error>.Failure(Errors.CanonicalPath.SegmentContainsPathSeparators(i));
                }
            }

            var newSegments = new string[Segments.Length + segments.Length];
            Array.Copy(Segments, 0, newSegments, 0, Segments.Length);
            Array.Copy(segments, 0, newSegments, Segments.Length, segments.Length);

            var combinedPath = "/" + string.Join("/", newSegments);
            return Result<CanonicalPath, Error>.Success(new CanonicalPath(combinedPath, newSegments));
        }

        /// <summary>
        /// Determines whether this path is a child of the specified parent path.
        /// </summary>
        /// <param name="parent">The potential parent path.</param>
        /// <returns>True if this path is a child of the parent; otherwise, false.</returns>
        public bool IsChildOf(CanonicalPath parent)
        {
            if (Segments.Length <= parent.Segments.Length)
                return false;

            for (int i = 0; i < parent.Segments.Length; i++)
            {
                if (!string.Equals(Segments[i], parent.Segments[i], StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Determines whether this path is a parent of the specified child path.
        /// </summary>
        /// <param name="child">The potential child path.</param>
        /// <returns>True if this path is a parent of the child; otherwise, false.</returns>
        public bool IsParentOf(CanonicalPath child)
        {
            if (child.Segments.Length <= Segments.Length)
                return false;

            for (int i = 0; i < Segments.Length; i++)
            {
                if (!string.Equals(Segments[i], child.Segments[i], StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Finds the first occurrence of the specified segment in this path.
        /// </summary>
        /// <param name="segment">The segment to search for.</param>
        /// <returns>The zero-based index of the first occurrence of the segment, or -1 if not found.</returns>
        public int Contains(string segment)
        {
            if (string.IsNullOrEmpty(segment))
                return -1;

            for (int i = 0; i < Segments.Length; i++)
            {
                if (string.Equals(Segments[i], segment, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// Finds the first occurrence of the specified sequence of segments in this path.
        /// </summary>
        /// <param name="segments">The sequence of segments to search for.</param>
        /// <returns>The zero-based index of the first occurrence of the sequence, or -1 if not found.</returns>
        public int Contains(string[] segments)
        {
            if (segments == null || segments.Length == 0)
                return -1;

            if (segments.Length > Segments.Length)
                return -1;

            for (int i = 0; i <= Segments.Length - segments.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < segments.Length; j++)
                {
                    if (!string.Equals(Segments[i + j], segments[j], StringComparison.OrdinalIgnoreCase))
                    {
                        found = false;
                        break;
                    }
                }

                if (found)
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// Efficiently compares two CanonicalPath instances for equality.
        /// </summary>
        /// <param name="other">The other CanonicalPath to compare.</param>
        /// <returns>True if the paths are equal; otherwise, false.</returns>
        public bool Equals(CanonicalPath other)
        {
            // Fast path: compare hash codes first
            if (_hashCode != other._hashCode)
                return false;

            // Compare path strings with case-insensitive comparison
            return string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns the hash code for this CanonicalPath.
        /// </summary>
        /// <returns>A hash code for the current object.</returns>
        public override int GetHashCode() => _hashCode;

        /// <summary>
        /// Returns the string representation of the canonical path.
        /// </summary>
        /// <returns>The path string.</returns>
        public override string ToString() => Value;

    }
}