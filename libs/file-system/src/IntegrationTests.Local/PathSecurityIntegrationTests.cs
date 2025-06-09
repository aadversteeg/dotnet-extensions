using System.Linq;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Security;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IntegrationTests.FileSystem.Local
{
    /// <summary>
    /// Integration tests for path security functionality.
    /// </summary>
    public class PathSecurityIntegrationTests
    {
        [Theory(DisplayName = "PSIT-001: PathSecurity ValidatePathAccess should work correctly")]
        [InlineData("/app/data", new[] { "/app/data" }, true)]
        [InlineData("/app/data/file.txt", new[] { "/app/data" }, true)]
        [InlineData("/app/data/sub/file.txt", new[] { "/app/data" }, true)]
        [InlineData("/unauthorized/file.txt", new[] { "/app/data" }, false)]
        [InlineData("/tmp/file.txt", new[] { "/tmp" }, true)]
        [InlineData("/tmp/sub/file.txt", new[] { "/tmp" }, true)]
        [InlineData("/logs/app.log", new[] { "/logs" }, true)]
        [InlineData("/logs/sub/app.log", new[] { "/logs" }, true)]
        public void PSIT001(string path, string[] allowedPaths, bool shouldBeAllowed)
        {
            // Arrange
            var canonicalAllowedPaths = allowedPaths.Select(p => CanonicalPath.Create(p).Value);
            var pathSecurity = new PathSecurityService(canonicalAllowedPaths);
            var canonicalPath = CanonicalPath.Create(path).Value;
            
            // Act
            var result = pathSecurity.ValidatePathAccess(canonicalPath);
            
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

        [Fact(DisplayName = "PSIT-002: PathSecurity ValidatePathAccess with no allowed paths should allow all")]
        public void PSIT002()
        {
            // Arrange
            var allowedPaths = new CanonicalPath[0];
            var pathSecurity = new PathSecurityService(allowedPaths);
            var canonicalPath = CanonicalPath.Create("/any/path/file.txt").Value;
            
            // Act
            var result = pathSecurity.ValidatePathAccess(canonicalPath);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
        }

        [Fact(DisplayName = "PSIT-003: PathSecurity ValidatePathAccess with invalid CanonicalPath should fail before calling ValidatePathAccess")]
        public void PSIT003()
        {
            // Arrange & Act
            var invalidPathResult = CanonicalPath.Create("");
            var whitespacePathResult = CanonicalPath.Create("   ");
            
            // Assert
            invalidPathResult.IsFailure.Should().BeTrue();
            whitespacePathResult.IsFailure.Should().BeTrue();
        }

        [Fact(DisplayName = "PSIT-004: PathSecurity via DI registration should respect AllowedPaths configuration")]
        public void PSIT004()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddFileSystemUtilities(options =>
            {
                options.EnablePathSecurity = true;
                options.AllowedPaths.Add("/tmp/allowed");
            });

            using var serviceProvider = services.BuildServiceProvider();
            var pathSecurity = serviceProvider.GetRequiredService<IPathSecurity>();

            var allowedPath = CanonicalPath.Create("/tmp/allowed/file.txt").Value;
            var forbiddenPath = CanonicalPath.Create("/tmp/forbidden/file.txt").Value;

            // Act
            var allowedResult = pathSecurity.ValidatePathAccess(allowedPath);
            var forbiddenResult = pathSecurity.ValidatePathAccess(forbiddenPath);

            // Assert
            allowedResult.IsSuccess.Should().BeTrue("access to allowed path should succeed");
            forbiddenResult.IsFailure.Should().BeTrue("access to forbidden path should fail");
            forbiddenResult.Error.Type.Should().Be(FileSystemErrorType.AccessDenied);
        }
    }
}