using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Ave.Extensions.FileSystem
{
    /// <summary>
    /// Provides equality comparison for <see cref="CanonicalPath"/> with configurable case sensitivity.
    /// </summary>
    public sealed class CanonicalPathComparer : IEqualityComparer<CanonicalPath>
    {
        private readonly StringComparison _comparison;
        private readonly StringComparer _stringComparer;

        /// <summary>
        /// Gets a comparer that performs case-sensitive comparisons.
        /// Use this for Linux and macOS file systems.
        /// </summary>
        public static CanonicalPathComparer CaseSensitive { get; } = new(StringComparison.Ordinal);

        /// <summary>
        /// Gets a comparer that performs case-insensitive comparisons.
        /// Use this for Windows file systems.
        /// </summary>
        public static CanonicalPathComparer CaseInsensitive { get; } = new(StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Gets a comparer appropriate for the current operating system.
        /// Returns case-insensitive for Windows, case-sensitive for Linux/macOS.
        /// </summary>
        public static CanonicalPathComparer PlatformDefault { get; } =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? CaseInsensitive
                : CaseSensitive;

        /// <summary>
        /// Gets the string comparison mode used by this comparer.
        /// </summary>
        public StringComparison Comparison => _comparison;

        /// <summary>
        /// Gets a value indicating whether this comparer is case-sensitive.
        /// </summary>
        public bool IsCaseSensitive => _comparison == StringComparison.Ordinal;

        /// <summary>
        /// Initializes a new instance of the <see cref="CanonicalPathComparer"/> class.
        /// </summary>
        /// <param name="comparison">The string comparison mode to use.</param>
        public CanonicalPathComparer(StringComparison comparison)
        {
            _comparison = comparison;
            _stringComparer = comparison == StringComparison.OrdinalIgnoreCase
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
        }

        /// <summary>
        /// Determines whether the specified paths are equal.
        /// </summary>
        /// <param name="x">The first path to compare.</param>
        /// <param name="y">The second path to compare.</param>
        /// <returns>true if the specified paths are equal; otherwise, false.</returns>
        public bool Equals(CanonicalPath x, CanonicalPath y)
        {
            return string.Equals(x.Value, y.Value, _comparison);
        }

        /// <summary>
        /// Returns a hash code for the specified path.
        /// </summary>
        /// <param name="obj">The path for which to get a hash code.</param>
        /// <returns>A hash code for the specified path.</returns>
        public int GetHashCode(CanonicalPath obj)
        {
            return _stringComparer.GetHashCode(obj.Value);
        }

        /// <summary>
        /// Determines whether the child path is a child of the parent path.
        /// </summary>
        /// <param name="child">The potential child path.</param>
        /// <param name="parent">The potential parent path.</param>
        /// <returns>true if child is a child of parent; otherwise, false.</returns>
        public bool IsChildOf(CanonicalPath child, CanonicalPath parent)
        {
            if (child.Segments.Length <= parent.Segments.Length)
                return false;

            for (int i = 0; i < parent.Segments.Length; i++)
            {
                if (!string.Equals(child.Segments[i], parent.Segments[i], _comparison))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Determines whether the parent path is a parent of the child path.
        /// </summary>
        /// <param name="parent">The potential parent path.</param>
        /// <param name="child">The potential child path.</param>
        /// <returns>true if parent is a parent of child; otherwise, false.</returns>
        public bool IsParentOf(CanonicalPath parent, CanonicalPath child)
        {
            return IsChildOf(child, parent);
        }

        /// <summary>
        /// Finds the first occurrence of the specified segment in the path.
        /// </summary>
        /// <param name="path">The path to search in.</param>
        /// <param name="segment">The segment to search for.</param>
        /// <returns>The zero-based index of the first occurrence, or -1 if not found.</returns>
        public int IndexOfSegment(CanonicalPath path, string segment)
        {
            if (string.IsNullOrEmpty(segment))
                return -1;

            for (int i = 0; i < path.Segments.Length; i++)
            {
                if (string.Equals(path.Segments[i], segment, _comparison))
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// Finds the first occurrence of the specified sequence of segments in the path.
        /// </summary>
        /// <param name="path">The path to search in.</param>
        /// <param name="segments">The sequence of segments to search for.</param>
        /// <returns>The zero-based index of the first occurrence, or -1 if not found.</returns>
        public int IndexOfSegments(CanonicalPath path, string[] segments)
        {
            if (segments == null || segments.Length == 0)
                return -1;

            if (segments.Length > path.Segments.Length)
                return -1;

            for (int i = 0; i <= path.Segments.Length - segments.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < segments.Length; j++)
                {
                    if (!string.Equals(path.Segments[i + j], segments[j], _comparison))
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
    }
}
