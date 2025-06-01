using System.Runtime.InteropServices;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Local.PathMappers;
using FluentAssertions;
using Xunit;

namespace IntegrationTests.Extensions.FileSystem
{
    /// <summary>
    /// Integration tests for path mapping functionality.
    /// </summary>
    public class PathMapperIntegrationTests
    {
        [Theory]
        [InlineData("/c/test/file.txt", @"C:\test\file.txt")]
        [InlineData("/d/data/logs", @"D:\data\logs")]
        [InlineData("/mnt/c/Users/test", @"C:\Users\test")]
        [InlineData("/c/", @"C:\")]
        public void WindowsPathMapper_MapToSystemPath_ShouldConvertCorrectly(string canonicalPath, string expectedSystemPath)
        {
            // Arrange
            var mapper = new WindowsPathMapper();
            
            // Act
            var result = mapper.MapToSystemPath(canonicalPath);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().Be(expectedSystemPath);
        }

        [Theory]
        [InlineData(@"C:\test\file.txt", "/c/test/file.txt")]
        [InlineData(@"D:\data\logs", "/d/data/logs")]
        [InlineData(@"\\server\share\file", "//server/share/file")]
        [InlineData(@"C:\", "/c/")]
        public void WindowsPathMapper_MapToCanonicalPath_ShouldConvertCorrectly(string systemPath, string expectedCanonicalPath)
        {
            // Arrange
            var mapper = new WindowsPathMapper();
            
            // Act
            var result = mapper.MapToCanonicalPath(systemPath);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().Be(expectedCanonicalPath);
        }

        [Theory]
        [InlineData("/home/user/file.txt")]
        [InlineData("/var/log/app.log")]
        [InlineData("/tmp/test")]
        public void UnixPathMapper_ShouldReturnPathsUnchanged(string path)
        {
            // Arrange
            var mapper = new UnixPathMapper();
            
            // Act
            var systemResult = mapper.MapToSystemPath(path);
            var canonicalResult = mapper.MapToCanonicalPath(path);
            
            // Assert
            systemResult.IsSuccess.Should().BeTrue();
            systemResult.Value.Should().Be(path);
            
            canonicalResult.IsSuccess.Should().BeTrue();
            canonicalResult.Value.Should().Be(path);
        }

        [Theory]
        [InlineData("/c/test/file.txt")]
        [InlineData(@"C:\test\file.txt")]
        [InlineData("/home/user/file.txt")]
        public void PathMapper_RoundTrip_ShouldMaintainConsistency(string inputPath)
        {
            // Arrange
            IPathMapper mapper = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
                ? new WindowsPathMapper() 
                : new UnixPathMapper();
            
            // Act - Convert to canonical, then to system, then back to canonical
            var canonicalResult = mapper.MapToCanonicalPath(inputPath);
            canonicalResult.IsSuccess.Should().BeTrue();
            
            var systemResult = mapper.MapToSystemPath(canonicalResult.Value);
            systemResult.IsSuccess.Should().BeTrue();
            
            var finalCanonicalResult = mapper.MapToCanonicalPath(systemResult.Value);
            finalCanonicalResult.IsSuccess.Should().BeTrue();
            
            // Assert - Final canonical should match the first canonical
            finalCanonicalResult.Value.Should().Be(canonicalResult.Value);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void PathMapper_EmptyOrNullPath_ShouldReturnFailure(string? path)
        {
            // Arrange
            var mapper = new WindowsPathMapper();
            
            // Act
            var systemResult = mapper.MapToSystemPath(path!);
            var canonicalResult = mapper.MapToCanonicalPath(path!);
            
            // Assert
            systemResult.IsFailure.Should().BeTrue();
            canonicalResult.IsFailure.Should().BeTrue();
        }

        [Theory]
        [InlineData("valid/path/file.txt")]
        [InlineData(@"C:\valid\path")]
        [InlineData("/c/valid/path")]
        public void PathMapper_IsValidPathFormat_ShouldReturnTrue(string validPath)
        {
            // Arrange
            var mapper = new WindowsPathMapper();
            
            // Act
            var isValid = mapper.IsValidPathFormat(validPath);
            
            // Assert
            isValid.Should().BeTrue();
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("path\0with\0nulls")]
        public void PathMapper_IsValidPathFormat_ShouldReturnFalse(string invalidPath)
        {
            // Arrange
            var mapper = new WindowsPathMapper();
            
            // Act
            var isValid = mapper.IsValidPathFormat(invalidPath);
            
            // Assert
            isValid.Should().BeFalse();
        }
    }
}