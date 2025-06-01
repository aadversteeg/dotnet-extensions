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
        private readonly IPathUtilities _pathUtilities;
        
        /// <summary>
        /// Initializes a new instance of the PathSecurityService class.
        /// </summary>
        /// <param name="pathUtilities">Path utilities for normalization.</param>
        public PathSecurityService(IPathUtilities pathUtilities)
        {
            _pathUtilities = pathUtilities ?? throw new ArgumentNullException(nameof(pathUtilities));
        }
        
        /// <inheritdoc />
        public Result<Unit, FileSystemError> ValidatePathAccess(string path, IEnumerable<string> allowedPaths)
        {
            if (string.IsNullOrWhiteSpace(path))
                return Result<Unit, FileSystemError>.Failure(
                    new FileSystemError("Path cannot be empty", path, FileSystemErrorType.InvalidPath));
            
            var normalizedPath = NormalizePath(path);
            var allowedPathsList = allowedPaths?.ToList() ?? new List<string>();
            
            // If no allowed paths specified, allow all
            if (!allowedPathsList.Any())
                return Result<Unit, FileSystemError>.Success(Unit.Value);
            
            foreach (var allowedPath in allowedPathsList)
            {
                var normalizedAllowedPath = NormalizePath(allowedPath);
                
                // Check direct match or subdirectory
                if (normalizedPath.Equals(normalizedAllowedPath, StringComparison.OrdinalIgnoreCase) ||
                    normalizedPath.StartsWith(normalizedAllowedPath + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return Result<Unit, FileSystemError>.Success(Unit.Value);
                }
                
                // Check wildcard patterns
                if (allowedPath.Contains('*') || allowedPath.Contains('?'))
                {
                    if (MatchesPattern(normalizedPath, normalizedAllowedPath))
                    {
                        return Result<Unit, FileSystemError>.Success(Unit.Value);
                    }
                }
            }
            
            return Result<Unit, FileSystemError>.Failure(
                new FileSystemError($"Access to path '{path}' is not allowed", path, FileSystemErrorType.AccessDenied));
        }
        
        /// <inheritdoc />
        public bool MatchesPattern(string path, string pattern)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(pattern))
                return false;
            
            try
            {
                // If pattern doesn't contain path separators, match against filename only
                if (!pattern.Contains('/') && !pattern.Contains('\\'))
                {
                    var fileName = _pathUtilities.GetFileName(path);
                    return MatchesWildcardPattern(fileName, pattern);
                }
                
                // For path-based patterns, handle glob-style matching
                return MatchesPathPattern(path, pattern);
            }
            catch (Exception)
            {
                // If pattern matching fails, fall back to simple string comparison
                return path.Equals(pattern, StringComparison.OrdinalIgnoreCase);
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
            var normalizedPath = NormalizePath(path);
            var normalizedPattern = NormalizePath(pattern);
            
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
        
        /// <inheritdoc />
        public string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;
            
            // Convert to canonical format using path utilities
            var normalized = _pathUtilities.ToCanonicalPath(path);
            
            // Remove trailing slashes (except for root)
            if (normalized.Length > 1 && normalized.EndsWith('/'))
                normalized = normalized.TrimEnd('/');
            
            // Resolve . and .. components
            var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var stack = new Stack<string>();
            
            foreach (var part in parts)
            {
                if (part == ".")
                    continue;
                else if (part == ".." && stack.Count > 0)
                    stack.Pop();
                else if (part != "..")
                    stack.Push(part);
            }
            
            var result = "/" + string.Join("/", stack.Reverse());
            return result == "/" ? "/" : result;
        }
    }
}