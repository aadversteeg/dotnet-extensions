using System.Collections.Generic;
using System.Linq;
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
        /// <param name="allowedPaths">Collection of allowed canonical paths.</param>
        public PathSecurityService(IEnumerable<CanonicalPath>? allowedPaths = null)
        {
            _allowedPaths = allowedPaths?.ToList() ?? [];
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
            }
            
            return Result<Unit, FileSystemError>.Failure(
                new FileSystemError($"Access to path '{path.Value}' is not allowed", path.Value, FileSystemErrorType.AccessDenied));
        }

    }
}