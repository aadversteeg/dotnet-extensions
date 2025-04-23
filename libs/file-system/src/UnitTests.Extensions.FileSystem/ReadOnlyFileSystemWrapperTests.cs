using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
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
            var mockFileSystem = new Mock<IFileSystem>();
            mockFileSystem.Setup(fs => fs.FileExistsAsync("test.txt", It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            
            var wrapper = mockFileSystem.Object.AsReadOnly();
            
            // Act
            var result = await wrapper.FileExistsAsync("test.txt");
            
            // Assert
            result.Should().BeTrue();
            mockFileSystem.Verify(fs => fs.FileExistsAsync("test.txt", It.IsAny<CancellationToken>()), Times.Once);
        }
        
        [Fact(DisplayName = "RFSW-002: Wrapper delegates DirectoryExistsAsync to underlying filesystem")]
        public async Task RFSW002()
        {
            // Arrange
            var mockFileSystem = new Mock<IFileSystem>();
            mockFileSystem.Setup(fs => fs.DirectoryExistsAsync("/test", It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            
            var wrapper = mockFileSystem.Object.AsReadOnly();
            
            // Act
            var result = await wrapper.DirectoryExistsAsync("/test");
            
            // Assert
            result.Should().BeTrue();
            mockFileSystem.Verify(fs => fs.DirectoryExistsAsync("/test", It.IsAny<CancellationToken>()), Times.Once);
        }
        
        [Fact(DisplayName = "RFSW-003: Wrapper delegates ReadAllTextAsync to underlying filesystem")]
        public async Task RFSW003()
        {
            // Arrange
            const string expectedContent = "file content";
            var mockFileSystem = new Mock<IFileSystem>();
            mockFileSystem.Setup(fs => fs.ReadAllTextAsync("test.txt", It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedContent);
            
            var wrapper = mockFileSystem.Object.AsReadOnly();
            
            // Act
            var result = await wrapper.ReadAllTextAsync("test.txt");
            
            // Assert
            result.Should().Be(expectedContent);
            mockFileSystem.Verify(fs => fs.ReadAllTextAsync("test.txt", It.IsAny<CancellationToken>()), Times.Once);
        }
        
        [Fact(DisplayName = "RFSW-004: Wrapper delegates GetFilesAsync to underlying filesystem")]
        public async Task RFSW004()
        {
            // Arrange
            var files = new[] { "/test/file1.txt", "/test/file2.txt" };
            var mockFileSystem = new Mock<IFileSystem>();
            mockFileSystem.Setup(fs => fs.GetFilesAsync("/test", "*.txt", It.IsAny<CancellationToken>()))
                .ReturnsAsync(files.AsEnumerable());
            
            var wrapper = mockFileSystem.Object.AsReadOnly();
            
            // Act
            var result = await wrapper.GetFilesAsync("/test", "*.txt");
            
            // Assert
            result.Should().BeEquivalentTo(files);
            mockFileSystem.Verify(fs => fs.GetFilesAsync("/test", "*.txt", It.IsAny<CancellationToken>()), Times.Once);
        }
        
        [Fact(DisplayName = "RFSW-005: Wrapper delegates GetDirectoriesAsync to underlying filesystem")]
        public async Task RFSW005()
        {
            // Arrange
            var directories = new[] { "/test/dir1", "/test/dir2" };
            var mockFileSystem = new Mock<IFileSystem>();
            mockFileSystem.Setup(fs => fs.GetDirectoriesAsync("/test", It.IsAny<CancellationToken>()))
                .ReturnsAsync(directories.AsEnumerable());
            
            var wrapper = mockFileSystem.Object.AsReadOnly();
            
            // Act
            var result = await wrapper.GetDirectoriesAsync("/test");
            
            // Assert
            result.Should().BeEquivalentTo(directories);
            mockFileSystem.Verify(fs => fs.GetDirectoriesAsync("/test", It.IsAny<CancellationToken>()), Times.Once);
        }
        
        [Fact(DisplayName = "RFSW-006: Wrapper protects against write operations")]
        public void RFSW006()
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
                "DeleteDirectoryAsync"
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
    }
}