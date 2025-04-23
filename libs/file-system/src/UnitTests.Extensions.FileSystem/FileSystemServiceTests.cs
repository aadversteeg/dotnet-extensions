using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Ave.Extensions.FileSystem;

namespace UnitTests.Extensions.FileSystem
{
    public class FileSystemServiceTests
    {
        [Fact(DisplayName = "FSS-001: FileExistsAsync returns true for existing file")]
        public async Task FSS001()
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
        
        [Fact(DisplayName = "FSS-002: FileExistsAsync returns false for non-existing file")]
        public async Task FSS002()
        {
            // Arrange
            var fileSystem = new FileSystemService();
            var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
            
            // Act
            var exists = await fileSystem.FileExistsAsync(nonExistentPath);
            
            // Assert
            exists.Should().BeFalse();
        }
        
        [Fact(DisplayName = "FSS-003: DirectoryExistsAsync returns true for existing directory")]
        public async Task FSS003()
        {
            // Arrange
            var fileSystem = new FileSystemService();
            var tempDir = Path.Combine(Path.GetTempPath(), "FileSystemTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            
            try
            {
                // Act
                var exists = await fileSystem.DirectoryExistsAsync(tempDir);
                
                // Assert
                exists.Should().BeTrue();
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir);
                }
            }
        }
        
        [Fact(DisplayName = "FSS-004: ReadWriteAllText performs round trip successfully")]
        public async Task FSS004()
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
        
        [Fact(DisplayName = "FSS-005: CreateDirectory creates the directory successfully")]
        public async Task FSS005()
        {
            // Arrange
            var fileSystem = new FileSystemService();
            var tempDir = Path.Combine(Path.GetTempPath(), "FSTest_" + Guid.NewGuid().ToString("N"));
            
            try
            {
                // Act
                await fileSystem.CreateDirectoryAsync(tempDir);
                var exists = Directory.Exists(tempDir);
                
                // Assert
                exists.Should().BeTrue();
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir);
                }
            }
        }
        
        [Fact(DisplayName = "FSS-006: GetFilesAsync returns correct file list")]
        public async Task FSS006()
        {
            // Arrange
            var fileSystem = new FileSystemService();
            var tempDir = Path.Combine(Path.GetTempPath(), "FSTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            
            var file1 = Path.Combine(tempDir, "file1.txt");
            var file2 = Path.Combine(tempDir, "file2.txt");
            
            try
            {
                File.WriteAllText(file1, "Content 1");
                File.WriteAllText(file2, "Content 2");
                
                // Act
                var files = await fileSystem.GetFilesAsync(tempDir);
                
                // Assert
                files.Should().HaveCount(2);
                files.Should().Contain(file1);
                files.Should().Contain(file2);
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }
    }
}