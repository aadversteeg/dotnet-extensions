using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Abstractions;
using Ave.Extensions.FileSystem.InMemory;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.Functional;
using FluentAssertions;
using Xunit;

namespace UnitTests.Extensions.FileSystem.InMemory
{
    public class InMemoryFileSystemMoveCopyTests
    {
        private readonly InMemoryFileSystem _fileSystem;
        
        public InMemoryFileSystemMoveCopyTests()
        {
            _fileSystem = new InMemoryFileSystem();
        }
        
        #region MoveFileAsync Tests
        
        [Fact(DisplayName = "IMFS-001: MoveFileAsync should move file when destination does not exist")]
        public async Task IMFS001()
        {
            // Arrange
            var source = CanonicalPath.Create("/source.txt").Value;
            var dest = CanonicalPath.Create("/dest.txt").Value;
            const string content = "test content";
            
            await _fileSystem.WriteAllTextAsync(source, content);
            
            // Act
            var result = await _fileSystem.MoveFileAsync(source, dest);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            
            var sourceExists = await _fileSystem.FileExistsAsync(source);
            sourceExists.Value.Should().BeFalse();
            
            var destExists = await _fileSystem.FileExistsAsync(dest);
            destExists.Value.Should().BeTrue();
            
            var destContent = await _fileSystem.ReadAllTextAsync(dest);
            destContent.Value.Should().Be(content);
        }
        
        [Fact(DisplayName = "IMFS-002: MoveFileAsync should fail when source does not exist")]
        public async Task IMFS002()
        {
            // Arrange
            var source = CanonicalPath.Create("/nonexistent.txt").Value;
            var dest = CanonicalPath.Create("/dest.txt").Value;
            
            // Act
            var result = await _fileSystem.MoveFileAsync(source, dest);
            
            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Type.Should().Be(FileSystemErrorType.FileNotFound);
        }
        
        [Fact(DisplayName = "IMFS-003: MoveFileAsync should fail when destination exists and overwrite is false")]
        public async Task IMFS003()
        {
            // Arrange
            var source = CanonicalPath.Create("/source.txt").Value;
            var dest = CanonicalPath.Create("/dest.txt").Value;
            
            await _fileSystem.WriteAllTextAsync(source, "source content");
            await _fileSystem.WriteAllTextAsync(dest, "dest content");
            
            // Act
            var result = await _fileSystem.MoveFileAsync(source, dest, overwrite: false);
            
            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Type.Should().Be(FileSystemErrorType.FileAlreadyExists);
            
            // Verify files unchanged
            var sourceContent = await _fileSystem.ReadAllTextAsync(source);
            sourceContent.Value.Should().Be("source content");
            
            var destContent = await _fileSystem.ReadAllTextAsync(dest);
            destContent.Value.Should().Be("dest content");
        }
        
        [Fact(DisplayName = "IMFS-004: MoveFileAsync should overwrite when destination exists and overwrite is true")]
        public async Task IMFS004()
        {
            // Arrange
            var source = CanonicalPath.Create("/source.txt").Value;
            var dest = CanonicalPath.Create("/dest.txt").Value;
            
            await _fileSystem.WriteAllTextAsync(source, "source content");
            await _fileSystem.WriteAllTextAsync(dest, "dest content");
            
            // Act
            var result = await _fileSystem.MoveFileAsync(source, dest, overwrite: true);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            
            var sourceExists = await _fileSystem.FileExistsAsync(source);
            sourceExists.Value.Should().BeFalse();
            
            var destContent = await _fileSystem.ReadAllTextAsync(dest);
            destContent.Value.Should().Be("source content");
        }
        
        [Fact(DisplayName = "IMFS-005: MoveFileAsync should create parent directory")]
        public async Task IMFS005()
        {
            // Arrange
            var source = CanonicalPath.Create("/source.txt").Value;
            var dest = CanonicalPath.Create("/subdir/dest.txt").Value;
            
            await _fileSystem.WriteAllTextAsync(source, "content");
            
            // Act
            var result = await _fileSystem.MoveFileAsync(source, dest);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            
            var dirExists = await _fileSystem.DirectoryExistsAsync(CanonicalPath.Create("/subdir").Value);
            dirExists.Value.Should().BeTrue();
        }
        
