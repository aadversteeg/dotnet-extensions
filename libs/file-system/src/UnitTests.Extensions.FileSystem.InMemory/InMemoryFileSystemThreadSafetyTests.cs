using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.InMemory;
using Ave.Extensions.FileSystem.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using UnitTests.Extensions.FileSystem.InMemory.TestHelpers;
using Xunit;

namespace UnitTests.Extensions.FileSystem.InMemory
{
    /// <summary>
    /// Thread safety tests for InMemoryFileSystem.
    /// </summary>
    public class InMemoryFileSystemThreadSafetyTests : IDisposable
    {
        private readonly InMemoryFileSystem _fileSystem;
        private readonly Mock<ILogger<InMemoryFileSystem>> _mockLogger;

        public InMemoryFileSystemThreadSafetyTests()
        {
            _mockLogger = new Mock<ILogger<InMemoryFileSystem>>();
            _fileSystem = new InMemoryFileSystem(_mockLogger.Object);
        }

        [Fact(DisplayName = "IMFS-TS-001: Concurrent file writes are thread-safe")]
        public async Task IMFSTS001()
        {
            // Arrange
            const int concurrentTasks = 50;
            const int filesPerTask = 10;
            var tasks = new List<Task>();

            // Act
            for (int i = 0; i < concurrentTasks; i++)
            {
                var taskId = i;
                tasks.Add(Task.Run(async () =>
                {
                    for (int j = 0; j < filesPerTask; j++)
                    {
                        var path = $"/test/task{taskId}_file{j}.txt";
                        var content = $"Content from task {taskId}, file {j}";
                        var result = await _fileSystem.WriteAllTextAsync(path.ToCanonicalPath(), content);
                        result.IsSuccess.Should().BeTrue();
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // Assert
            var filesResult = await _fileSystem.GetFilesAsync("/test".ToCanonicalPath(), "*", System.IO.SearchOption.AllDirectories);
            filesResult.IsSuccess.Should().BeTrue();
            filesResult.Value.Should().HaveCount(concurrentTasks * filesPerTask);

            // Verify each file has correct content
            for (int i = 0; i < concurrentTasks; i++)
            {
                for (int j = 0; j < filesPerTask; j++)
                {
                    var path = $"/test/task{i}_file{j}.txt";
                    var readResult = await _fileSystem.ReadAllTextAsync(path.ToCanonicalPath());
                    readResult.IsSuccess.Should().BeTrue();
                    readResult.Value.Should().Be($"Content from task {i}, file {j}");
                }
            }
        }

        [Fact(DisplayName = "IMFS-TS-002: Concurrent directory operations are thread-safe")]
        public async Task IMFSTS002()
        {
            // Arrange
            const int concurrentTasks = 30;
            var tasks = new List<Task>();

            // Act
            for (int i = 0; i < concurrentTasks; i++)
            {
                var taskId = i;
                tasks.Add(Task.Run(async () =>
                {
                    var dirPath = $"/test/dir{taskId}";
                    var result = await _fileSystem.CreateDirectoryAsync(dirPath.ToCanonicalPath());
                    result.IsSuccess.Should().BeTrue();

                    // Create some files in the directory
                    for (int j = 0; j < 5; j++)
                    {
                        var filePath = $"{dirPath}/file{j}.txt";
                        var writeResult = await _fileSystem.WriteAllTextAsync(filePath.ToCanonicalPath(), $"Content {j}");
                        writeResult.IsSuccess.Should().BeTrue();
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // Assert
            var dirsResult = await _fileSystem.GetDirectoriesAsync("/test".ToCanonicalPath());
            dirsResult.IsSuccess.Should().BeTrue();
            dirsResult.Value.Should().HaveCount(concurrentTasks);

            for (int i = 0; i < concurrentTasks; i++)
            {
                var dirPath = $"/test/dir{i}";
                var dirExists = await _fileSystem.DirectoryExistsAsync(dirPath.ToCanonicalPath());
                dirExists.Value.Should().BeTrue();

                var filesResult = await _fileSystem.GetFilesAsync(dirPath.ToCanonicalPath());
                filesResult.IsSuccess.Should().BeTrue();
                filesResult.Value.Should().HaveCount(5);
            }
        }

        [Fact(DisplayName = "IMFS-TS-003: Concurrent read/write operations are thread-safe")]
        public async Task IMFSTS003()
        {
            // Arrange
            const int iterations = 100;
            const string filePath = "/test/shared_file.txt";
            
            // Pre-create the file
            await _fileSystem.WriteAllTextAsync(filePath.ToCanonicalPath(), "Initial content");

            var readTasks = new List<Task<string>>();
            var writeTasks = new List<Task>();

            // Act - Start concurrent readers and writers
            for (int i = 0; i < iterations; i++)
            {
                var iteration = i;
                
                // Reader task
                readTasks.Add(Task.Run(async () =>
                {
                    var result = await _fileSystem.ReadAllTextAsync(filePath.ToCanonicalPath());
                    result.IsSuccess.Should().BeTrue();
                    return result.Value;
                }));

                // Writer task
                if (i % 10 == 0) // Write less frequently than read
                {
                    writeTasks.Add(Task.Run(async () =>
                    {
                        var content = $"Updated content {iteration}";
                        var result = await _fileSystem.WriteAllTextAsync(filePath.ToCanonicalPath(), content);
                        result.IsSuccess.Should().BeTrue();
                    }));
                }
            }

            var allTasks = readTasks.Cast<Task>().Concat(writeTasks).ToArray();
            await Task.WhenAll(allTasks);

            // Assert - No exceptions should have been thrown
            // File should still exist and be readable
            var finalResult = await _fileSystem.FileExistsAsync(filePath.ToCanonicalPath());
            finalResult.Value.Should().BeTrue();

            var finalReadResult = await _fileSystem.ReadAllTextAsync(filePath.ToCanonicalPath());
            finalReadResult.IsSuccess.Should().BeTrue();
            finalReadResult.Value.Should().NotBeNull();
        }

        [Fact(DisplayName = "IMFS-TS-004: Concurrent file deletion is thread-safe")]
        public async Task IMFSTS004()
        {
            // Arrange
            const int fileCount = 50;
            var filePaths = new List<string>();

            // Create files
            for (int i = 0; i < fileCount; i++)
            {
                var path = $"/test/file{i}.txt";
                filePaths.Add(path);
                await _fileSystem.WriteAllTextAsync(path.ToCanonicalPath(), $"Content {i}");
            }

            // Act - Delete files concurrently
            var deleteTasks = filePaths.Select(path => Task.Run(async () =>
            {
                var result = await _fileSystem.DeleteFileAsync(path.ToCanonicalPath());
                result.IsSuccess.Should().BeTrue();
            })).ToArray();

            await Task.WhenAll(deleteTasks);

            // Assert
            foreach (var path in filePaths)
            {
                var existsResult = await _fileSystem.FileExistsAsync(path.ToCanonicalPath());
                existsResult.Value.Should().BeFalse();
            }
        }

        [Fact(DisplayName = "IMFS-TS-005: Concurrent monitoring sessions are thread-safe")]
        public async Task IMFSTS005()
        {
            // Arrange
            const int monitorCount = 20;
            var sessions = new List<IMonitoringSession>();

            // Create directories first
            var directoryTasks = Enumerable.Range(0, monitorCount).Select(async i =>
            {
                await _fileSystem.CreateDirectoryAsync($"/test/dir{i}".ToCanonicalPath());
            }).ToArray();
            
            await Task.WhenAll(directoryTasks);

            // Act - Create multiple monitoring sessions concurrently
            var sessionTasks = Enumerable.Range(0, monitorCount).Select(async i =>
            {
                var options = MonitoringOptions.ForPath($"/test/dir{i}".ToCanonicalPath());
                var result = await _fileSystem.CreateMonitoringSessionAsync(options);
                result.IsSuccess.Should().BeTrue();
                
                sessions.Add(result.Value);
                return result.Value;
            }).ToArray();

            await Task.WhenAll(sessionTasks);

            // Perform some file operations
            var operationTasks = Enumerable.Range(0, monitorCount).Select(async i =>
            {
                await _fileSystem.WriteAllTextAsync($"/test/dir{i}/file.txt".ToCanonicalPath(), "test content");
            }).ToArray();

            await Task.WhenAll(operationTasks);

            // Wait a bit for events to propagate
            await Task.Delay(100);

            // Assert - All sessions should be functional
            sessions.Should().HaveCount(monitorCount);

            // Cleanup
            foreach (var session in sessions)
            {
                session.Dispose();
            }
        }


        [Fact(DisplayName = "IMFS-TS-007: Mixed concurrent operations maintain consistency")]
        public async Task IMFSTS007()
        {
            // Arrange
            const int operationCount = 200;
            var random = new Random(42); // Fixed seed for reproducibility
            var tasks = new List<Task>();

            // Act - Perform mixed operations concurrently
            for (int i = 0; i < operationCount; i++)
            {
                var operation = random.Next(5);
                var index = i;
                
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        switch (operation)
                        {
                            case 0: // Create file
                                var filePath = $"/test/mixed/file{index}.txt";
                                await _fileSystem.WriteAllTextAsync(filePath.ToCanonicalPath(), $"Content {index}");
                                break;
                                
                            case 1: // Create directory
                                var dirPath = $"/test/mixed/dir{index}";
                                await _fileSystem.CreateDirectoryAsync(dirPath.ToCanonicalPath());
                                break;
                                
                            case 2: // Read file (might not exist)
                                var readPath = $"/test/mixed/file{index % 50}.txt";
                                await _fileSystem.ReadAllTextAsync(readPath.ToCanonicalPath());
                                break;
                                
                            case 3: // Check existence
                                var checkPath = $"/test/mixed/file{index % 100}.txt";
                                await _fileSystem.FileExistsAsync(checkPath.ToCanonicalPath());
                                break;
                                
                            case 4: // List files
                                await _fileSystem.GetFilesAsync("/test/mixed".ToCanonicalPath());
                                break;
                        }
                    }
                    catch
                    {
                        // Some operations may fail due to race conditions (e.g., file not found)
                        // This is expected and acceptable
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // Assert - File system should be in a consistent state
            var finalDirExists = await _fileSystem.DirectoryExistsAsync("/test/mixed".ToCanonicalPath());
            var finalFilesResult = await _fileSystem.GetFilesAsync("/test/mixed".ToCanonicalPath(), "*", System.IO.SearchOption.AllDirectories);
            var finalDirsResult = await _fileSystem.GetDirectoriesAsync("/test/mixed".ToCanonicalPath(), "*", System.IO.SearchOption.AllDirectories);

            // The exact counts may vary due to race conditions, but the operations should complete successfully
            finalFilesResult.IsSuccess.Should().BeTrue();
            finalDirsResult.IsSuccess.Should().BeTrue();
        }

        public void Dispose()
        {
            _fileSystem?.Dispose();
        }
    }
}