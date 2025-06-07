using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Local;
using Ave.Extensions.FileSystem.Local.PathMappers;
using Ave.Extensions.FileSystem.Models;
using FluentAssertions;
using UnitTests.Extensions.FileSystem.Local.TestHelpers;

namespace UnitTests.Extensions.FileSystem.Local
{
    /// <summary>
    /// Unit tests for LocalFileSystem.CreateMonitoringSessionAsync method.
    /// </summary>
    public class CreateMonitoringSessionAsyncTests
    {
        private static IPathMapper CreatePathMapper()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
                ? new WindowsPathMapper() 
                : new UnixPathMapper();
        }
        [Fact(DisplayName = "CMSA-001: CreateMonitoringSessionAsync returns error for null options")]
        public async Task CMSA001()
        {
            // Arrange
            var fileSystem = new LocalFileSystem(CreatePathMapper());

            // Act
            var result = await fileSystem.CreateMonitoringSessionAsync(null!);

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Message.Should().Be("Monitoring options cannot be null");
            result.Error.Type.Should().Be(FileSystemErrorType.Unknown);
        }

        [Fact(DisplayName = "CMSA-002: CreateMonitoringSessionAsync returns error for non-existent directory")]
        public async Task CMSA002()
        {
            // Arrange
            var fileSystem = new LocalFileSystem(CreatePathMapper());
            var options = MonitoringOptions.ForPath("/non/existent/directory".ToCanonicalPath());

            // Act
            var result = await fileSystem.CreateMonitoringSessionAsync(options);

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Message.Should().Contain("Directory not found");
            result.Error.Type.Should().Be(FileSystemErrorType.DirectoryNotFound);
        }

        [Fact(DisplayName = "CMSA-003: CreateMonitoringSessionAsync returns valid session for valid directory")]
        public async Task CMSA003()
        {
            // Arrange
            var pathMapper = CreatePathMapper();
            var fileSystem = new LocalFileSystem(pathMapper);
            var tempDir = System.IO.Path.GetTempPath();
            var canonicalPathResult = pathMapper.MapToCanonicalPath(tempDir);
            canonicalPathResult.IsSuccess.Should().BeTrue();
            var canonicalPath = canonicalPathResult.Value;
            var options = MonitoringOptions.ForPath(canonicalPath);

            // Act
            var result = await fileSystem.CreateMonitoringSessionAsync(options);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().NotBeNull();
            result.Value.Path.Should().Be(canonicalPath);
            result.Value.IsActive.Should().BeTrue();
            
            // Clean up
            result.Value.Dispose();
        }

        [Fact(DisplayName = "CMSA-004: CreateMonitoringSessionAsync session can be stopped")]
        public async Task CMSA004()
        {
            // Arrange
            var pathMapper = CreatePathMapper();
            var fileSystem = new LocalFileSystem(pathMapper);
            var tempDir = System.IO.Path.GetTempPath();
            var canonicalPathResult = pathMapper.MapToCanonicalPath(tempDir);
            canonicalPathResult.IsSuccess.Should().BeTrue();
            var canonicalPath = canonicalPathResult.Value;
            var options = MonitoringOptions.ForPath(canonicalPath);

            // Act
            var result = await fileSystem.CreateMonitoringSessionAsync(options);
            result.IsSuccess.Should().BeTrue();
            
            var session = result.Value;
            session.IsActive.Should().BeTrue();
            
            await session.StopAsync();

            // Assert
            session.IsActive.Should().BeFalse();
            
            // Clean up
            session.Dispose();
        }
    }
}