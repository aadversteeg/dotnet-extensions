using System;
using Ave.Extensions.FileSystem;

namespace IntegrationTests.FileSystem.Local.TestHelpers
{
    /// <summary>
    /// Extension methods to make test code easier to read when working with CanonicalPath.
    /// </summary>
    public static class CanonicalPathExtensions
    {
        /// <summary>
        /// Converts a string path to a CanonicalPath for testing purposes.
        /// Throws an exception if the path is invalid (which is appropriate for tests).
        /// </summary>
        /// <param name="path">The string path to convert.</param>
        /// <returns>A valid CanonicalPath.</returns>
        public static CanonicalPath ToCanonicalPath(this string path)
        {
            var result = CanonicalPath.Create(path);
            if (result.IsFailure)
                throw new ArgumentException($"Invalid test path: {path}. Error: {result.Error.Message}", nameof(path));
            return result.Value;
        }
    }
}