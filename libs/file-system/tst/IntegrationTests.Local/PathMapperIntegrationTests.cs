using System.Runtime.InteropServices;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Local.PathMappers;
using Ave.Extensions.Functional;
using FluentAssertions;
using IntegrationTests.FileSystem.Local.TestHelpers;
using Xunit;

namespace IntegrationTests.FileSystem.Local
{
    /// <summary>
    /// Integration tests for path mapping functionality.
    /// </summary>
    public class PathMapperIntegrationTests
    {
        [Theory(DisplayName = "PMIT-001: WindowsPathMapper MapToSystemPath should convert correctly")]
        [InlineData("/c/test/file.txt", @"C:\test\file.txt")]
        [InlineData("/d/data/logs", @"D:\data\logs")]
        [InlineData("/c/Users/test", @"C:\Users\test")]
        [InlineData("/c/", @"C:\")]
        [InlineData("/mnt/d/data/logs", @"D:\data\logs")]
        [InlineData("/mnt/c/Users/test", @"C:\Users\test")]
        public void PMIT001(string canonicalPath, string expectedSystemPath)
        {
            // Arrange
            var mapper = new WindowsPathMapper();
            
            // Act
            var result = mapper.MapToSystemPath(canonicalPath.ToCanonicalPath());
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().Be(expectedSystemPath);
        }

        [Theory(DisplayName = "PMIT-002: WindowsPathMapper MapToCanonicalPath should convert correctly")]
        [InlineData(@"C:\test\file.txt", "/c/test/file.txt")]
        [InlineData(@"D:\data\logs", "/d/data/logs")]
        [InlineData(@"C:\", "/c/")]
        public void PMIT002(string systemPath, string expectedCanonicalPath)
        {
            // Arrange
            var mapper = new WindowsPathMapper();
            
            // Act
            var result = mapper.MapToCanonicalPath(systemPath);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.ToString().Should().Be(expectedCanonicalPath);
        }

        [Theory(DisplayName = "PMIT-003: UnixPathMapper should return paths unchanged")]
        [InlineData("/home/user/file.txt")]
        [InlineData("/var/log/app.log")]
        [InlineData("/tmp/test")]
        public void PMIT003(string path)
        {
            // Arrange
            var mapper = new UnixPathMapper();
            
            // Act
            var systemResult = mapper.MapToSystemPath(path.ToCanonicalPath());
            var canonicalResult = mapper.MapToCanonicalPath(path);
            
            // Assert
            systemResult.IsSuccess.Should().BeTrue();
            systemResult.Value.Should().Be(path);
            
            canonicalResult.IsSuccess.Should().BeTrue();
            canonicalResult.Value.ToString().Should().Be(path);
        }

        [Fact(DisplayName = "PMIT-004: PathMapper round trip should maintain consistency")]
        public void PMIT004()
        {
            // Test Windows-specific paths on Windows, Unix paths on Unix-like systems
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Test Windows path mapper with canonical and system paths that have true round-trip consistency
                var mapper = new WindowsPathMapper();
                var testCases = new[]
                {
                    ("/c/test/file.txt", "/c/test/file.txt"),              // Canonical -> Canonical
                    (@"C:\test\file.txt", "/c/test/file.txt"),             // System -> Canonical
                    ("/d/data/logs", "/d/data/logs"),                      // Canonical -> Canonical
                    (@"D:\data\logs", "/d/data/logs")                      // System -> Canonical
                };
                
                foreach (var (inputPath, expectedCanonical) in testCases)
                {
                    // Convert to canonical, then to system, then back to canonical
                    var canonicalResult = mapper.MapToCanonicalPath(inputPath);
                    canonicalResult.IsSuccess.Should().BeTrue($"Failed to convert '{inputPath}' to canonical");
                    
                    var systemResult = mapper.MapToSystemPath(canonicalResult.Value);
                    systemResult.IsSuccess.Should().BeTrue($"Failed to convert canonical '{canonicalResult.Value}' to system");
                    
                    var finalCanonicalResult = mapper.MapToCanonicalPath(systemResult.Value);
                    finalCanonicalResult.IsSuccess.Should().BeTrue($"Failed to convert system '{systemResult.Value}' back to canonical");
                    
                    // Final canonical should match the expected normalized canonical form
                    finalCanonicalResult.Value.ToString().Should().Be(expectedCanonical, 
                        $"Round trip failed for '{inputPath}': expected {expectedCanonical}, got {finalCanonicalResult.Value}");
                }
            }
            else
            {
                // Test Unix path mapper - Unix paths should remain unchanged
                var mapper = new UnixPathMapper();
                var testPaths = new[]
                {
                    "/home/user/file.txt",
                    "/var/log/app.log", 
                    "/tmp/test"
                };
                
                foreach (var inputPath in testPaths)
                {
                    // Convert to canonical, then to system, then back to canonical
                    var canonicalResult = mapper.MapToCanonicalPath(inputPath);
                    canonicalResult.IsSuccess.Should().BeTrue($"Failed to convert '{inputPath}' to canonical");
                    
                    var systemResult = mapper.MapToSystemPath(canonicalResult.Value);
                    systemResult.IsSuccess.Should().BeTrue($"Failed to convert canonical '{canonicalResult.Value}' to system");
                    
                    var finalCanonicalResult = mapper.MapToCanonicalPath(systemResult.Value);
                    finalCanonicalResult.IsSuccess.Should().BeTrue($"Failed to convert system '{systemResult.Value}' back to canonical");
                    
                    // For Unix mapper, everything should remain the same
                    finalCanonicalResult.Value.ToString().Should().Be(inputPath, 
                        $"Unix path should remain unchanged: {inputPath} -> {finalCanonicalResult.Value}");
                }
            }
        }

        [Theory(DisplayName = "PMIT-005: PathMapper with empty or null path should return failure")]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void PMIT005(string? path)
        {
            // Arrange
            var mapper = new WindowsPathMapper();
            
            // Act
            Result<string, Error> systemResult;
            Result<CanonicalPath, Error> canonicalResult;
            
            // For empty/null paths, we expect ToCanonicalPath to fail, which is the intended behavior
            // The mapper methods should also handle these cases appropriately
            try
            {
                systemResult = mapper.MapToSystemPath(path!.ToCanonicalPath());
            }
            catch
            {
                // If conversion to CanonicalPath fails, that's expected for invalid paths
                systemResult = Result<string, Error>.Failure(new Error(FileSystemErrorCodes.IO.InvalidPath, "Invalid path"));
            }
            
            canonicalResult = mapper.MapToCanonicalPath(path!);
            
            // Assert
            systemResult.IsFailure.Should().BeTrue();
            canonicalResult.IsFailure.Should().BeTrue();
        }

        [Theory(DisplayName = "PMIT-006: WindowsPathMapper with relative path should resolve against base canonical path")]
        [InlineData("docs", "/c/users", "/c/users/docs")]
        [InlineData("src/main.cs", "/c/projects", "/c/projects/src/main.cs")]
        [InlineData(".", "/c/users", "/c/users")]
        public void PMIT006(string relativePath, string basePath, string expected)
        {
            // Arrange
            var mapper = new WindowsPathMapper();
            var baseCanonical = basePath.ToCanonicalPath();

            // Act
            var result = mapper.MapToCanonicalPath(relativePath, baseCanonical);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Value.Should().Be(expected);
        }

        [Theory(DisplayName = "PMIT-007: UnixPathMapper with relative path should resolve against base canonical path")]
        [InlineData("docs", "/home/user", "/home/user/docs")]
        [InlineData("src/main.cs", "/home", "/home/src/main.cs")]
        [InlineData(".", "/home/user", "/home/user")]
        public void PMIT007(string relativePath, string basePath, string expected)
        {
            // Arrange
            var mapper = new UnixPathMapper();
            var baseCanonical = basePath.ToCanonicalPath();

            // Act
            var result = mapper.MapToCanonicalPath(relativePath, baseCanonical);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Value.Should().Be(expected);
        }

        [Theory(DisplayName = "PMIT-008: MapToCanonicalPath with .. segments should navigate up correctly")]
        [InlineData("../sibling", "/home/user", "/home/sibling")]
        [InlineData("../../file.txt", "/a/b/c", "/a/file.txt")]
        [InlineData("../other/sub/file.txt", "/c/projects/current", "/c/projects/other/sub/file.txt")]
        public void PMIT008(string relativePath, string basePath, string expected)
        {
            // Arrange
            var mapper = new UnixPathMapper();
            var baseCanonical = basePath.ToCanonicalPath();

            // Act
            var result = mapper.MapToCanonicalPath(relativePath, baseCanonical);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Value.Should().Be(expected);
        }
    }
}