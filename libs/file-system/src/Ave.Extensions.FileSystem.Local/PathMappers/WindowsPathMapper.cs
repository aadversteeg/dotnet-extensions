using System.Text.RegularExpressions;
using Ave.Extensions.Functional;

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
        public Result<string, Error> MapToSystemPath(CanonicalPath canonicalPath)
        {
            var pathValue = canonicalPath.Value;
            
            // Handle drive letter format: /d/path -> D:\path
            var driveMatch = DriveLetterRegex.Match(pathValue);
            if (driveMatch.Success)
            {
                var driveLetter = driveMatch.Groups[1].Value.ToUpperInvariant();
                var remainingPath = driveMatch.Groups[2].Value ?? "";
                var systemPath = $"{driveLetter}:{remainingPath.Replace('/', '\\')}";
                return Result<string, Error>.Success(systemPath);
            }
            
            // Handle WSL format: /mnt/c/path -> C:\path
            var wslMatch = WslPathRegex.Match(pathValue);
            if (wslMatch.Success)
            {
                var driveLetter = wslMatch.Groups[1].Value.ToUpperInvariant();
                var remainingPath = wslMatch.Groups[2].Value ?? "";
                var systemPath = $"{driveLetter}:{remainingPath.Replace('/', '\\')}";
                return Result<string, Error>.Success(systemPath);
            }
            
            // Default: assume it's already a Windows path or a relative path
            return Result<string, Error>.Success(pathValue.Replace('/', '\\'));
        }
        
        /// <inheritdoc />
        public Result<CanonicalPath, Error> MapToCanonicalPath(string systemPath)
        {
            if (string.IsNullOrWhiteSpace(systemPath))
                return Result<CanonicalPath, Error>.Failure(Errors.CanonicalPath.PathNullOrEmpty);
            
            // Handle Windows absolute paths: C:\path -> /mnt/c/path
            if (systemPath.Length >= 2 && systemPath[1] == ':')
            {
                var driveLetter = char.ToLowerInvariant(systemPath[0]);
                var remainingPath = systemPath.Substring(2).Replace('\\', '/');
                var driveCanonicalPath = $"/mnt/{driveLetter}{remainingPath}";
                var driveResult = CanonicalPath.Create(driveCanonicalPath);
                if (driveResult.IsFailure)
                    return Result<CanonicalPath, Error>.Failure(driveResult.Error);
                return Result<CanonicalPath, Error>.Success(driveResult.Value);
            }
            
            // Handle UNC paths: \\server\share -> //server/share
            if (systemPath.StartsWith(@"\\"))
            {
                var uncCanonicalPath = systemPath.Replace('\\', '/');
                var uncResult = CanonicalPath.Create(uncCanonicalPath);
                if (uncResult.IsFailure)
                    return Result<CanonicalPath, Error>.Failure(uncResult.Error);
                return Result<CanonicalPath, Error>.Success(uncResult.Value);
            }
            
            // Default: just replace backslashes
            var defaultCanonicalPath = systemPath.Replace('\\', '/');
            var defaultResult = CanonicalPath.Create(defaultCanonicalPath);
            if (defaultResult.IsFailure)
                return Result<CanonicalPath, Error>.Failure(defaultResult.Error);
            return Result<CanonicalPath, Error>.Success(defaultResult.Value);
        }
    }
}