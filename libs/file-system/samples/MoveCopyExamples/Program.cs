using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Extensions;
using Ave.Extensions.FileSystem.Local;
using Ave.Extensions.FileSystem.Local.PathMappers;
using Ave.Extensions.FileSystem.InMemory;
using Ave.Extensions.FileSystem.Progress;
using Ave.Extensions.Functional;

namespace MoveCopyExamples
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Ave.Extensions.FileSystem - Move and Copy Examples");
            Console.WriteLine("==================================================\n");

            // Example 1: Basic file operations
            await BasicFileOperationsExample();
            
            // Example 2: Directory operations
            await DirectoryOperationsExample();
            
            // Example 3: Progress reporting
            await ProgressReportingExample();
            
            // Example 4: Error handling
            await ErrorHandlingExample();
            
            // Example 5: Performance comparison
            await PerformanceComparisonExample();
            
            // Example 6: Cross-platform considerations
            await CrossPlatformExample();
            
            Console.WriteLine("\nAll examples completed!");
        }
        
        static async Task BasicFileOperationsExample()
        {
            Console.WriteLine("1. Basic File Operations");
            Console.WriteLine("------------------------");
            
            var pathMapper = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
                ? (IPathMapper)new WindowsPathMapper() 
                : new UnixPathMapper();
            var fileSystem = new LocalFileSystem(pathMapper);
            var testDir = CanonicalPath.Create(Path.Combine(Path.GetTempPath(), "ave-examples")).Value;
            
            // Create test directory
            await fileSystem.CreateDirectoryAsync(testDir);
            
            // Create a test file
            var sourceFile = CanonicalPath.Create(Path.Combine(testDir.Value, "source.txt")).Value;
            var destFile = CanonicalPath.Create(Path.Combine(testDir.Value, "dest.txt")).Value;
            
            await fileSystem.WriteAllTextAsync(sourceFile, "Hello, World!");
            Console.WriteLine($"Created source file: {sourceFile.Value}");
            
            // Copy the file
            var copyResult = await fileSystem.CopyFileAsync(sourceFile, destFile);
            if (copyResult.IsSuccess)
            {
                Console.WriteLine($"Copied to: {destFile.Value}");
            }
            
            // Move the original
            var movedFile = CanonicalPath.Create(Path.Combine(testDir.Value, "moved.txt")).Value;
            var moveResult = await fileSystem.MoveFileAsync(sourceFile, movedFile);
            if (moveResult.IsSuccess)
            {
                Console.WriteLine($"Moved to: {movedFile.Value}");
            }
            
            // Verify
            var sourceExists = await fileSystem.FileExistsAsync(sourceFile);
            Console.WriteLine($"Source exists after move: {sourceExists.Value}");
            
            // Cleanup
            await fileSystem.DeleteDirectoryAsync(testDir, recursive: true);
            Console.WriteLine();
        }
        
        static async Task DirectoryOperationsExample()
        {
            Console.WriteLine("2. Directory Operations");
            Console.WriteLine("-----------------------");
            
            var fileSystem = new InMemoryFileSystem(); // Using in-memory for demo
            
            // Create source directory structure
            var sourceDir = CanonicalPath.Create("/source-dir").Value;
            await fileSystem.CreateDirectoryAsync(sourceDir);
            await fileSystem.WriteAllTextAsync(CanonicalPath.Create("/source-dir/file1.txt").Value, "File 1");
            await fileSystem.WriteAllTextAsync(CanonicalPath.Create("/source-dir/file2.txt").Value, "File 2");
            await fileSystem.CreateDirectoryAsync(CanonicalPath.Create("/source-dir/subdir").Value);
            await fileSystem.WriteAllTextAsync(CanonicalPath.Create("/source-dir/subdir/file3.txt").Value, "File 3");
            
            Console.WriteLine("Created source directory structure");
            
            // Copy entire directory
            var copyDest = CanonicalPath.Create("/copy-dest").Value;
            var copyResult = await fileSystem.CopyDirectoryAsync(sourceDir, copyDest);
            Console.WriteLine($"Directory copy result: {(copyResult.IsSuccess ? "Success" : copyResult.Error.Message)}");
            
            // Move directory
            var moveDest = CanonicalPath.Create("/move-dest").Value;
            var moveResult = await fileSystem.MoveDirectoryAsync(sourceDir, moveDest);
            Console.WriteLine($"Directory move result: {(moveResult.IsSuccess ? "Success" : moveResult.Error.Message)}");
            
            // Verify source no longer exists
            var sourceExists = await fileSystem.DirectoryExistsAsync(sourceDir);
            Console.WriteLine($"Source directory exists after move: {sourceExists.Value}");
            Console.WriteLine();
        }
        
        static async Task ProgressReportingExample()
        {
            Console.WriteLine("3. Progress Reporting");
            Console.WriteLine("---------------------");
            
            var fileSystem = new InMemoryFileSystem();
            
            // Create a directory with multiple files
            var sourceDir = CanonicalPath.Create("/progress-test").Value;
            await fileSystem.CreateDirectoryAsync(sourceDir);
            
            // Create several files
            for (int i = 1; i <= 5; i++)
            {
                var filePath = CanonicalPath.Create($"/progress-test/file{i}.dat").Value;
                var content = new string('X', i * 1000); // Varying sizes
                await fileSystem.WriteAllTextAsync(filePath, content);
            }
            
            // Set up progress reporting
            var lastProgress = 0.0;
            var progress = new Progress<ICopyProgress>(p =>
            {
                var currentProgress = p.TotalBytes > 0 ? (double)p.BytesTransferred / p.TotalBytes * 100 : 0;
                if (currentProgress - lastProgress >= 10 || currentProgress >= 100)
                {
                    Console.WriteLine($"Progress: {p.FilesProcessed}/{p.TotalFiles} files " +
                                    $"({currentProgress:F1}%) - Current: {Path.GetFileName(p.CurrentFile)}");
                    lastProgress = currentProgress;
                }
            });
            
            var copyOptions = new CopyOptions
            {
                Progress = progress,
                Overwrite = false
            };
            
            // Copy with progress
            var destDir = CanonicalPath.Create("/progress-dest").Value;
            var result = await fileSystem.CopyAsync(sourceDir, destDir, copyOptions);
            
            Console.WriteLine($"Copy completed: {(result.IsSuccess ? "Success" : result.Error.Message)}");
            Console.WriteLine();
        }
        
        static async Task ErrorHandlingExample()
        {
            Console.WriteLine("4. Error Handling");
            Console.WriteLine("-----------------");
            
            var pathMapper = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
                ? (IPathMapper)new WindowsPathMapper() 
                : new UnixPathMapper();
            var fileSystem = new LocalFileSystem(pathMapper);
            var testDir = CanonicalPath.Create(Path.Combine(Path.GetTempPath(), "ave-errors")).Value;
            await fileSystem.CreateDirectoryAsync(testDir);
            
            try
            {
                // Example 1: File already exists
                var file1 = CanonicalPath.Create(Path.Combine(testDir.Value, "exists.txt")).Value;
                await fileSystem.WriteAllTextAsync(file1, "Original");
                var file2 = CanonicalPath.Create(Path.Combine(testDir.Value, "dest.txt")).Value;
                await fileSystem.WriteAllTextAsync(file2, "Destination"); // Create destination
                
                var copyResult = await fileSystem.CopyFileAsync(file1, file2, overwrite: false);
                if (copyResult.IsFailure)
                {
                    Console.WriteLine($"Expected error - {copyResult.Error.Code}: {copyResult.Error.Message}");
                }
                
                // Example 2: Source not found
                var nonExistent = CanonicalPath.Create(Path.Combine(testDir.Value, "not-found.txt")).Value;
                var moveResult = await fileSystem.MoveFileAsync(nonExistent, file1);
                if (moveResult.IsFailure)
                {
                    Console.WriteLine($"Expected error - {moveResult.Error.Code}: {moveResult.Error.Message}");
                }
                
                // Example 3: Directory already exists
                var dir1 = CanonicalPath.Create(Path.Combine(testDir.Value, "dir1")).Value;
                var dir2 = CanonicalPath.Create(Path.Combine(testDir.Value, "dir2")).Value;
                await fileSystem.CreateDirectoryAsync(dir1);
                await fileSystem.CreateDirectoryAsync(dir2);
                
                var dirCopyResult = await fileSystem.CopyDirectoryAsync(dir1, dir2);
                if (dirCopyResult.IsFailure)
                {
                    Console.WriteLine($"Expected error - {dirCopyResult.Error.Code}: {dirCopyResult.Error.Message}");
                }
            }
            finally
            {
                // Cleanup
                await fileSystem.DeleteDirectoryAsync(testDir, recursive: true);
            }
            
            Console.WriteLine();
        }
        
        static async Task PerformanceComparisonExample()
        {
            Console.WriteLine("5. Performance Comparison");
            Console.WriteLine("-------------------------");
            
            var pathMapper = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
                ? (IPathMapper)new WindowsPathMapper() 
                : new UnixPathMapper();
            var fileSystem = new LocalFileSystem(pathMapper);
            var testDir = CanonicalPath.Create(Path.Combine(Path.GetTempPath(), "ave-perf")).Value;
            await fileSystem.CreateDirectoryAsync(testDir);
            
            try
            {
                // Create a test file
                var sourceFile = CanonicalPath.Create(Path.Combine(testDir.Value, "perf-test.dat")).Value;
                var content = new byte[10 * 1024 * 1024]; // 10MB
                new Random().NextBytes(content);
                // Create file by writing bytes via stream
                var streamResult = await fileSystem.OpenWriteAsync(sourceFile);
                if (streamResult.IsSuccess)
                {
                    using var stream = streamResult.Value;
                    await stream.WriteAsync(content, 0, content.Length);
                }
                
                // Test 1: Same-volume move (should be fast)
                var moveDestSame = CanonicalPath.Create(Path.Combine(testDir.Value, "moved.dat")).Value;
                var sw = Stopwatch.StartNew();
                await fileSystem.MoveFileAsync(sourceFile, moveDestSame);
                sw.Stop();
                Console.WriteLine($"Same-volume move (10MB): {sw.ElapsedMilliseconds}ms");
                
                // Test 2: Copy operation
                var copyDest = CanonicalPath.Create(Path.Combine(testDir.Value, "copied.dat")).Value;
                sw.Restart();
                await fileSystem.CopyFileAsync(moveDestSame, copyDest);
                sw.Stop();
                Console.WriteLine($"Copy operation (10MB): {sw.ElapsedMilliseconds}ms");
                
                // Test 3: Traditional copy+delete
                var traditionalDest = CanonicalPath.Create(Path.Combine(testDir.Value, "traditional.dat")).Value;
                sw.Restart();
                await fileSystem.CopyFileAsync(copyDest, traditionalDest);
                await fileSystem.DeleteFileAsync(copyDest);
                sw.Stop();
                Console.WriteLine($"Traditional copy+delete (10MB): {sw.ElapsedMilliseconds}ms");
            }
            finally
            {
                // Cleanup
                await fileSystem.DeleteDirectoryAsync(testDir, recursive: true);
            }
            
            Console.WriteLine();
        }
        
        static Task CrossPlatformExample()
        {
            Console.WriteLine("6. Cross-Platform Considerations");
            Console.WriteLine("---------------------------------");
            
            var pathMapper = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
                ? (IPathMapper)new WindowsPathMapper() 
                : new UnixPathMapper();
            var fileSystem = new LocalFileSystem(pathMapper);
            
            // Show platform-specific path handling
            Console.WriteLine($"Current OS: {Environment.OSVersion.Platform}");
            Console.WriteLine($"Path separator: {Path.DirectorySeparatorChar}");
            
            // Canonical paths work across platforms
            var path1Result = CanonicalPath.Create("/cross/platform/path");
            var path2Result = CanonicalPath.Create(@"C:\windows\style\path");
            
            if (path1Result.IsSuccess)
            {
                Console.WriteLine($"Canonical path 1: {path1Result.Value.Value}");
            }
            
            if (path2Result.IsSuccess)
            {
                Console.WriteLine($"Canonical path 2: {path2Result.Value.Value}");
            }
            
            
            // Case sensitivity note
            if (Environment.OSVersion.Platform == PlatformID.Unix || 
                Environment.OSVersion.Platform == PlatformID.MacOSX)
            {
                Console.WriteLine("Note: File system is case-sensitive on this platform");
            }
            else
            {
                Console.WriteLine("Note: File system is case-insensitive on this platform");
            }
            
            Console.WriteLine();
            return Task.CompletedTask;
        }
    }
}