using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Extensions;
using Ave.Extensions.FileSystem.InMemory;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Progress;
using Ave.Extensions.Functional;
using FluentAssertions;
using Xunit;

namespace UnitTests.Extensions.FileSystem
{
    /// <summary>
    /// Synchronous progress reporter for unit tests.
    /// </summary>
    public class SynchronousProgress<T> : IProgress<T>
    {
        private readonly Action<T> _callback;
        
        public SynchronousProgress(Action<T> callback)
        {
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
        }
        
        public void Report(T value)
        {
            _callback(value);
        }
    }

    /// <summary>
    /// Unit tests for file system extension methods.
    /// </summary>
    public class ExtensionMethodTests
    {
        private readonly InMemoryFileSystem _fileSystem;
        
        public ExtensionMethodTests()
        {
            _fileSystem = new InMemoryFileSystem();
        }
        
        [Fact(DisplayName = "EXT-001: MoveAsync with options should respect overwrite")]
        public async Task EXT001()
        {
            // Arrange
            var source = CanonicalPath.Create("/source.txt").Value;
            var dest = CanonicalPath.Create("/dest.txt").Value;
            
            await _fileSystem.WriteAllTextAsync(source, "source content");
            await _fileSystem.WriteAllTextAsync(dest, "dest content");
            
            var options = new MoveOptions { Overwrite = true };
            
            // Act
            var result = await FileSystemMoveExtensions.MoveAsync(_fileSystem, source, dest, options);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            var content = await _fileSystem.ReadAllTextAsync(dest);
            content.Value.Should().Be("source content");
        }
        
        [Fact(DisplayName = "EXT-002: CopyAsync with progress should report progress")]
        public async Task EXT002()
        {
            // Arrange
            var source = CanonicalPath.Create("/source.txt").Value;
            var dest = CanonicalPath.Create("/dest.txt").Value;
            
            await _fileSystem.WriteAllTextAsync(source, "content");
            
            var progressReports = new List<ICopyProgress>();
            
            // Use synchronous callback instead of Progress<T> to avoid thread context issues
            IProgress<ICopyProgress> progress = new SynchronousProgress<ICopyProgress>(p => progressReports.Add(p));
            
            var options = new CopyOptions { Progress = progress };
            
            // Act
            var result = await FileSystemCopyExtensions.CopyAsync(_fileSystem, source, dest, options);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            progressReports.Should().NotBeEmpty();
            
            // Should have at least start and end progress
            progressReports.Should().HaveCountGreaterOrEqualTo(2);
            
            var lastProgress = progressReports.Last();
            lastProgress.FilesProcessed.Should().Be(1);
            lastProgress.TotalFiles.Should().Be(1);
        }
        
        [Fact(DisplayName = "EXT-003: CopyAsync directory with progress should report multiple files")]
        public async Task EXT003()
        {
            // Arrange
            var sourceDir = CanonicalPath.Create("/source").Value;
            var destDir = CanonicalPath.Create("/dest").Value;
            
            await _fileSystem.CreateDirectoryAsync(sourceDir);
            await _fileSystem.WriteAllTextAsync(CanonicalPath.Create("/source/file1.txt").Value, "content1");
            await _fileSystem.WriteAllTextAsync(CanonicalPath.Create("/source/file2.txt").Value, "content2");
            await _fileSystem.CreateDirectoryAsync(CanonicalPath.Create("/source/subdir").Value);
            await _fileSystem.WriteAllTextAsync(CanonicalPath.Create("/source/subdir/file3.txt").Value, "content3");
            
            var progressReports = new List<ICopyProgress>();
            IProgress<ICopyProgress> progress = new SynchronousProgress<ICopyProgress>(p => progressReports.Add(p));
            
            var options = new CopyOptions { Progress = progress };
            
            // Act
            var result = await FileSystemCopyExtensions.CopyAsync(_fileSystem, sourceDir, destDir, options);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            progressReports.Should().NotBeEmpty();
            
            var lastProgress = progressReports.Last();
            lastProgress.TotalFiles.Should().Be(3); // 3 files total
            lastProgress.FilesProcessed.Should().Be(3);
        }
        
        [Fact(DisplayName = "EXT-004: MoveFileWithProgressAsync should work")]
        public async Task EXT004()
        {
            // Arrange
            var source = CanonicalPath.Create("/source.txt").Value;
            var dest = CanonicalPath.Create("/dest.txt").Value;
            
            await _fileSystem.WriteAllTextAsync(source, "content");
            
            var progressReported = false;
            IProgress<ICopyProgress> progress = new SynchronousProgress<ICopyProgress>(p => progressReported = true);
            
            // Act
            var result = await FileSystemMoveExtensions.MoveFileWithProgressAsync(
                _fileSystem,
                source, 
                dest, 
                overwrite: false, 
                progress: progress);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            progressReported.Should().BeTrue();
            
            var sourceExists = await _fileSystem.FileExistsAsync(source);
            sourceExists.Value.Should().BeFalse();
            
            var destContent = await _fileSystem.ReadAllTextAsync(dest);
            destContent.Value.Should().Be("content");
        }
        
