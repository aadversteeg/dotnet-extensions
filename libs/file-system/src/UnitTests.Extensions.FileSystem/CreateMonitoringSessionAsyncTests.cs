using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.Functional;
using FluentAssertions;
using Moq;
using UnitTests.Extensions.FileSystem.TestHelpers;

namespace UnitTests.Extensions.FileSystem
{
    /// <summary>
    /// Unit tests for ReadOnlyFileSystemWrapper.CreateMonitoringSessionAsync method.
    /// </summary>
    public class CreateMonitoringSessionAsyncTests
    {
        [Fact(DisplayName = "CMSA-001: ReadOnlyFileSystemWrapper delegates CreateMonitoringSessionAsync to underlying file system")]
        public async Task CMSA001()
        {
            // Arrange
            var mockSession = new Mock<IMonitoringSession>();
            var expectedResult = Result<IMonitoringSession, FileSystemError>.Success(mockSession.Object);
            var options = MonitoringOptions.ForPath("/test".ToCanonicalPath());
            
            var mockFileSystem = new Mock<IFileSystem>();
            mockFileSystem.Setup(fs => fs.CreateMonitoringSessionAsync(options, default))
                .ReturnsAsync(expectedResult);

            var wrapper = mockFileSystem.Object.AsReadOnly();

            // Act
            var result = await wrapper.CreateMonitoringSessionAsync(options);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeSameAs(mockSession.Object);
            mockFileSystem.Verify(fs => fs.CreateMonitoringSessionAsync(options, default), Times.Once);
        }

        [Fact(DisplayName = "CMSA-002: ReadOnlyFileSystemWrapper propagates errors from underlying file system")]
        public async Task CMSA002()
        {
            // Arrange
            var expectedError = new FileSystemError("Test error", "/test", FileSystemErrorType.DirectoryNotFound);
            var expectedResult = Result<IMonitoringSession, FileSystemError>.Failure(expectedError);
            var options = MonitoringOptions.ForPath("/test".ToCanonicalPath());
            
            var mockFileSystem = new Mock<IFileSystem>();
            mockFileSystem.Setup(fs => fs.CreateMonitoringSessionAsync(options, default))
                .ReturnsAsync(expectedResult);

            var wrapper = mockFileSystem.Object.AsReadOnly();

            // Act
            var result = await wrapper.CreateMonitoringSessionAsync(options);

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Should().Be(expectedError);
            mockFileSystem.Verify(fs => fs.CreateMonitoringSessionAsync(options, default), Times.Once);
        }
    }
}