        #endregion
        
        #region CopyFileAsync Tests
        
        [Fact(DisplayName = "IMFS-006: CopyFileAsync should copy file when destination does not exist")]
        public async Task IMFS006()
        {
            // Arrange
            var source = CanonicalPath.Create("/source.txt").Value;
            var dest = CanonicalPath.Create("/dest.txt").Value;
            const string content = "test content";
            
            await _fileSystem.WriteAllTextAsync(source, content);
            
            // Act
            var result = await _fileSystem.CopyFileAsync(source, dest);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            
            var sourceExists = await _fileSystem.FileExistsAsync(source);
            sourceExists.Value.Should().BeTrue(); // Source should still exist
            
            var destExists = await _fileSystem.FileExistsAsync(dest);
            destExists.Value.Should().BeTrue();
            
            var sourceContent = await _fileSystem.ReadAllTextAsync(source);
            var destContent = await _fileSystem.ReadAllTextAsync(dest);
            sourceContent.Value.Should().Be(content);
            destContent.Value.Should().Be(content);
        }
        
        [Fact(DisplayName = "IMFS-007: CopyFileAsync should deep copy and not share references")]
        public async Task IMFS007()
        {
            // Arrange
            var source = CanonicalPath.Create("/source.txt").Value;
            var dest = CanonicalPath.Create("/dest.txt").Value;
            const string originalContent = "original content";
            
            await _fileSystem.WriteAllTextAsync(source, originalContent);
            
            // Act
            var copyResult = await _fileSystem.CopyFileAsync(source, dest);
            copyResult.IsSuccess.Should().BeTrue();
            
            // Modify the destination
            await _fileSystem.WriteAllTextAsync(dest, "modified content");
            
            // Assert - source should be unchanged
            var sourceContent = await _fileSystem.ReadAllTextAsync(source);
            sourceContent.Value.Should().Be(originalContent);
            
            var destContent = await _fileSystem.ReadAllTextAsync(dest);
            destContent.Value.Should().Be("modified content");
        }
        
        #endregion
        
        #region MoveDirectoryAsync Tests
        
        [Fact(DisplayName = "IMFS-008: MoveDirectoryAsync should move entire directory")]
        public async Task IMFS008()
        {
            // Arrange
            var sourceDir = CanonicalPath.Create("/source-dir").Value;
            var destDir = CanonicalPath.Create("/dest-dir").Value;
            
            await _fileSystem.CreateDirectoryAsync(sourceDir);
            await _fileSystem.WriteAllTextAsync(CanonicalPath.Create("/source-dir/file1.txt").Value, "content1");
            await _fileSystem.WriteAllTextAsync(CanonicalPath.Create("/source-dir/file2.txt").Value, "content2");
            await _fileSystem.CreateDirectoryAsync(CanonicalPath.Create("/source-dir/subdir").Value);
            await _fileSystem.WriteAllTextAsync(CanonicalPath.Create("/source-dir/subdir/file3.txt").Value, "content3");
            
            // Act
            var result = await _fileSystem.MoveDirectoryAsync(sourceDir, destDir);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            
            // Source should not exist
            var sourceExists = await _fileSystem.DirectoryExistsAsync(sourceDir);
            sourceExists.Value.Should().BeFalse();
            
            // Destination should exist with all contents
            var destExists = await _fileSystem.DirectoryExistsAsync(destDir);
            destExists.Value.Should().BeTrue();
            
            var file1 = await _fileSystem.ReadAllTextAsync(CanonicalPath.Create("/dest-dir/file1.txt").Value);
            file1.Value.Should().Be("content1");
            
            var file3 = await _fileSystem.ReadAllTextAsync(CanonicalPath.Create("/dest-dir/subdir/file3.txt").Value);
            file3.Value.Should().Be("content3");
        }
        
