using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Ave.Extensions.Functional;
using Ave.Extensions.FileSystem.Models;

namespace Ave.Extensions.FileSystem.Local.PathMappers
{
    /// <summary>
    /// Windows-specific path mapper that handles drive letters and UNC paths.
    /// </summary>
    public class WindowsPathMapper : IPathMapper
    {
        private static readonly Regex DriveLetterRegex = new(@"^/([a-zA-Z])(/.*)?$");
        private static readonly Regex WslPathRegex = new(@"^/mnt/([a-zA-Z])(/.*)?$");
        
        /// <inheritdoc />
        public Result<string, FileSystemError> MapToSystemPath(string canonicalPath)
        {
            if (string.IsNullOrWhiteSpace(canonicalPath))
                return Result<string, FileSystemError>.Failure(new FileSystemError("Path cannot be empty"));
            
            // Handle drive letter format: /d/path -> D:\path
            var driveMatch = DriveLetterRegex.Match(canonicalPath);
            if (driveMatch.Success)
            {
                var driveLetter = driveMatch.Groups[1].Value.ToUpperInvariant();
                var remainingPath = driveMatch.Groups[2].Value ?? "";
                var systemPath = $"{driveLetter}:{remainingPath.Replace('/', '\\')}";
                return Result<string, FileSystemError>.Success(systemPath);
            }
            
            // Handle WSL format: /mnt/c/path -> C:\path
            var wslMatch = WslPathRegex.Match(canonicalPath);
            if (wslMatch.Success)
            {
                var driveLetter = wslMatch.Groups[1].Value.ToUpperInvariant();
                var remainingPath = wslMatch.Groups[2].Value ?? "";
                var systemPath = $"{driveLetter}:{remainingPath.Replace('/', '\\')}";
                return Result<string, FileSystemError>.Success(systemPath);
            }
            
            // Default: assume it's already a Windows path or a relative path
            return Result<string, FileSystemError>.Success(canonicalPath.Replace('/', '\\'));
        }
        
        /// <inheritdoc />
        public Result<string, FileSystemError> MapToCanonicalPath(string systemPath)
        {
            if (string.IsNullOrWhiteSpace(systemPath))
                return Result<string, FileSystemError>.Failure(new FileSystemError("Path cannot be empty"));
            
            // Handle Windows absolute paths: C:\path -> /c/path
            if (systemPath.Length >= 2 && systemPath[1] == ':')
            {
                var driveLetter = char.ToLowerInvariant(systemPath[0]);
                var remainingPath = systemPath.Substring(2).Replace('\\', '/');
                var canonicalPath = $"/{driveLetter}{remainingPath}";
                return Result<string, FileSystemError>.Success(canonicalPath);
            }
            
            // Handle UNC paths: \\server\share -> //server/share
            if (systemPath.StartsWith(@"\\"))
            {
                var canonicalPath = systemPath.Replace('\\', '/');
                return Result<string, FileSystemError>.Success(canonicalPath);
            }
            
            // Default: just replace backslashes
            return Result<string, FileSystemError>.Success(systemPath.Replace('\\', '/'));
        }
        
        /// <inheritdoc />
        public bool IsValidPathFormat(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;
            
            // Check for invalid characters
            var invalidChars = Path.GetInvalidPathChars();
            return !path.Any(c => invalidChars.Contains(c));
        }
    }
}