using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Ave.Extensions.Functional;
using Ave.Extensions.FileSystem.Models;

namespace Ave.Extensions.FileSystem.Security
{
    /// <summary>
    /// Default implementation of path security validation.
    /// </summary>
    public class PathSecurityService : IPathSecurity
    {
        private readonly IReadOnlyList<CanonicalPath> _allowedPaths;
        
        /// <summary>
        /// Initializes a new instance of the PathSecurityService class.
        /// </summary>
        /// <param name="allowedPaths">Collection of allowed canonical path patterns.</param>
        public PathSecurityService(IEnumerable<CanonicalPath>? allowedPaths = null)
        {
            _allowedPaths = allowedPaths?.ToList() ?? new List<CanonicalPath>();
        }
        
        /// <inheritdoc />
        public Result<Unit, FileSystemError> ValidatePathAccess(CanonicalPath path)
        {
            // If no allowed paths specified, allow all
            if (!_allowedPaths.Any())
                return Result<Unit, FileSystemError>.Success(Unit.Value);
            
            foreach (var allowedPath in _allowedPaths)
            {
                // Check direct match or if path is a child of allowed path
                if (path.Equals(allowedPath) || path.IsChildOf(allowedPath))
                {
                    return Result<Unit, FileSystemError>.Success(Unit.Value);
                }
                
                // Check wildcard patterns
                if (allowedPath.Value.Contains('*') || allowedPath.Value.Contains('?'))
                {
                    if (MatchesPattern(path, allowedPath.Value))
                    {
                        return Result<Unit, FileSystemError>.Success(Unit.Value);
                    }
                }
            }
            
            return Result<Unit, FileSystemError>.Failure(
                new FileSystemError($"Access to path '{path.Value}' is not allowed", path.Value, FileSystemErrorType.AccessDenied));
        }
        
        /// <inheritdoc />
        public bool MatchesPattern(CanonicalPath path, string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
                return false;
            
            try
            {
                // If pattern doesn't contain path separators, match against filename only
                if (!pattern.Contains('/') && !pattern.Contains('\\'))
                {
                    var fileName = path.GetName();
                    return MatchesWildcardPattern(fileName, pattern);
                }
                
                // For path-based patterns, handle glob-style matching
                return MatchesPathPattern(path.Value, pattern);
            }
            catch (Exception)
            {
                // If pattern matching fails, fall back to simple string comparison
                return path.Value.Equals(pattern, StringComparison.OrdinalIgnoreCase);
            }
        }
        
        private bool MatchesWildcardPattern(string text, string pattern)
        {
            // Convert wildcard pattern to regex for filename matching
            var regexPattern = "^" + Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
                
            return Regex.IsMatch(text, regexPattern, RegexOptions.IgnoreCase);
        }
        
        private bool MatchesPathPattern(string path, string pattern)
        {
            // Normalize the input path using CanonicalPath if possible, otherwise basic normalization
            var normalizedPath = path.Replace('\\', '/');
            var canonicalPathResult = CanonicalPath.Create(normalizedPath);
            if (canonicalPathResult.IsSuccess)
            {
                normalizedPath = canonicalPathResult.Value.Value;
            }
            
            // Normalize the pattern (assume it's a valid path pattern)
            var normalizedPattern = pattern.Replace('\\', '/');
            var canonicalPatternResult = CanonicalPath.Create(normalizedPattern);
            if (canonicalPatternResult.IsSuccess)
            {
                normalizedPattern = canonicalPatternResult.Value.Value;
            }
            
            // Handle recursive wildcard /** 
            if (normalizedPattern.EndsWith("/**"))
            {
                var basePath = normalizedPattern.Substring(0, normalizedPattern.Length - 3);
                return normalizedPath.Equals(basePath, StringComparison.OrdinalIgnoreCase) ||
                       normalizedPath.StartsWith(basePath + "/", StringComparison.OrdinalIgnoreCase);
            }
            
            // Handle single-level wildcard /*
            if (normalizedPattern.EndsWith("/*"))
            {
                var basePath = normalizedPattern.Substring(0, normalizedPattern.Length - 2);
                if (!normalizedPath.StartsWith(basePath + "/", StringComparison.OrdinalIgnoreCase))
                    return false;
                
                var remainingPath = normalizedPath.Substring(basePath.Length + 1);
                // Should not contain additional path separators (single level only)
                return !remainingPath.Contains('/');
            }
            
            // Regular wildcard pattern matching for other cases
            var regexPattern = "^" + Regex.Escape(normalizedPattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
                
            return Regex.IsMatch(normalizedPath, regexPattern, RegexOptions.IgnoreCase);
        }
        
    }
}