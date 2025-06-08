using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Mapping;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Traversal;
using Ave.Extensions.FileSystem.Traversal.Handlers;
using Ave.Extensions.Functional;
using FluentAssertions;
using Moq;
using UnitTests.Extensions.FileSystem.TestHelpers;

namespace UnitTests.Extensions.FileSystem
{
    public class ReadOnlyFileSystemWrapperTests
    {
        [Fact(DisplayName = "RFSW-001: Wrapper delegates FileExistsAsync to underlying filesystem")]
        public async Task RFSW001()
        {
            // Arrange
            var expectedResult = Result<bool, FileSystemError>.Success(true);
            var mockFileSystem = new Mock<IFileSystem>();
            mockFileSystem.Setup(fs => fs.FileExistsAsync("/test.txt".ToCanonicalPath(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedResult);
            
            var wrapper = mockFileSystem.Object.AsReadOnly();
            
            // Act
            var result = await wrapper.FileExistsAsync("/test.txt".ToCanonicalPath());
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeTrue();
            mockFileSystem.Verify(fs => fs.FileExistsAsync("/test.txt".ToCanonicalPath(), It.IsAny<CancellationToken>()), Times.Once);
        }
        
        [Fact(DisplayName = "RFSW-002: Wrapper delegates DirectoryExistsAsync to underlying filesystem")]
        public async Task RFSW002()
        {
            // Arrange
            var expectedResult = Result<bool, FileSystemError>.Success(true);
            var mockFileSystem = new Mock<IFileSystem>();
            mockFileSystem.Setup(fs => fs.DirectoryExistsAsync("/test".ToCanonicalPath(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedResult);
            
            var wrapper = mockFileSystem.Object.AsReadOnly();
            
            // Act
            var result = await wrapper.DirectoryExistsAsync("/test".ToCanonicalPath());
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeTrue();
            mockFileSystem.Verify(fs => fs.DirectoryExistsAsync("/test".ToCanonicalPath(), It.IsAny<CancellationToken>()), Times.Once);
        }
        
        [Fact(DisplayName = "RFSW-003: Wrapper delegates ReadAllTextAsync to underlying filesystem")]
        public async Task RFSW003()
        {
            // Arrange
            const string expectedContent = "file content";
            var expectedResult = Result<string, FileSystemError>.Success(expectedContent);
            var mockFileSystem = new Mock<IFileSystem>();
            mockFileSystem.Setup(fs => fs.ReadAllTextAsync("/test.txt".ToCanonicalPath(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedResult);
            
            var wrapper = mockFileSystem.Object.AsReadOnly();
            
            // Act
            var result = await wrapper.ReadAllTextAsync("/test.txt".ToCanonicalPath());
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().Be(expectedContent);
            mockFileSystem.Verify(fs => fs.ReadAllTextAsync("/test.txt".ToCanonicalPath(), It.IsAny<CancellationToken>()), Times.Once);
        }
        
        [Fact(DisplayName = "RFSW-004: Wrapper delegates GetFilesAsync to underlying filesystem")]
        public async Task RFSW004()
        {
            // Arrange
            var mockFileSystem = new Mock<IFileSystem>();
            
            // Mock TraverseAsync which is called by the GetFilesAsync extension method
            var summary = new TraversalSummary(2, 0, 0, TimeSpan.Zero, false);
            mockFileSystem.Setup(fs => fs.TraverseAsync(
                    "/test".ToCanonicalPath(),
                    It.IsAny<ITraversalHandler<FileSystemEntry>>(),
                    It.IsAny<IFileSystemEntryMapper<FileSystemEntry>>(),
                    It.IsAny<TraversalOptions>(),
                    It.IsAny<CancellationToken>()))
                .Returns((CanonicalPath path, ITraversalHandler<FileSystemEntry> handler, IFileSystemEntryMapper<FileSystemEntry> mapper, TraversalOptions options, CancellationToken token) =>
                {
                    // Simulate the PathCollectorHandler collecting files
                    if (handler is PathCollectorHandler<FileSystemEntry> collector)
                    {
                        // Use reflection to add items to the internal list since we can't access it directly
                        var pathsField = typeof(PathCollectorHandler<FileSystemEntry>).GetField("_paths", 
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var pathsList = (List<CanonicalPath>)pathsField!.GetValue(collector)!;
                        pathsList.Add("/test/file1.txt".ToCanonicalPath());
                        pathsList.Add("/test/file2.txt".ToCanonicalPath());
                    }
                    return Task.FromResult(Result<TraversalSummary, FileSystemError>.Success(summary));
                });
            
            var wrapper = mockFileSystem.Object.AsReadOnly();
            
            // Act
            var result = await wrapper.GetFilesAsync("/test".ToCanonicalPath(), "*.txt", recursive: false);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().HaveCount(2);
            result.Value.Should().Contain("/test/file1.txt".ToCanonicalPath());
            result.Value.Should().Contain("/test/file2.txt".ToCanonicalPath());
            mockFileSystem.Verify(fs => fs.TraverseAsync(
                "/test".ToCanonicalPath(),
                It.IsAny<ITraversalHandler<FileSystemEntry>>(),
                It.IsAny<IFileSystemEntryMapper<FileSystemEntry>>(),
                It.IsAny<TraversalOptions>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }
        
        [Fact(DisplayName = "RFSW-005: Wrapper delegates GetDirectoriesAsync to underlying filesystem")]
        public async Task RFSW005()
        {
            // Arrange
            var mockFileSystem = new Mock<IFileSystem>();
            
            // Mock TraverseAsync which is called by the GetDirectoriesAsync extension method
            var summary = new TraversalSummary(0, 2, 0, TimeSpan.Zero, false);
            mockFileSystem.Setup(fs => fs.TraverseAsync(
                    "/test".ToCanonicalPath(),
                    It.IsAny<ITraversalHandler<FileSystemEntry>>(),
                    It.IsAny<IFileSystemEntryMapper<FileSystemEntry>>(),
                    It.IsAny<TraversalOptions>(),
                    It.IsAny<CancellationToken>()))
                .Returns((CanonicalPath path, ITraversalHandler<FileSystemEntry> handler, IFileSystemEntryMapper<FileSystemEntry> mapper, TraversalOptions options, CancellationToken token) =>
                {
                    // Simulate the PathCollectorHandler collecting directories
                    if (handler is PathCollectorHandler<FileSystemEntry> collector)
                    {
                        // Use reflection to add items to the internal list since we can't access it directly
                        var pathsField = typeof(PathCollectorHandler<FileSystemEntry>).GetField("_paths", 
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var pathsList = (List<CanonicalPath>)pathsField!.GetValue(collector)!;
                        pathsList.Add("/test/dir1".ToCanonicalPath());
                        pathsList.Add("/test/dir2".ToCanonicalPath());
                    }
                    return Task.FromResult(Result<TraversalSummary, FileSystemError>.Success(summary));
                });
            
            var wrapper = mockFileSystem.Object.AsReadOnly();
            
            // Act
            var result = await wrapper.GetDirectoriesAsync("/test".ToCanonicalPath(), "*", recursive: false);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().HaveCount(2);
            result.Value.Should().Contain("/test/dir1".ToCanonicalPath());
            result.Value.Should().Contain("/test/dir2".ToCanonicalPath());
            mockFileSystem.Verify(fs => fs.TraverseAsync(
                "/test".ToCanonicalPath(),
                It.IsAny<ITraversalHandler<FileSystemEntry>>(),
                It.IsAny<IFileSystemEntryMapper<FileSystemEntry>>(),
                It.IsAny<TraversalOptions>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact(DisplayName = "RFSW-006: Wrapper delegates GetEntryAsync to underlying filesystem for files")]
        public async Task RFSW006()
        {
            // Arrange
            var entry = new FileSystemEntry(
                path: "/test/file.txt".ToCanonicalPath(),
                isDirectory: false,
                size: 1024,
                extension: ".txt",
                createdUtc: DateTime.UtcNow,
                modifiedUtc: DateTime.UtcNow,
                lastAccessUtc: DateTime.UtcNow,
                attributes: FileAttributes.Normal);
            var expectedResult = Result<FileSystemEntry, FileSystemError>.Success(entry);
            var mockFileSystem = new Mock<IFileSystem>();
            mockFileSystem.Setup(fs => fs.GetEntryAsync("/test/file.txt".ToCanonicalPath(), It.IsAny<IFileSystemEntryMapper<FileSystemEntry>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedResult);
            
            var wrapper = mockFileSystem.Object.AsReadOnly();
            
            // Act
            var result = await wrapper.GetEntryAsync("/test/file.txt".ToCanonicalPath(), IdentityMapper.Instance);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeEquivalentTo(entry);
            mockFileSystem.Verify(fs => fs.GetEntryAsync("/test/file.txt".ToCanonicalPath(), It.IsAny<IFileSystemEntryMapper<FileSystemEntry>>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact(DisplayName = "RFSW-007: Wrapper delegates GetEntryAsync to underlying filesystem for directories")]
        public async Task RFSW007()
        {
            // Arrange
            var entry = new FileSystemEntry(
                path: "/test/dir".ToCanonicalPath(),
                isDirectory: true,
                size: null,
                extension: null,
                createdUtc: DateTime.UtcNow,
                modifiedUtc: DateTime.UtcNow,
                lastAccessUtc: DateTime.UtcNow,
                attributes: FileAttributes.Directory);
            var expectedResult = Result<FileSystemEntry, FileSystemError>.Success(entry);
            var mockFileSystem = new Mock<IFileSystem>();
            mockFileSystem.Setup(fs => fs.GetEntryAsync("/test/dir".ToCanonicalPath(), It.IsAny<IFileSystemEntryMapper<FileSystemEntry>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedResult);
            
            var wrapper = mockFileSystem.Object.AsReadOnly();
            
            // Act
            var result = await wrapper.GetEntryAsync("/test/dir".ToCanonicalPath(), IdentityMapper.Instance);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeEquivalentTo(entry);
            mockFileSystem.Verify(fs => fs.GetEntryAsync("/test/dir".ToCanonicalPath(), It.IsAny<IFileSystemEntryMapper<FileSystemEntry>>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact(DisplayName = "RFSW-008: Wrapper delegates OpenReadAsync to underlying filesystem")]
        public async Task RFSW008()
        {
            // Arrange
            var stream = new MemoryStream();
            var expectedResult = Result<Stream, FileSystemError>.Success(stream);
            var mockFileSystem = new Mock<IFileSystem>();
            mockFileSystem.Setup(fs => fs.OpenReadAsync("/test/file.txt".ToCanonicalPath(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedResult);
            
            var wrapper = mockFileSystem.Object.AsReadOnly();
            
            // Act
            var result = await wrapper.OpenReadAsync("/test/file.txt".ToCanonicalPath());
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeSameAs(stream);
            mockFileSystem.Verify(fs => fs.OpenReadAsync("/test/file.txt".ToCanonicalPath(), It.IsAny<CancellationToken>()), Times.Once);
        }
        
        [Fact(DisplayName = "RFSW-009: Wrapper protects against write operations")]
        public void RFSW009()
        {
            // Arrange
            var mockFileSystem = new Mock<IFileSystem>();
            var wrapper = mockFileSystem.Object.AsReadOnly();
            
            // Act
            var wrapperType = wrapper.GetType();
            
            // Assert
            // Verify the wrapper doesn't expose any methods from the IFileSystem interface
            // that aren't in IReadOnlyFileSystem
            var writeMethodNames = new[] {
                "WriteAllTextAsync",
                "DeleteFileAsync", 
                "CreateDirectoryAsync",
                "DeleteDirectoryAsync",
                "OpenWriteAsync",
                "OpenAsync"
            };
            
            foreach (var methodName in writeMethodNames)
            {
                // Check if the wrapper type declares this method (it shouldn't)
                var methods = wrapperType.GetMethods()
                    .Where(m => m.Name == methodName)
                    .ToList();
                
                methods.Should().BeEmpty($"Wrapper should not expose {methodName}");
            }
            
            // Verify the wrapper only implements IReadOnlyFileSystem (not IFileSystem)
            wrapperType.GetInterfaces().Should().Contain(typeof(IReadOnlyFileSystem));
            wrapperType.GetInterfaces().Should().NotContain(typeof(IFileSystem));
        }

        [Fact(DisplayName = "RFSW-011: Info property returns read-only characteristics")]
        public void RFSW011()
        {
            // Arrange
            var mockFileSystemInfo = new Ave.Extensions.FileSystem.FileSystemInfo(
                isDurable: true,
                supportsMonitoring: false,
                isReadOnly: false  // Original file system is not read-only
            );
            var mockFileSystem = new Mock<IFileSystem>();
            mockFileSystem.Setup(fs => fs.Info).Returns(mockFileSystemInfo);
            
            var wrapper = mockFileSystem.Object.AsReadOnly();
            
            // Act
            var info = wrapper.Info;
            
            // Assert
            info.IsDurable.Should().Be(mockFileSystemInfo.IsDurable, "durability should be preserved from underlying file system");
            info.SupportsMonitoring.Should().Be(mockFileSystemInfo.SupportsMonitoring, "monitoring support should be preserved from underlying file system");
            info.IsReadOnly.Should().BeTrue("wrapper should always be read-only regardless of underlying file system");
        }

        [Fact(DisplayName = "RFSW-012: Wrapper delegates CreateMonitoringSessionAsync to underlying filesystem")]
        public async Task RFSW012()
        {
            // Arrange
            var mockSession = new Mock<IMonitoringSession>();
            var expectedResult = Result<IMonitoringSession, FileSystemError>.Success(mockSession.Object);
            var options = MonitoringOptions.ForPath("/test".ToCanonicalPath());
            
            var mockFileSystem = new Mock<IFileSystem>();
            mockFileSystem.Setup(fs => fs.CreateMonitoringSessionAsync(options, It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedResult);

            var wrapper = mockFileSystem.Object.AsReadOnly();

            // Act
            var result = await wrapper.CreateMonitoringSessionAsync(options);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeSameAs(mockSession.Object);
            mockFileSystem.Verify(fs => fs.CreateMonitoringSessionAsync(options, It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}