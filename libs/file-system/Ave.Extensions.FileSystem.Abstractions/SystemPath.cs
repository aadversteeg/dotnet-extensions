namespace Ave.Extensions.FileSystem
{
    /// <summary>
    /// Provides platform-independent detection of absolute and relative system paths.
    /// Recognizes both Windows (drive letters, UNC) and Unix (leading slash) patterns
    /// regardless of the current operating system.
    /// </summary>
    public static class SystemPath
    {
        /// <summary>
        /// Determines whether the specified system path is absolute.
        /// </summary>
        /// <param name="systemPath">The system path to check.</param>
        /// <returns>True if the path is absolute; otherwise, false.</returns>
        public static bool IsAbsolute(string? systemPath)
        {
            if (string.IsNullOrEmpty(systemPath))
                return false;

            // Unix absolute: /path
            if (systemPath[0] == '/')
                return true;

            // Windows drive letter: C:\ or C:/
            if (systemPath.Length >= 2 && char.IsLetter(systemPath[0]) && systemPath[1] == ':')
                return true;

            // UNC: \\server or //server
            if (systemPath.Length >= 2 &&
                ((systemPath[0] == '\\' && systemPath[1] == '\\') ||
                 (systemPath[0] == '/' && systemPath[1] == '/')))
                return true;

            return false;
        }

        /// <summary>
        /// Determines whether the specified system path is relative.
        /// </summary>
        /// <param name="systemPath">The system path to check.</param>
        /// <returns>True if the path is relative (non-null, non-empty, and not absolute); otherwise, false.</returns>
        public static bool IsRelative(string? systemPath)
        {
            if (string.IsNullOrEmpty(systemPath))
                return false;

            return !IsAbsolute(systemPath);
        }
    }
}
