using System;
using System.IO;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using FluentAssertions;
using Xunit;

namespace UnitTests.Extensions.FileSystem.FileSystemTests
{
    public class FileSystemTests
    {
        [Fact]
        public async Task FileExists_WithExistingFile_ReturnsTrue()
        {
            // Arrange
            var fileSystem = new FileSystemService();
            var tempFilePath = Path.GetTempFileName();
            
            try
            {
                // Act
                var exists = await fileSystem.FileExistsAsync(tempFilePath);
                
                // Assert
                exists.Should().BeTrue();
            }
            finally
            {
                // Cleanup
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
        }
        
        [Fact]
        public async Task FileExists_WithNonExistingFile_ReturnsFalse()
        {
            // Arrange
            var fileSystem = new FileSystemService();
            var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
            
            // Act
            var exists = await fileSystem.FileExistsAsync(nonExistentPath);
            
            // Assert
            exists.Should().BeFalse();
        }
        
        [Fact]
        public async Task ReadWriteAllText_RoundTrip_Succeeds()
        {
            // Arrange
            var fileSystem = new FileSystemService();
            var tempFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
            var expectedContent = "Hello World!";
            
            try
            {
                // Act
                await fileSystem.WriteAllTextAsync(tempFilePath, expectedContent);
                var actualContent = await fileSystem.ReadAllTextAsync(tempFilePath);
                
                // Assert
                actualContent.Should().Be(expectedContent);
            }
            finally
            {
                // Cleanup
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
        }
    }
}
