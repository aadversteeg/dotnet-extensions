using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Ave.Extensions.FileSystem
{
    /// <summary>
    /// Cross-platform path manipulation utilities.
    /// </summary>
    public class PathUtilities : IPathUtilities
    {
        /// <inheritdoc />
        public string Combine(params string[] paths)
        {
            if (paths == null)
                throw new ArgumentNullException(nameof(paths));

            return Path.Combine(paths);
        }

        /// <inheritdoc />
        public string GetFileName(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            return Path.GetFileName(path);
        }

        /// <inheritdoc />
        public string GetFileNameWithoutExtension(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            return Path.GetFileNameWithoutExtension(path);
        }

        /// <inheritdoc />
        public string GetExtension(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            return Path.GetExtension(path);
        }

        /// <inheritdoc />
        public string? GetDirectoryName(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            return Path.GetDirectoryName(path);
        }

        /// <inheritdoc />
        public string GetFullPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("Path cannot be null or empty.", nameof(path));

            return Path.GetFullPath(path);
        }

        /// <inheritdoc />
        public string GetRelativePath(string relativeTo, string path)
        {
            if (string.IsNullOrEmpty(relativeTo))
                throw new ArgumentException("RelativeTo path cannot be null or empty.", nameof(relativeTo));
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("Path cannot be null or empty.", nameof(path));

            return Path.GetRelativePath(relativeTo, path);
        }

        /// <inheritdoc />
        public bool IsPathRooted(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            return Path.IsPathRooted(path);
        }

        /// <inheritdoc />
        public string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            try
            {
                // Get full path to normalize separators and resolve relative paths
                var fullPath = Path.GetFullPath(path);
                
                // Ensure consistent directory separator
                return fullPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            }
            catch
            {
                // If GetFullPath fails, just normalize separators
                return path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            }
        }

        /// <inheritdoc />
        public bool IsSubPathOf(string parentPath, string childPath)
        {
            if (string.IsNullOrEmpty(parentPath) || string.IsNullOrEmpty(childPath))
                return false;

            try
            {
                var normalizedParent = NormalizePath(GetFullPath(parentPath));
                var normalizedChild = NormalizePath(GetFullPath(childPath));

                // Ensure parent path ends with directory separator for accurate comparison
                if (!normalizedParent.EndsWith(Path.DirectorySeparatorChar.ToString()))
                {
                    normalizedParent += Path.DirectorySeparatorChar;
                }

                return normalizedChild.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <inheritdoc />
        public char DirectorySeparatorChar => Path.DirectorySeparatorChar;

        /// <inheritdoc />
        public char AltDirectorySeparatorChar => Path.AltDirectorySeparatorChar;

        /// <inheritdoc />
        public char VolumeSeparatorChar => Path.VolumeSeparatorChar;

        /// <inheritdoc />
        public char[] GetInvalidFileNameChars()
        {
            return Path.GetInvalidFileNameChars();
        }

        /// <inheritdoc />
        public char[] GetInvalidPathChars()
        {
            return Path.GetInvalidPathChars();
        }

        /// <inheritdoc />
        public bool IsValidFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return false;

            var invalidChars = GetInvalidFileNameChars();
            return !fileName.Any(c => invalidChars.Contains(c));
        }

        /// <inheritdoc />
        public bool IsValidPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            try
            {
                var invalidChars = GetInvalidPathChars();
                return !path.Any(c => invalidChars.Contains(c));
            }
            catch
            {
                return false;
            }
        }

        /// <inheritdoc />
        public string ToCanonicalPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;
            
            // Normalize separators to forward slashes
            var normalized = path.Replace('\\', '/');
            
            // Handle Windows absolute paths
            if (normalized.Length >= 2 && normalized[1] == ':')
            {
                var driveLetter = char.ToLowerInvariant(normalized[0]);
                var remainingPath = normalized.Substring(2);
                normalized = $"/{driveLetter}{remainingPath}";
            }
            
            // Remove duplicate slashes
            while (normalized.Contains("//"))
            {
                normalized = normalized.Replace("//", "/");
            }
            
            return normalized;
        }

        /// <inheritdoc />
        public bool IsCanonicalPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;
            
            // Canonical paths use forward slashes and don't contain backslashes
            if (path.Contains('\\'))
                return false;
            
            // Canonical paths for absolute Windows paths start with /letter/
            if (path.Length >= 2 && path[0] == '/' && char.IsLetter(path[1]))
                return true;
            
            // Unix absolute paths start with /
            if (path.StartsWith('/'))
                return true;
            
            // Relative paths are also considered canonical if they use forward slashes
            return true;
        }

        /// <inheritdoc />
        public string FromCanonicalPath(string canonicalPath)
        {
            if (string.IsNullOrEmpty(canonicalPath))
                return canonicalPath;
            
            // On Unix, canonical paths are already correct
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return canonicalPath;
            
            // Handle Windows drive letters: /c/path -> C:\path
            if (canonicalPath.Length >= 3 && canonicalPath[0] == '/' && char.IsLetter(canonicalPath[1]) && canonicalPath[2] == '/')
            {
                var driveLetter = char.ToUpperInvariant(canonicalPath[1]);
                var remainingPath = canonicalPath.Substring(2).Replace('/', '\\');
                return $"{driveLetter}:{remainingPath}";
            }
            
            // Default: just replace forward slashes with backslashes on Windows
            return canonicalPath.Replace('/', '\\');
        }
    }
}