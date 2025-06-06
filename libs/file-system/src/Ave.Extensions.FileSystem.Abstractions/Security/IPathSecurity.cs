using System.Collections.Generic;
using Ave.Extensions.Functional;
using Ave.Extensions.FileSystem.Models;

namespace Ave.Extensions.FileSystem.Security
{
    /// <summary>
    /// Provides path validation and security features
    /// </summary>
    public interface IPathSecurity
    {
        /// <summary>
        /// Validates if a path is within allowed boundaries
        /// </summary>
        /// <param name="path">The path to validate.</param>
        /// <returns>Success if the path is allowed, failure otherwise.</returns>
        Result<Unit, FileSystemError> ValidatePathAccess(CanonicalPath path);
        
        /// <summary>
        /// Checks if a path matches a wildcard pattern
        /// </summary>
        /// <param name="path">The path to check.</param>
        /// <param name="pattern">The wildcard pattern to match against.</param>
        /// <returns>True if the path matches the pattern.</returns>
        bool MatchesPattern(CanonicalPath path, string pattern);
        
    }
}