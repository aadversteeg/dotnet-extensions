using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

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
        Result<Unit, Error> ValidatePathAccess(CanonicalPath path);
        
        
    }
}