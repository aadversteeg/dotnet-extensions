using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Ave.Extensions.FileSystem.Local
{
    /// <summary>
    /// Long path support for .NET Framework on Windows. .NET Framework passes a path to Windows as it is, so without
    /// the LongPathsEnabled machine setting a path of 260 characters or more (a folder: 248) fails. A path with the
    /// <c>\\?\</c> prefix has no such limit. .NET adds that prefix itself and needs nothing from here.
    /// <para>
    /// Every system path goes in through <see cref="ToSystemPath"/> and every path that comes back from the
    /// framework goes out through <see cref="RemovePrefix"/>, so a caller never sees the prefix.
    /// </para>
    /// </summary>
    internal static class WindowsLongPath
    {
        private const string Prefix = @"\\?\";
        private const string DevicePrefix = @"\\.\";

        private static readonly Regex PrefixInTextRegex = new(@"\\\\\?\\(?=[a-zA-Z]:\\)");
        private static readonly Lazy<bool> Active = new(DetectActive);

        /// <summary>
        /// True on Windows on .NET Framework when the process accepts prefixed paths. An application in legacy path
        /// mode (one that targets a version below 4.6.2, or sets Switch.System.IO.UseLegacyPathHandling) rejects
        /// them; there this stays false and paths pass unchanged.
        /// </summary>
        public static bool IsActive => Active.Value;

        /// <summary>
        /// Turns a system path into the form that is passed to the framework: when <see cref="IsActive"/>, the full
        /// path with the <c>\\?\</c> prefix; otherwise the path unchanged.
        /// </summary>
        public static string ToSystemPath(string path)
        {
            if (!IsActive || string.IsNullOrEmpty(path))
                return path;

            string fullPath;
            try
            {
                // Windows takes a prefixed path literally, so it must already be the full path: no relative
                // segments, no "..", no forward slashes.
                fullPath = Path.GetFullPath(path);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                // Not a valid path: the file operation itself reports that, as it does without the prefix.
                return path;
            }

            return AddPrefix(fullPath);
        }

        /// <summary>
        /// Adds the <c>\\?\</c> prefix to a drive-absolute path (<c>C:\...</c>). Any other path (relative,
        /// network, device or already prefixed) is returned unchanged.
        /// </summary>
        public static string AddPrefix(string path)
        {
            if (!IsDriveAbsolute(path))
                return path;

            return Prefix + path;
        }

        /// <summary>
        /// Removes the <c>\\?\</c> prefix from a drive-absolute path. Any other path is returned unchanged.
        /// </summary>
        public static string RemovePrefix(string path)
        {
            if (path == null || !path.StartsWith(Prefix, StringComparison.Ordinal) || !IsDriveAbsolute(path, Prefix.Length))
                return path!;

            return path.Substring(Prefix.Length);
        }

        /// <summary>
        /// Removes the <c>\\?\</c> prefix from every drive-absolute path in a text, such as an exception message.
        /// </summary>
        public static string RemovePrefixes(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf(Prefix, StringComparison.Ordinal) < 0)
                return text;

            return PrefixInTextRegex.Replace(text, string.Empty);
        }

        /// <summary>
        /// The name of a file or folder as it is without the prefix. For a prefixed drive root .NET Framework
        /// returns an empty name, where the unprefixed root has the root itself (<c>C:\</c>) as its name.
        /// </summary>
        public static string GetName(System.IO.FileSystemInfo info)
        {
            var name = info.Name;
            return name.Length == 0 ? RemovePrefix(info.FullName) : name;
        }

        private static bool IsDriveAbsolute(string path, int start = 0)
        {
            return path != null
                && path.Length >= start + 3
                && IsAsciiLetter(path[start])
                && path[start + 1] == ':'
                && path[start + 2] == '\\'
                && !path.StartsWith(DevicePrefix, StringComparison.Ordinal);
        }

        private static bool IsAsciiLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

        private static bool DetectActive()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                || !RuntimeInformation.FrameworkDescription.StartsWith(".NET Framework", StringComparison.Ordinal))
                return false;

            try
            {
                // In legacy path mode Path.GetFullPath throws on the prefix ("Illegal characters in path") and
                // Directory.Exists returns false, so only a process that really accepts prefixed paths passes.
                var probe = Prefix + Environment.SystemDirectory;
                return Path.GetFullPath(probe).StartsWith(Prefix, StringComparison.Ordinal) && Directory.Exists(probe);
            }
            catch
            {
                return false;
            }
        }
    }
}