        [Fact(DisplayName = "IMFS-009: MoveDirectoryAsync should fail when destination exists")]
        public async Task IMFS009()
        {
            // Arrange
            var sourceDir = CanonicalPath.Create("/source-dir").Value;
            var destDir = CanonicalPath.Create("/dest-dir").Value;
            
            await _fileSystem.CreateDirectoryAsync(sourceDir);
            await _fileSystem.CreateDirectoryAsync(destDir);
            
            // Act
            var result = await _fileSystem.MoveDirectoryAsync(sourceDir, destDir);
            
            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Type.Should().Be(FileSystemErrorType.DirectoryAlreadyExists);
        }
        
        [Fact(DisplayName = "IMFS-010: MoveDirectoryAsync should fail when moving to subdirectory")]
        public async Task IMFS010()
        {
            // Arrange
            var sourceDir = CanonicalPath.Create("/parent").Value;
            var destDir = CanonicalPath.Create("/parent/child").Value;
            
            await _fileSystem.CreateDirectoryAsync(sourceDir);
            
            // Act
            var result = await _fileSystem.MoveDirectoryAsync(sourceDir, destDir);
            
            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Type.Should().Be(FileSystemErrorType.InvalidPath);
        }
        
        #endregion
        
        #region CopyDirectoryAsync Tests
        
        [Fact(DisplayName = "IMFS-011: CopyDirectoryAsync should copy entire directory with deep copy")]
        public async Task IMFS011()
        {
            // Arrange
            var sourceDir = CanonicalPath.Create("/source-dir").Value;
            var destDir = CanonicalPath.Create("/dest-dir").Value;
            
            await _fileSystem.CreateDirectoryAsync(sourceDir);
            await _fileSystem.WriteAllTextAsync(CanonicalPath.Create("/source-dir/file1.txt").Value, "content1");
            await _fileSystem.CreateDirectoryAsync(CanonicalPath.Create("/source-dir/subdir").Value);
            await _fileSystem.WriteAllTextAsync(CanonicalPath.Create("/source-dir/subdir/file2.txt").Value, "content2");
            
            // Act
            var result = await _fileSystem.CopyDirectoryAsync(sourceDir, destDir);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            
            // Both should exist
            var sourceExists = await _fileSystem.DirectoryExistsAsync(sourceDir);
            sourceExists.Value.Should().BeTrue();
            
            var destExists = await _fileSystem.DirectoryExistsAsync(destDir);
            destExists.Value.Should().BeTrue();
            
            // Verify deep copy by modifying destination
            await _fileSystem.WriteAllTextAsync(CanonicalPath.Create("/dest-dir/file1.txt").Value, "modified");
            
            var sourceFile = await _fileSystem.ReadAllTextAsync(CanonicalPath.Create("/source-dir/file1.txt").Value);
            sourceFile.Value.Should().Be("content1"); // Source unchanged
            
            var destFile = await _fileSystem.ReadAllTextAsync(CanonicalPath.Create("/dest-dir/file1.txt").Value);
            destFile.Value.Should().Be("modified");
        }
        
        #endregion
        
        #region Generic Operations Tests
        
        [Fact(DisplayName = "IMFS-012: MoveAsync should auto-detect file")]
        public async Task IMFS012()
        {
            // Arrange
            var source = CanonicalPath.Create("/file.txt").Value;
            var dest = CanonicalPath.Create("/moved.txt").Value;
            
            await _fileSystem.WriteAllTextAsync(source, "content");
            
            // Act
            var result = await _fileSystem.MoveAsync(source, dest);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            
            var sourceExists = await _fileSystem.FileExistsAsync(source);
            sourceExists.Value.Should().BeFalse();
            
            var destContent = await _fileSystem.ReadAllTextAsync(dest);
            destContent.Value.Should().Be("content");
        }
        
        [Fact(DisplayName = "IMFS-013: MoveAsync should auto-detect directory")]
        public async Task IMFS013()
        {
            // Arrange
            var source = CanonicalPath.Create("/dir").Value;
            var dest = CanonicalPath.Create("/moved-dir").Value;
            
            await _fileSystem.CreateDirectoryAsync(source);
            await _fileSystem.WriteAllTextAsync(CanonicalPath.Create("/dir/file.txt").Value, "content");
            
            // Act
            var result = await _fileSystem.MoveAsync(source, dest);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            
            var sourceExists = await _fileSystem.DirectoryExistsAsync(source);
            sourceExists.Value.Should().BeFalse();
            
            var fileContent = await _fileSystem.ReadAllTextAsync(CanonicalPath.Create("/moved-dir/file.txt").Value);
            fileContent.Value.Should().Be("content");
        }
        
