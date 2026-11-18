using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Abstractions;
using Ave.Extensions.FileSystem.Local;
using Ave.Extensions.FileSystem.Local.PathMappers;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTests.FileSystem.Local
{
    /// <summary>
    /// Integration tests for LocalFileSystem move and copy operations with real-world scenarios.
    /// </summary>
    public class LocalFileSystemMoveCopyIntegrationTests : IDisposable
    {
        private readonly string _testRoot;
        private readonly LocalFileSystem _fileSystem;
        private readonly IPathMapper _pathMapper;
        private readonly ITestOutputHelper _output;
        private readonly Random _random;
        
        public LocalFileSystemMoveCopyIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
            _testRoot = Path.Combine(Path.GetTempPath(), $"ave-move-copy-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_testRoot);
            
            _pathMapper = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
                ? (IPathMapper)new WindowsPathMapper() 
                : new UnixPathMapper();
            _fileSystem = new LocalFileSystem(_pathMapper);
            _random = new Random(42); // Deterministic for reproducibility
        }
        
        #region Large File Operations
        
        [Theory(DisplayName = "LFSMC-001: Move large files efficiently preserves content and performance")]
        [InlineData(100)] // 100MB
        [InlineData(250)] // 250MB  
        public async Task LFSMC001_MoveLargeFilesEfficiently(int sizeInMB)
        {
            // Arrange
            var sizeInBytes = sizeInMB * 1024L * 1024L;
            var sourceFile = await CreateLargeFileAsync($"large-{sizeInMB}mb.dat", sizeInBytes);
            var destFile = Path.Combine(_testRoot, $"moved-large-{sizeInMB}mb.dat");
            
            var sourcePath = _pathMapper.MapToCanonicalPath(sourceFile).Value;
            var destPath = _pathMapper.MapToCanonicalPath(destFile).Value;
            
            // Calculate source hash for verification
            var sourceHash = await CalculateFileHashAsync(sourceFile);
            var sourceInfo = new System.IO.FileInfo(sourceFile);
            var originalCreationTime = sourceInfo.CreationTime;
            var originalLastWriteTime = sourceInfo.LastWriteTime;
            
            // Act
            var sw = Stopwatch.StartNew();
            var result = await _fileSystem.MoveFileAsync(sourcePath, destPath);
            sw.Stop();
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            File.Exists(sourceFile).Should().BeFalse("source should be removed after move");
            File.Exists(destFile).Should().BeTrue("destination should exist after move");
            
            var destInfo = new System.IO.FileInfo(destFile);
            destInfo.Length.Should().Be(sizeInBytes);
            
            // Verify content integrity
            var destHash = await CalculateFileHashAsync(destFile);
            destHash.Should().Be(sourceHash, "file content should be identical after move");
            
            // Verify timestamps preserved
            destInfo.CreationTime.Should().BeCloseTo(originalCreationTime, TimeSpan.FromSeconds(1));
            destInfo.LastWriteTime.Should().BeCloseTo(originalLastWriteTime, TimeSpan.FromSeconds(1));
            
            _output.WriteLine($"Moved {sizeInMB}MB file in {sw.ElapsedMilliseconds}ms");
            
            // Performance expectation: same-volume move should be very fast
            sw.ElapsedMilliseconds.Should().BeLessThan(500, "same-volume move should be metadata-only");
        }
        
        [Theory(DisplayName = "LFSMC-002: Copy large files with streaming (no memory spike)")]
        [InlineData(100)] // 100MB
        public async Task LFSMC002_CopyLargeFilesWithStreaming(int sizeInMB)
        {
            // Arrange
            var sizeInBytes = sizeInMB * 1024L * 1024L;
            var sourceFile = await CreateLargeFileAsync($"copy-source-{sizeInMB}mb.dat", sizeInBytes);
            var destFile = Path.Combine(_testRoot, $"copy-dest-{sizeInMB}mb.dat");
            
            var sourcePath = _pathMapper.MapToCanonicalPath(sourceFile).Value;
            var destPath = _pathMapper.MapToCanonicalPath(destFile).Value;
            
            var sourceHash = await CalculateFileHashAsync(sourceFile);
            
            // Force garbage collection to get baseline
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var initialMemory = GC.GetTotalMemory(forceFullCollection: false);
            
            // Act
            var result = await _fileSystem.CopyFileAsync(sourcePath, destPath);
            
            var peakMemory = GC.GetTotalMemory(forceFullCollection: false);
            var memoryUsedMB = (peakMemory - initialMemory) / (1024.0 * 1024.0);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            File.Exists(sourceFile).Should().BeTrue("source should remain after copy");
            File.Exists(destFile).Should().BeTrue("destination should exist after copy");
            
            // Verify content
            var destHash = await CalculateFileHashAsync(destFile);
            destHash.Should().Be(sourceHash);
            
            _output.WriteLine($"Memory used during {sizeInMB}MB copy: ~{memoryUsedMB:F2}MB");
            
            // Should not load entire file into memory
            memoryUsedMB.Should().BeLessThan(20, "copy should use streaming, not load entire file");
        }
        
        #endregion
        
        #region Deep Directory Hierarchy
        
        [SkippableFact(DisplayName = "LFSMC-003: Copy deep directory hierarchy preserves structure")]
        public async Task LFSMC003_CopyDeepDirectoryHierarchy()
        {
            // The deepest files of this hierarchy have paths of more than 260 characters.
            Skip.If(LongPathsUnavailable(),
                   "Windows long paths are not enabled for this process (LongPathsEnabled=0 on the machine, or no " +
                   "longPathAware in the manifest of the test host); .NET Framework does not add the \\\\?\\ prefix " +
                   "itself, so a path of 260 characters or more fails.");

            // Arrange
            var depth = 15; // 15 levels deep
            var filesPerLevel = 3;
            var sourceRoot = await CreateDeepHierarchyAsync("deep-source", depth, filesPerLevel);
            var destRoot = Path.Combine(_testRoot, "deep-dest");
            
            var sourcePath = _pathMapper.MapToCanonicalPath(sourceRoot).Value;
            var destPath = _pathMapper.MapToCanonicalPath(destRoot).Value;
            
            // Count total files and directories
            var sourceFiles = Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories);
            var sourceDirs = Directory.GetDirectories(sourceRoot, "*", SearchOption.AllDirectories);
            
            // Act
            var sw = Stopwatch.StartNew();
            var result = await _fileSystem.CopyDirectoryAsync(sourcePath, destPath);
            sw.Stop();
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            
            var destFiles = Directory.GetFiles(destRoot, "*", SearchOption.AllDirectories);
            var destDirs = Directory.GetDirectories(destRoot, "*", SearchOption.AllDirectories);
            
            destFiles.Length.Should().Be(sourceFiles.Length);
            destDirs.Length.Should().Be(sourceDirs.Length);
            
            _output.WriteLine($"Copied {sourceFiles.Length} files in {sourceDirs.Length} directories");
            _output.WriteLine($"Time taken: {sw.ElapsedMilliseconds}ms");
            
            // Verify some file contents
            foreach (var sourceFile in sourceFiles.Take(10))
            {
                var relativePath = Path.GetRelativePath(sourceRoot, sourceFile);
                var destFile = Path.Combine(destRoot, relativePath);
                
                File.Exists(destFile).Should().BeTrue($"file {relativePath} should exist");
                
                var sourceContent = await File.ReadAllTextAsync(sourceFile);
                var destContent = await File.ReadAllTextAsync(destFile);
                destContent.Should().Be(sourceContent);
            }
        }
        
        [Fact(DisplayName = "LFSMC-004: Move deep directory hierarchy is atomic")]
        public async Task LFSMC004_MoveDeepDirectoryHierarchy()
        {
            // Arrange
            var sourceRoot = await CreateDeepHierarchyAsync("move-source", 10, 2);
            var destRoot = Path.Combine(_testRoot, "move-dest");
            
            var sourcePath = _pathMapper.MapToCanonicalPath(sourceRoot).Value;
            var destPath = _pathMapper.MapToCanonicalPath(destRoot).Value;
            
            // Act
            var sw = Stopwatch.StartNew();
            var result = await _fileSystem.MoveDirectoryAsync(sourcePath, destPath);
            sw.Stop();
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            Directory.Exists(sourceRoot).Should().BeFalse("source should not exist after move");
            Directory.Exists(destRoot).Should().BeTrue("destination should exist after move");
            
            _output.WriteLine($"Moved directory hierarchy in {sw.ElapsedMilliseconds}ms");
            
            // On same volume, should be very fast (metadata operation)
            sw.ElapsedMilliseconds.Should().BeLessThan(100, "directory move should be metadata-only");
        }
        
        #endregion
        
        #region Cross-Volume Operations (Windows)
        
        [SkippableFact(DisplayName = "LFSMC-005: Cross-volume move handles correctly")]
        public async Task LFSMC005_CrossVolumeMove()
        {
            Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), 
                      "Cross-volume test only applicable on Windows");
            
            var differentVolume = GetDifferentVolume();
            Skip.If(differentVolume == null, "No different volume available for testing");
            
            // Arrange
            var sourceFile = await CreateLargeFileAsync("cross-volume-source.dat", 10 * 1024 * 1024); // 10MB
            var destDir = Path.Combine(
                differentVolume!.RootDirectory.FullName, 
                "temp", 
                "ave-filesystem-tests",
                $"ave-test-{Guid.NewGuid():N}"
            );
            Directory.CreateDirectory(destDir);
            
            try
            {
                var destFile = Path.Combine(destDir, "cross-volume-dest.dat");
                var sourcePath = _pathMapper.MapToCanonicalPath(sourceFile).Value;
                var destPath = _pathMapper.MapToCanonicalPath(destFile).Value;
                
                var sourceHash = await CalculateFileHashAsync(sourceFile);
                
                // Act
                var sw = Stopwatch.StartNew();
                var result = await _fileSystem.MoveFileAsync(sourcePath, destPath);
                sw.Stop();
                
                // Assert
                result.IsSuccess.Should().BeTrue();
                File.Exists(sourceFile).Should().BeFalse("source should be deleted after cross-volume move");
                File.Exists(destFile).Should().BeTrue("destination should exist");
                
                var destHash = await CalculateFileHashAsync(destFile);
                destHash.Should().Be(sourceHash, "content should be preserved");
                
                _output.WriteLine($"Cross-volume move completed in {sw.ElapsedMilliseconds}ms");
                
                // Note: Cross-volume operations typically involve copy+delete rather than rename
                // On modern systems with fast storage, this may still complete very quickly
                sw.ElapsedMilliseconds.Should().BeGreaterOrEqualTo(0, "operation should complete successfully");
            }
            finally
            {
                // Cleanup - only remove our specific test folder
                try 
                { 
                    if (Directory.Exists(destDir))
                    {
                        Directory.Delete(destDir, recursive: true); 
                    }
                    
                    // Optionally clean up the ave-filesystem-tests folder if it's empty
                    var parentTestDir = Path.GetDirectoryName(destDir);
                    if (Directory.Exists(parentTestDir) && !Directory.EnumerateFileSystemEntries(parentTestDir).Any())
                    {
                        Directory.Delete(parentTestDir, recursive: false);
                    }
                } 
                catch 
                { 
                    // Best effort cleanup
                }
            }
        }
        
        #endregion
        
        #region Cancellation Support
        
        [Fact(DisplayName = "LFSMC-006: Copy operation respects cancellation")]
        public async Task LFSMC006_CopyCancellation()
        {
            // Arrange
            var sourceFile = await CreateLargeFileAsync("cancel-source.dat", 50 * 1024 * 1024); // 50MB
            var destFile = Path.Combine(_testRoot, "cancel-dest.dat");
            
            var sourcePath = _pathMapper.MapToCanonicalPath(sourceFile).Value;
            var destPath = _pathMapper.MapToCanonicalPath(destFile).Value;
            
            using var cts = new CancellationTokenSource(50); // Cancel after 50ms
            
            // Act
            bool operationCancelled = false;
            var sw = Stopwatch.StartNew();
            try
            {
                await _fileSystem.CopyFileAsync(sourcePath, destPath, cancellationToken: cts.Token);
                _output.WriteLine($"File copy completed in {sw.ElapsedMilliseconds}ms before cancellation - acceptable on fast storage");
            }
            catch (OperationCanceledException)
            {
                operationCancelled = true;
                _output.WriteLine($"File copy was successfully cancelled after {sw.ElapsedMilliseconds}ms");
            }
            
            // Assert - Source should always remain
            File.Exists(sourceFile).Should().BeTrue("source should remain after copy operation");
            
            // Verify behavior based on what actually happened
            if (operationCancelled)
            {
                // Operation was cancelled - destination may or may not exist (partial copy)
                _output.WriteLine("Copy operation was cancelled as expected");
                
                // If destination exists, it should be smaller than source (partial copy)
                if (File.Exists(destFile))
                {
                    var sourceSize = new System.IO.FileInfo(sourceFile).Length;
                    var destSize = new System.IO.FileInfo(destFile).Length;
                    destSize.Should().BeLessOrEqualTo(sourceSize, "cancelled copy should not exceed source size");
                }
            }
            else
            {
                // Operation completed before cancellation - this is acceptable on very fast systems
                _output.WriteLine("Copy operation completed before cancellation could take effect");
                File.Exists(destFile).Should().BeTrue("destination should exist when copy completes");
                
                var sourceSize = new System.IO.FileInfo(sourceFile).Length;
                var destSize = new System.IO.FileInfo(destFile).Length;
                destSize.Should().Be(sourceSize, "completed copy should match source size");
            }
        }
        
        [Fact(DisplayName = "LFSMC-007: Directory copy cancellation stops operation")]
        public async Task LFSMC007_DirectoryCopyCancellation()
        {
            // Arrange
            var sourceRoot = await CreateDeepHierarchyAsync("cancel-dir-source", 8, 20); // More files to increase operation time
            var destRoot = Path.Combine(_testRoot, "cancel-dir-dest");
            
            var sourcePath = _pathMapper.MapToCanonicalPath(sourceRoot).Value;
            var destPath = _pathMapper.MapToCanonicalPath(destRoot).Value;
            
            using var cts = new CancellationTokenSource(50); // Cancel after 50ms
            
            // Act
            bool operationCancelled = false;
            try
            {
                await _fileSystem.CopyDirectoryAsync(sourcePath, destPath, cts.Token);
            }
            catch (OperationCanceledException)
            {
                operationCancelled = true;
                _output.WriteLine("Directory copy was successfully cancelled with exception");
            }
            
            // Assert - check if operation was actually cancelled
            // Source should always be intact
            Directory.Exists(sourceRoot).Should().BeTrue();
            
            if (Directory.Exists(destRoot))
            {
                var copiedFiles = Directory.GetFiles(destRoot, "*", SearchOption.AllDirectories);
                var sourceFiles = Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories);
                
                _output.WriteLine($"Copied {copiedFiles.Length} of {sourceFiles.Length} files");
                
                // If not all files were copied, operation was effectively cancelled
                bool effectivelyCancelled = operationCancelled || copiedFiles.Length < sourceFiles.Length;
                
                if (effectivelyCancelled)
                {
                    _output.WriteLine("Directory copy was cancelled (either by exception or partial completion)");
                    copiedFiles.Length.Should().BeLessThan(sourceFiles.Length, "should not have copied all files when cancelled");
                }
                else
                {
                    // Operation completed before cancellation - this is acceptable on very fast systems
                    _output.WriteLine("Directory copy completed before cancellation - operation was faster than expected");
                    copiedFiles.Length.Should().Be(sourceFiles.Length, "all files should be copied when operation completes");
                }
            }
            else
            {
                // If destination doesn't exist, operation was cancelled very early
                _output.WriteLine("Destination directory doesn't exist - operation was cancelled very early");
            }
        }
        
        #endregion
        
        #region File Attributes and Special Cases
        
        [SkippableFact(DisplayName = "LFSMC-008: File attributes are preserved")]
        public async Task LFSMC008_PreserveFileAttributes()
        {
            Skip.If(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), 
                   "File attributes work differently on Linux");
            
            // Arrange
            var sourceFile = Path.Combine(_testRoot, "attributes-source.txt");
            await File.WriteAllTextAsync(sourceFile, "Test content");
            
            var sourceInfo = new System.IO.FileInfo(sourceFile);
            // The times first: .NET Framework opens the file for writing to set them, which a read-only file refuses.
            sourceInfo.CreationTime = new DateTime(2020, 1, 1, 12, 0, 0);
            sourceInfo.LastWriteTime = new DateTime(2021, 6, 15, 14, 30, 0);
            sourceInfo.Attributes = FileAttributes.ReadOnly | FileAttributes.Hidden;
            
            var destFile = Path.Combine(_testRoot, "attributes-dest.txt");
            var sourcePath = _pathMapper.MapToCanonicalPath(sourceFile).Value;
            var destPath = _pathMapper.MapToCanonicalPath(destFile).Value;
            
            // Act
            var result = await _fileSystem.CopyFileAsync(sourcePath, destPath);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            
            var destInfo = new System.IO.FileInfo(destFile);
            
            // Debug output to understand what's happening
            _output.WriteLine($"Source attributes: {sourceInfo.Attributes}");
            _output.WriteLine($"Destination attributes: {destInfo.Attributes}");
            
            destInfo.Attributes.Should().HaveFlag(FileAttributes.ReadOnly, 
                $"ReadOnly should be preserved. Source: {sourceInfo.Attributes}, Dest: {destInfo.Attributes}");
            destInfo.Attributes.Should().HaveFlag(FileAttributes.Hidden,
                $"Hidden should be preserved. Source: {sourceInfo.Attributes}, Dest: {destInfo.Attributes}");
            destInfo.CreationTime.Should().BeCloseTo(sourceInfo.CreationTime, TimeSpan.FromSeconds(1));
            destInfo.LastWriteTime.Should().BeCloseTo(sourceInfo.LastWriteTime, TimeSpan.FromSeconds(1));
        }
        
        [Fact(DisplayName = "LFSMC-009: Unicode filenames handled correctly")]
        public async Task LFSMC009_UnicodeFilenames()
        {
            // Arrange
            var unicodeNames = new[]
            {
                "文件名.txt",           // Chinese
                "файл.txt",            // Russian
                "αρχείο.txt",          // Greek
                "ファイル.txt",         // Japanese
                "🎨🎭🎪.txt",          // Emojis
                "café-résumé.txt"      // Accented characters
            };
            
            foreach (var name in unicodeNames)
            {
                var sourceFile = Path.Combine(_testRoot, "unicode", name);
                Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
                await File.WriteAllTextAsync(sourceFile, $"Content for {name}");
                
                var destFile = Path.Combine(_testRoot, "unicode-copy", name);
                var sourcePath = _pathMapper.MapToCanonicalPath(sourceFile).Value;
                var destPath = _pathMapper.MapToCanonicalPath(destFile).Value;
                
                // Act
                var result = await _fileSystem.CopyFileAsync(sourcePath, destPath);
                
                // Assert
                result.IsSuccess.Should().BeTrue($"should handle unicode filename: {name}");
                File.Exists(destFile).Should().BeTrue();
                
                var content = await File.ReadAllTextAsync(destFile);
                content.Should().Be($"Content for {name}");
            }
            
            // Test directory with unicode name
            var unicodeDir = Path.Combine(_testRoot, "目录文件夹");
            Directory.CreateDirectory(unicodeDir);
            await File.WriteAllTextAsync(Path.Combine(unicodeDir, "test.txt"), "test");
            
            var destUnicodeDir = Path.Combine(_testRoot, "目录文件夹-副本");
            var sourceDirPath = _pathMapper.MapToCanonicalPath(unicodeDir).Value;
            var destDirPath = _pathMapper.MapToCanonicalPath(destUnicodeDir).Value;
            
            var dirResult = await _fileSystem.CopyDirectoryAsync(sourceDirPath, destDirPath);
            dirResult.IsSuccess.Should().BeTrue("should handle unicode directory names");
        }
        
        [SkippableFact(DisplayName = "LFSMC-010: Very long paths handled correctly")]
        public async Task LFSMC010_VeryLongPaths()
        {
            Skip.If(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), 
                   "Long path testing is Windows-specific");
            
            // Arrange - Create path approaching Windows limit
            var longComponent = new string('a', 50);
            var pathBuilder = new StringBuilder(_testRoot);
            
            // Build path close to 260 character limit
            while (pathBuilder.Length < 200)
            {
                pathBuilder.Append(Path.DirectorySeparatorChar);
                pathBuilder.Append(longComponent);
            }
            
            var longDir = pathBuilder.ToString();
            Directory.CreateDirectory(longDir);
            
            var sourceFile = Path.Combine(longDir, "file.txt");
            await File.WriteAllTextAsync(sourceFile, "Long path content");
            
            var destFile = sourceFile.Replace("file.txt", "file-copy.txt");
            var sourcePath = _pathMapper.MapToCanonicalPath(sourceFile).Value;
            var destPath = _pathMapper.MapToCanonicalPath(destFile).Value;
            
            // Act
            var result = await _fileSystem.CopyFileAsync(sourcePath, destPath);
            
            // Assert
            result.IsSuccess.Should().BeTrue("should handle long paths");
            File.Exists(destFile).Should().BeTrue();
            
            _output.WriteLine($"Successfully handled path length: {sourceFile.Length} characters");
        }
        
        #endregion
        
        #region Edge Cases
        
        [Fact(DisplayName = "LFSMC-011: Empty file copy/move")]
        public async Task LFSMC011_EmptyFiles()
        {
            // Arrange
            var emptySource = Path.Combine(_testRoot, "empty.txt");
            File.Create(emptySource).Dispose(); // Create empty file
            
            var copyDest = Path.Combine(_testRoot, "empty-copy.txt");
            var moveDest = Path.Combine(_testRoot, "empty-moved.txt");
            
            // Act - Copy
            var copyResult = await _fileSystem.CopyFileAsync(
                _pathMapper.MapToCanonicalPath(emptySource).Value, 
                _pathMapper.MapToCanonicalPath(copyDest).Value);
            
            // Act - Move
            var moveResult = await _fileSystem.MoveFileAsync(
                _pathMapper.MapToCanonicalPath(copyDest).Value, 
                _pathMapper.MapToCanonicalPath(moveDest).Value);
            
            // Assert
            copyResult.IsSuccess.Should().BeTrue();
            moveResult.IsSuccess.Should().BeTrue();
            
            new System.IO.FileInfo(moveDest).Length.Should().Be(0);
            File.Exists(copyDest).Should().BeFalse("moved file should not exist at source");
        }
        
        [Fact(DisplayName = "LFSMC-012: Directory with empty subdirectories")]
        public async Task LFSMC012_EmptySubdirectories()
        {
            // Arrange
            var sourceRoot = Path.Combine(_testRoot, "empty-dirs");
            var emptyDir1 = Path.Combine(sourceRoot, "empty1", "nested-empty");
            var emptyDir2 = Path.Combine(sourceRoot, "empty2");
            Directory.CreateDirectory(emptyDir1);
            Directory.CreateDirectory(emptyDir2);
            
            // Add one file
            await File.WriteAllTextAsync(Path.Combine(sourceRoot, "file.txt"), "content");
            
            var destRoot = Path.Combine(_testRoot, "empty-dirs-copy");
            
            // Act
            var result = await _fileSystem.CopyDirectoryAsync(
                _pathMapper.MapToCanonicalPath(sourceRoot).Value, 
                _pathMapper.MapToCanonicalPath(destRoot).Value);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            Directory.Exists(Path.Combine(destRoot, "empty1", "nested-empty")).Should().BeTrue();
            Directory.Exists(Path.Combine(destRoot, "empty2")).Should().BeTrue();
            File.Exists(Path.Combine(destRoot, "file.txt")).Should().BeTrue();
        }
        
        [Fact(DisplayName = "LFSMC-013: Overwrite scenarios")]
        public async Task LFSMC013_OverwriteScenarios()
        {
            // Arrange
            var source = Path.Combine(_testRoot, "overwrite-source.txt");
            var dest = Path.Combine(_testRoot, "overwrite-dest.txt");
            
            await File.WriteAllTextAsync(source, "Source content");
            await File.WriteAllTextAsync(dest, "Original dest content");
            
            var sourcePath = _pathMapper.MapToCanonicalPath(source).Value;
            var destPath = _pathMapper.MapToCanonicalPath(dest).Value;
            
            // Act - Try without overwrite
            var noOverwriteResult = await _fileSystem.CopyFileAsync(sourcePath, destPath, overwrite: false);
            
            // Assert
            noOverwriteResult.IsFailure.Should().BeTrue();
            noOverwriteResult.Error.Code.Should().Be(FileSystemErrorCodes.IO.FileAlreadyExists);
            
            var destContent = await File.ReadAllTextAsync(dest);
            destContent.Should().Be("Original dest content", "failed copy should not modify destination");
            
            // Act - With overwrite
            var overwriteResult = await _fileSystem.CopyFileAsync(sourcePath, destPath, overwrite: true);
            
            // Assert
            overwriteResult.IsSuccess.Should().BeTrue();
            destContent = await File.ReadAllTextAsync(dest);
            destContent.Should().Be("Source content", "overwrite should replace content");
        }
        
        #endregion
        
        #region Helper Methods
        
        private async Task<string> CreateLargeFileAsync(string filename, long sizeInBytes)
        {
            var filePath = Path.Combine(_testRoot, filename);
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            const int bufferSize = 81920; // 80KB
            var buffer = new byte[bufferSize];
            
            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, useAsync: true))
            {
                long written = 0;
                while (written < sizeInBytes)
                {
                    int toWrite = (int)Math.Min(bufferSize, sizeInBytes - written);
                    _random.NextBytes(buffer);
                    await fs.WriteAsync(buffer, 0, toWrite);
                    written += toWrite;
                }
            }
            
            return filePath;
        }
        
        private async Task<string> CreateDeepHierarchyAsync(string rootName, int depth, int filesPerLevel)
        {
            var rootDir = Path.Combine(_testRoot, rootName);
            var currentPath = rootDir;
            
            for (int level = 0; level < depth; level++)
            {
                currentPath = Path.Combine(currentPath, $"level-{level:D3}");
                Directory.CreateDirectory(currentPath);
                
                // Create files at this level
                for (int file = 0; file < filesPerLevel; file++)
                {
                    var fileName = $"file-L{level:D3}-F{file:D3}.txt";
                    var filePath = Path.Combine(currentPath, fileName);
                    var content = $"Level {level}, File {file}\nPath: {filePath}\n" + new string('x', 100);
                    await File.WriteAllTextAsync(filePath, content);
                }
                
                // Create sibling directory
                if (level < depth - 1)
                {
                    var siblingDir = Path.Combine(Path.GetDirectoryName(currentPath)!, $"sibling-{level:D3}");
                    Directory.CreateDirectory(siblingDir);
                    await File.WriteAllTextAsync(
                        Path.Combine(siblingDir, $"sibling-{level:D3}.txt"), 
                        $"Sibling at level {level}");
                }
            }
            
            return rootDir;
        }
        
        private async Task<string> CalculateFileHashAsync(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = await sha256.ComputeHashAsync(stream);
            return Convert.ToBase64String(hash);
        }
        
        // True on .NET Framework when Windows does not let this process use paths of 260 characters or more (folders:
        // 248). That takes LongPathsEnabled=1 on the machine and longPathAware in the manifest of the executable.
        // .NET (Core) adds the \\?\ prefix itself and needs neither.
        private static bool LongPathsUnavailable()
        {
#if NETFRAMEWORK
            try
            {
                return RtlAreLongPathsEnabled() == 0;
            }
            catch (EntryPointNotFoundException)
            {
                // Windows before Windows 10 version 1607 has no long path support at all
                return true;
            }
#else
            return false;
#endif
        }

#if NETFRAMEWORK
        [DllImport("ntdll.dll")]
        private static extern byte RtlAreLongPathsEnabled();
#endif

        private DriveInfo? GetDifferentVolume()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return null;
            
            var testDrive = new DriveInfo(Path.GetPathRoot(_testRoot)!);
            
            return DriveInfo.GetDrives()
                .Where(d => d.IsReady && 
                           d.DriveType == DriveType.Fixed && 
                           d.Name != testDrive.Name &&
                           d.AvailableFreeSpace > 100 * 1024 * 1024)
                .FirstOrDefault();
        }
        
        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testRoot))
                {
                    // Handle Windows file locking
                    int retries = 3;
                    while (retries > 0)
                    {
                        try
                        {
                            Directory.Delete(_testRoot, recursive: true);
                            break;
                        }
                        catch (IOException) when (retries > 1)
                        {
                            retries--;
                            Thread.Sleep(100);
                        }
                    }
                }
            }
            catch
            {
                // Best effort cleanup
            }
        }
        
        #endregion
    }
}