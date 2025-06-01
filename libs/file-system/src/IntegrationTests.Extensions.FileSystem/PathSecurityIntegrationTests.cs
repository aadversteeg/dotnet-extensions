using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Security;
using FluentAssertions;
using Xunit;

namespace IntegrationTests.Extensions.FileSystem
{
    /// <summary>
    /// Integration tests for path security functionality.
    /// </summary>
    public class PathSecurityIntegrationTests
    {
        [Theory]
        [InlineData("/app/data", new[] { "/app/data" }, true)]
        [InlineData("/app/data/file.txt", new[] { "/app/data" }, true)]
        [InlineData("/app/data/sub/file.txt", new[] { "/app/data" }, true)]
        [InlineData("/unauthorized/file.txt", new[] { "/app/data" }, false)]
        [InlineData("/tmp/file.txt", new[] { "/tmp/*" }, true)]
        [InlineData("/tmp/sub/file.txt", new[] { "/tmp/*" }, false)]
        [InlineData("/logs/app.log", new[] { "/logs/**" }, true)]
        [InlineData("/logs/sub/app.log", new[] { "/logs/**" }, true)]
        public void PathSecurity_ValidatePathAccess_ShouldWorkCorrectly(string path, string[] allowedPaths, bool shouldBeAllowed)
        {
            // Arrange
            var pathUtilities = new PathUtilities();
            var pathSecurity = new PathSecurityService(pathUtilities);
            
            // Act
            var result = pathSecurity.ValidatePathAccess(path, allowedPaths);
            
            // Assert
            if (shouldBeAllowed)
            {
                result.IsSuccess.Should().BeTrue();
            }
            else
            {
                result.IsFailure.Should().BeTrue();
                result.Error.Type.Should().Be(FileSystemErrorType.AccessDenied);
            }
        }

        [Theory]
        [InlineData("/config/app.json", "*.json", true)]
        [InlineData("/config/app.xml", "*.json", false)]
        [InlineData("/data/test.txt", "test.*", true)]
        [InlineData("/data/other.txt", "test.*", false)]
        [InlineData("/app/file.log", "*", true)]
        [InlineData("/app/file.log", "file.?og", true)]
        public void PathSecurity_MatchesPattern_ShouldWorkWithWildcards(string path, string pattern, bool shouldMatch)
        {
            // Arrange
            var pathUtilities = new PathUtilities();
            var pathSecurity = new PathSecurityService(pathUtilities);
            
            // Act
            var matches = pathSecurity.MatchesPattern(path, pattern);
            
            // Assert
            matches.Should().Be(shouldMatch);
        }

        [Theory]
        [InlineData("/app/../data/file.txt", "/data/file.txt")]
        [InlineData("/app/./data/file.txt", "/app/data/file.txt")]
        [InlineData("/app//data///file.txt", "/app/data/file.txt")]
        [InlineData("/app/data/", "/app/data")]
        [InlineData("/", "/")]
        public void PathSecurity_NormalizePath_ShouldResolvePathComponents(string inputPath, string expectedPath)
        {
            // Arrange
            var pathUtilities = new PathUtilities();
            var pathSecurity = new PathSecurityService(pathUtilities);
            
            // Act
            var normalized = pathSecurity.NormalizePath(inputPath);
            
            // Assert
            normalized.Should().Be(expectedPath);
        }

        [Fact]
        public void PathSecurity_ValidatePathAccess_WithNoAllowedPaths_ShouldAllowAll()
        {
            // Arrange
            var pathUtilities = new PathUtilities();
            var pathSecurity = new PathSecurityService(pathUtilities);
            var allowedPaths = new string[0];
            
            // Act
            var result = pathSecurity.ValidatePathAccess("/any/path/file.txt", allowedPaths);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void PathSecurity_ValidatePathAccess_WithEmptyPath_ShouldReturnFailure(string? emptyPath)
        {
            // Arrange
            var pathUtilities = new PathUtilities();
            var pathSecurity = new PathSecurityService(pathUtilities);
            var allowedPaths = new[] { "/app/data" };
            
            // Act
            var result = pathSecurity.ValidatePathAccess(emptyPath!, allowedPaths);
            
            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Type.Should().Be(FileSystemErrorType.InvalidPath);
        }
    }
}