        [Fact(DisplayName = "IMFS-014: CopyAsync should auto-detect and copy")]
        public async Task IMFS014()
        {
            // Arrange
            var sourceFile = CanonicalPath.Create("/file.txt").Value;
            var destFile = CanonicalPath.Create("/copied.txt").Value;
            var sourceDir = CanonicalPath.Create("/dir").Value;
            var destDir = CanonicalPath.Create("/copied-dir").Value;
            
            await _fileSystem.WriteAllTextAsync(sourceFile, "file content");
            await _fileSystem.CreateDirectoryAsync(sourceDir);
            
            // Act - Copy file
            var fileResult = await _fileSystem.CopyAsync(sourceFile, destFile);
            
            // Act - Copy directory
            var dirResult = await _fileSystem.CopyAsync(sourceDir, destDir);
            
            // Assert
            fileResult.IsSuccess.Should().BeTrue();
            dirResult.IsSuccess.Should().BeTrue();
            
            var copiedFileContent = await _fileSystem.ReadAllTextAsync(destFile);
            copiedFileContent.Value.Should().Be("file content");
            
            var copiedDirExists = await _fileSystem.DirectoryExistsAsync(destDir);
            copiedDirExists.Value.Should().BeTrue();
        }
        
        #endregion
        
        #region Thread Safety Tests
        
        [Fact(DisplayName = "IMFS-015: Move and copy should be thread-safe")]
        public async Task IMFS015()
        {
            // Arrange
            var tasks = new List<Task>();
            var basePath = CanonicalPath.Create("/concurrent").Value;
            await _fileSystem.CreateDirectoryAsync(basePath);
            
            // Create initial files
            for (int i = 0; i < 10; i++)
            {
                var path = CanonicalPath.Create($"/concurrent/file{i}.txt").Value;
                await _fileSystem.WriteAllTextAsync(path, $"content{i}");
            }
            
            // Act - Perform concurrent operations
            for (int i = 0; i < 10; i++)
            {
                var index = i;
                
                // Move operations
                tasks.Add(Task.Run(async () =>
                {
                    var source = CanonicalPath.Create($"/concurrent/file{index}.txt").Value;
                    var dest = CanonicalPath.Create($"/concurrent/moved{index}.txt").Value;
                    await _fileSystem.MoveFileAsync(source, dest);
                }));
                
                // Copy operations
                tasks.Add(Task.Run(async () =>
                {
                    await Task.Delay(50); // Small delay to interleave operations
                    var source = CanonicalPath.Create($"/concurrent/moved{index}.txt").Value;
                    var dest = CanonicalPath.Create($"/concurrent/copied{index}.txt").Value;
                    
                    // Try to copy the moved file
                    var exists = await _fileSystem.FileExistsAsync(source);
                    if (exists.Value)
                    {
                        await _fileSystem.CopyFileAsync(source, dest);
                    }
                }));
            }
            
            await Task.WhenAll(tasks);
            
            // Assert - Verify final state
            for (int i = 0; i < 10; i++)
            {
                var sourcePath = CanonicalPath.Create($"/concurrent/file{i}.txt").Value;
                var movedPath = CanonicalPath.Create($"/concurrent/moved{i}.txt").Value;
                
                var sourceExists = await _fileSystem.FileExistsAsync(sourcePath);
                sourceExists.Value.Should().BeFalse(); // Original files should not exist
                
                var movedExists = await _fileSystem.FileExistsAsync(movedPath);
                movedExists.Value.Should().BeTrue(); // Moved files should exist
                
                var content = await _fileSystem.ReadAllTextAsync(movedPath);
                content.Value.Should().Be($"content{i}");
            }
        }
        
        #endregion
    }
}