        [Fact(DisplayName = "EXT-005: CopyProgress should calculate percentage correctly")]
        public void EXT005()
        {
            // Arrange & Act
            var progress = new CopyProgress(
                bytesTransferred: 50,
                totalBytes: 100,
                currentFile: "/test.txt",
                filesProcessed: 1,
                totalFiles: 2);
            
            // Assert
            progress.PercentComplete.Should().Be(50.0);
            progress.BytesTransferred.Should().Be(50);
            progress.TotalBytes.Should().Be(100);
            progress.CurrentFile.Should().Be("/test.txt");
            progress.FilesProcessed.Should().Be(1);
            progress.TotalFiles.Should().Be(2);
        }
        
        [Fact(DisplayName = "EXT-006: CopyProgress WithUpdate should create new instance")]
        public void EXT006()
        {
            // Arrange
            var original = new CopyProgress(10, 100, "/file1.txt", 1, 5);
            
            // Act
            var updated = original.WithUpdate(bytesTransferred: 20, currentFile: "/file2.txt");
            
            // Assert
            updated.Should().NotBeSameAs(original);
            updated.BytesTransferred.Should().Be(20);
            updated.CurrentFile.Should().Be("/file2.txt");
            updated.TotalBytes.Should().Be(100); // Unchanged
            updated.FilesProcessed.Should().Be(1); // Unchanged
            updated.TotalFiles.Should().Be(5); // Unchanged
        }
        
        [Fact(DisplayName = "EXT-007: CopyAsync without progress should use fast path")]
        public async Task EXT007()
        {
            // Arrange
            var source = CanonicalPath.Create("/source.txt").Value;
            var dest = CanonicalPath.Create("/dest.txt").Value;
            
            await _fileSystem.WriteAllTextAsync(source, "content");
            
            var options = new CopyOptions { Progress = null };
            
            // Act
            var result = await FileSystemCopyExtensions.CopyAsync(_fileSystem, source, dest, options);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            var destContent = await _fileSystem.ReadAllTextAsync(dest);
            destContent.Value.Should().Be("content");
        }
        
        [Fact(DisplayName = "EXT-008: MoveAsync should handle default options")]
        public async Task EXT008()
        {
            // Arrange
            var source = CanonicalPath.Create("/source.txt").Value;
            var dest = CanonicalPath.Create("/dest.txt").Value;
            
            await _fileSystem.WriteAllTextAsync(source, "content");
            
            // Act - passing null options should use defaults
            var result = await FileSystemMoveExtensions.MoveAsync(_fileSystem, source, dest, options: null);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            var sourceExists = await _fileSystem.FileExistsAsync(source);
            sourceExists.Value.Should().BeFalse();
            var destContent = await _fileSystem.ReadAllTextAsync(dest);
            destContent.Value.Should().Be("content");
        }
        
        [Fact(DisplayName = "EXT-009: CopyAsync should handle non-existent source")]
        public async Task EXT009()
        {
            // Arrange
            var source = CanonicalPath.Create("/nonexistent.txt").Value;
            var dest = CanonicalPath.Create("/dest.txt").Value;
            
            var options = new CopyOptions();
            
            // Act
            var result = await FileSystemCopyExtensions.CopyAsync(_fileSystem, source, dest, options);
            
            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Type.Should().Be(FileSystemErrorType.PathNotFound);
        }
        
        [Fact(DisplayName = "EXT-010: Extension methods should handle null fileSystem")]
        public async Task EXT010()
        {
            // Arrange
            IFileSystem? nullFileSystem = null;
            var source = CanonicalPath.Create("/source.txt").Value;
            var dest = CanonicalPath.Create("/dest.txt").Value;
            
            // Act & Assert
            var copyException = await Assert.ThrowsAsync<ArgumentNullException>(() => 
                FileSystemCopyExtensions.CopyAsync(nullFileSystem!, source, dest));
            copyException.ParamName.Should().Be("fileSystem");
            
            var moveException = await Assert.ThrowsAsync<ArgumentNullException>(() => 
                FileSystemMoveExtensions.MoveAsync(nullFileSystem!, source, dest));
            moveException.ParamName.Should().Be("fileSystem");
            
            var moveWithProgressException = await Assert.ThrowsAsync<ArgumentNullException>(() => 
                FileSystemMoveExtensions.MoveFileWithProgressAsync(nullFileSystem!, source, dest));
            moveWithProgressException.ParamName.Should().Be("fileSystem");
        }
    }
}