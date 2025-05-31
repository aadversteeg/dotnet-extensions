using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.Functional;
using FluentAssertions;
using Moq;

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
            mockFileSystem.Setup(fs => fs.FileExistsAsync("test.txt", It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedResult);
            
            var wrapper = mockFileSystem.Object.AsReadOnly();
            
            // Act
            var result = await wrapper.FileExistsAsync("test.txt");
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeTrue();
            mockFileSystem.Verify(fs => fs.FileExistsAsync("test.txt", It.IsAny<CancellationToken>()), Times.Once);
        }
        
        [Fact(DisplayName = "RFSW-002: Wrapper delegates DirectoryExistsAsync to underlying filesystem")]
        public async Task RFSW002()
        {
            // Arrange
            var expectedResult = Result<bool, FileSystemError>.Success(true);
            var mockFileSystem = new Mock<IFileSystem>();
            mockFileSystem.Setup(fs => fs.DirectoryExistsAsync("/test", It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedResult);
            
            var wrapper = mockFileSystem.Object.AsReadOnly();
            
            // Act
            var result = await wrapper.DirectoryExistsAsync("/test");
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeTrue();
            mockFileSystem.Verify(fs => fs.DirectoryExistsAsync("/test", It.IsAny<CancellationToken>()), Times.Once);
        }
        
        [Fact(DisplayName = "RFSW-003: Wrapper delegates ReadAllTextAsync to underlying filesystem")]
        public async Task RFSW003()
        {
            // Arrange
            const string expectedContent = "file content";
            var expectedResult = Result<string, FileSystemError>.Success(expectedContent);
            var mockFileSystem = new Mock<IFileSystem>();
            mockFileSystem.Setup(fs => fs.ReadAllTextAsync("test.txt", It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedResult);
            
            var wrapper = mockFileSystem.Object.AsReadOnly();
            
            // Act
            var result = await wrapper.ReadAllTextAsync("test.txt");
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().Be(expectedContent);
            mockFileSystem.Verify(fs => fs.ReadAllTextAsync("test.txt", It.IsAny<CancellationToken>()), Times.Once);
        }
        
        [Fact(DisplayName = "RFSW-004: Wrapper delegates GetFilesAsync to underlying filesystem")]
        public async Task RFSW004()
        {
            // Arrange
            var files = new[] { "/test/file1.txt", "/test/file2.txt" };
            var expectedResult = Result<string[], FileSystemError>.Success(files);
            var mockFileSystem = new Mock<IFileSystem>();
            mockFileSystem.Setup(fs => fs.GetFilesAsync("/test", "*.txt", SearchOption.TopDirectoryOnly, It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedResult);
            
            var wrapper = mockFileSystem.Object.AsReadOnly();
            
            // Act
            var result = await wrapper.GetFilesAsync("/test", "*.txt", SearchOption.TopDirectoryOnly);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeEquivalentTo(files);
            mockFileSystem.Verify(fs => fs.GetFilesAsync("/test", "*.txt", SearchOption.TopDirectoryOnly, It.IsAny<CancellationToken>()), Times.Once);
        }
        
        [Fact(DisplayName = "RFSW-005: Wrapper delegates GetDirectoriesAsync to underlying filesystem")]
        public async Task RFSW005()
        {
            // Arrange
            var directories = new[] { "/test/dir1", "/test/dir2" };
            var expectedResult = Result<string[], FileSystemError>.Success(directories);
            var mockFileSystem = new Mock<IFileSystem>();
            mockFileSystem.Setup(fs => fs.GetDirectoriesAsync("/test", "*", SearchOption.TopDirectoryOnly, It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedResult);
            
            var wrapper = mockFileSystem.Object.AsReadOnly();
            
            // Act
            var result = await wrapper.GetDirectoriesAsync("/test", "*", SearchOption.TopDirectoryOnly);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeEquivalentTo(directories);
            mockFileSystem.Verify(fs => fs.GetDirectoriesAsync("/test", "*", SearchOption.TopDirectoryOnly, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact(DisplayName = "RFSW-006: Wrapper delegates GetFileMetadataAsync to underlying filesystem")]
        public async Task RFSW006()
        {
            // Arrange
            var metadata = new FileMetadata
            {
                FullPath = "/test/file.txt",
                Name = "file.txt",
                Extension = ".txt",
                Size = 1024
            };
            var expectedResult = Result<FileMetadata, FileSystemError>.Success(metadata);
            var mockFileSystem = new Mock<IFileSystem>();
            mockFileSystem.Setup(fs => fs.GetFileMetadataAsync("/test/file.txt", It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedResult);
            
            var wrapper = mockFileSystem.Object.AsReadOnly();
            
            // Act
            var result = await wrapper.GetFileMetadataAsync("/test/file.txt");
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeEquivalentTo(metadata);
            mockFileSystem.Verify(fs => fs.GetFileMetadataAsync("/test/file.txt", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact(DisplayName = "RFSW-007: Wrapper delegates GetDirectoryMetadataAsync to underlying filesystem")]
        public async Task RFSW007()
        {
            // Arrange
            var metadata = new DirectoryMetadata
            {
                FullPath = "/test/dir",
                Name = "dir"
            };
            var expectedResult = Result<DirectoryMetadata, FileSystemError>.Success(metadata);
            var mockFileSystem = new Mock<IFileSystem>();
            mockFileSystem.Setup(fs => fs.GetDirectoryMetadataAsync("/test/dir", It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedResult);
            
            var wrapper = mockFileSystem.Object.AsReadOnly();
            
            // Act
            var result = await wrapper.GetDirectoryMetadataAsync("/test/dir");
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeEquivalentTo(metadata);
            mockFileSystem.Verify(fs => fs.GetDirectoryMetadataAsync("/test/dir", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact(DisplayName = "RFSW-008: Wrapper delegates OpenReadAsync to underlying filesystem")]
        public async Task RFSW008()
        {
            // Arrange
            var stream = new MemoryStream();
            var expectedResult = Result<Stream, FileSystemError>.Success(stream);
            var mockFileSystem = new Mock<IFileSystem>();
            mockFileSystem.Setup(fs => fs.OpenReadAsync("/test/file.txt", It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedResult);
            
            var wrapper = mockFileSystem.Object.AsReadOnly();
            
            // Act
            var result = await wrapper.OpenReadAsync("/test/file.txt");
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeSameAs(stream);
            mockFileSystem.Verify(fs => fs.OpenReadAsync("/test/file.txt", It.IsAny<CancellationToken>()), Times.Once);
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

        [Fact(DisplayName = "RFSW-010: Wrapper delegates CreateMonitorAsync to underlying filesystem")]
        public async Task RFSW010()
        {
            // Arrange
            var mockMonitor = new Mock<IFileSystemMonitor>();
            var mockFileSystem = new Mock<IFileSystem>();
            mockFileSystem.Setup(fs => fs.CreateMonitorAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockMonitor.Object);
            
            var wrapper = mockFileSystem.Object.AsReadOnly();
            
            // Act
            var result = await wrapper.CreateMonitorAsync();
            
            // Assert
            result.Should().BeSameAs(mockMonitor.Object);
            mockFileSystem.Verify(fs => fs.CreateMonitorAsync(It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}