using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.InMemory;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.Functional;
using FluentAssertions;
using UnitTests.Extensions.FileSystem.InMemory.TestHelpers;
using Xunit;

namespace UnitTests.Extensions.FileSystem.InMemory
{
    /// <summary>
    /// Unit tests for InMemoryFileSystem.CreateMonitoringSessionAsync method.
    /// </summary>
    public class CreateMonitoringSessionAsyncTests
    {
        [Fact(DisplayName = "CMSA-001: CreateMonitoringSessionAsync returns error for null options")]
        public async Task CMSA001()
        {
            // Arrange
            var fileSystem = new InMemoryFileSystem();

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
            var fileSystem = new InMemoryFileSystem();
            var options = MonitoringOptions.ForPath("/non/existent/directory".ToCanonicalPath());

            // Act
            var result = await fileSystem.CreateMonitoringSessionAsync(options);

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Message.Should().Contain("Directory not found");
            result.Error.Type.Should().Be(FileSystemErrorType.DirectoryNotFound);
        }

        [Fact(DisplayName = "CMSA-003: CreateMonitoringSessionAsync returns valid session for root directory")]
        public async Task CMSA003()
        {
            // Arrange
            var fileSystem = new InMemoryFileSystem();
            var options = MonitoringOptions.ForPath(CanonicalPath.Root);

            // Act
            var result = await fileSystem.CreateMonitoringSessionAsync(options);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().NotBeNull();
            result.Value.Path.Should().Be(CanonicalPath.Root);
            result.Value.IsActive.Should().BeTrue();
            
            // Clean up
            result.Value.Dispose();
        }

        [Fact(DisplayName = "CMSA-004: CreateMonitoringSessionAsync returns valid session for created directory")]
        public async Task CMSA004()
        {
            // Arrange
            var fileSystem = new InMemoryFileSystem();
            var testPath = "/test/directory".ToCanonicalPath();
            await fileSystem.CreateDirectoryAsync(testPath);
            var options = MonitoringOptions.ForPath(testPath);

            // Act
            var result = await fileSystem.CreateMonitoringSessionAsync(options);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().NotBeNull();
            result.Value.Path.Should().Be(testPath);
            result.Value.IsActive.Should().BeTrue();
            
            // Clean up
            result.Value.Dispose();
        }

        [Fact(DisplayName = "CMSA-005: CreateMonitoringSessionAsync session can be stopped")]
        public async Task CMSA005()
        {
            // Arrange
            var fileSystem = new InMemoryFileSystem();
            var options = MonitoringOptions.ForPath(CanonicalPath.Root);

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