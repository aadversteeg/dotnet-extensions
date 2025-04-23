using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;

namespace Ave.Extensions.FileSystem
{
    public class FileSystemMonitorTests
    {
        [Fact(DisplayName = "FSM-001: CreateMonitorAsync returns non-null monitor")]
        public async Task FSM001()
        {
            // Arrange
            var fileSystem = new FileSystemService();

            // Act
            var monitor = await fileSystem.CreateMonitorAsync();

            // Assert
            monitor.Should().NotBeNull();
            monitor.Should().BeAssignableTo<IFileSystemMonitor>();
        }

        [Fact(DisplayName = "FSM-002: WatchDirectoryAsync returns valid monitoring handle")]
        public async Task FSM002()
        {
            // Arrange
            var fileSystem = new FileSystemService();
            var monitor = await fileSystem.CreateMonitorAsync();
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                // Act
                var handle = await monitor.WatchDirectoryAsync(
                    tempDir,
                    false,
                    "*",
                    FileSystemNotifyFilters.All);

                // Assert
                handle.Should().NotBeNull();
                handle.Path.Should().Be(Path.GetFullPath(tempDir));
                
                // Cleanup
                await handle.StopAsync();
                handle.Dispose();
            }
            finally
            {
                Directory.Delete(tempDir);
            }
        }

        [Fact(DisplayName = "FSM-003: Monitor raises FileChanged event when file is created")]
        public async Task FSM003()
        {
            // Arrange
            var fileSystem = new FileSystemService();
            var monitor = await fileSystem.CreateMonitorAsync();
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                var eventReceived = new TaskCompletionSource<FileSystemChangeEventArgs>();
                monitor.FileChanged += (sender, args) => eventReceived.TrySetResult(args);

                var handle = await monitor.WatchDirectoryAsync(
                    tempDir,
                    false,
                    "*",
                    FileSystemNotifyFilters.All);

                // Act
                var testFilePath = Path.Combine(tempDir, "test.txt");
                File.WriteAllText(testFilePath, "test content");

                // Assert
                var taskCompletedInTime = await Task.WhenAny(eventReceived.Task, Task.Delay(TimeSpan.FromSeconds(5))) == eventReceived.Task;
                taskCompletedInTime.Should().BeTrue("the event should be received within the timeout");
                var result = await eventReceived.Task;
                result.Should().NotBeNull();
                result.ChangeType.Should().Be(FileSystemChangeType.Created);
                Path.GetFullPath(result.FullPath).Should().Be(Path.GetFullPath(testFilePath));
                
                // Cleanup
                await handle.StopAsync();
                handle.Dispose();
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact(DisplayName = "FSM-004: ReadOnlyFileSystem can create monitor through wrapping")]
        public async Task FSM004()
        {
            // Arrange
            var fileSystem = new FileSystemService();
            var readOnlyFileSystem = fileSystem.AsReadOnly();

            // Act
            var monitor = await readOnlyFileSystem.CreateMonitorAsync();

            // Assert
            monitor.Should().NotBeNull();
            monitor.Should().BeAssignableTo<IFileSystemMonitor>();
        }

        [Fact(DisplayName = "FSM-005: WatchDirectoryAsStreamAsync returns async stream of events")]
        public async Task FSM005()
        {
            // Arrange
            var fileSystem = new FileSystemService();
            var monitor = await fileSystem.CreateMonitorAsync();
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            
            var events = new List<FileSystemChangeEventArgs>();
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            try
            {
                // Start monitoring as a background task
                var monitoringTask = Task.Run(async () =>
                {
                    await foreach (var change in monitor.WatchDirectoryAsStreamAsync(
                        tempDir, 
                        false, 
                        "*", 
                        FileSystemNotifyFilters.All, 
                        cts.Token))
                    {
                        events.Add(change);
                        if (events.Count >= 2) // We expect 2 events
                        {
                            cts.Cancel();
                            break;
                        }
                    }
                });

                // Act - create a file, then modify it
                await Task.Delay(500); // Give the watcher time to initialize
                var testFilePath = Path.Combine(tempDir, "test-stream.txt");
                File.WriteAllText(testFilePath, "initial content");
                await Task.Delay(500); // Give the watcher time to detect
                File.WriteAllText(testFilePath, "updated content");

                // Wait for the monitoring to complete (either by timeout or by collecting events)
                try
                {
                    await monitoringTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected when we cancel the token
                }

                // Assert
                events.Should().HaveCountGreaterThanOrEqualTo(1, "at least one file change should be detected");
                events.Where(e => e.ChangeType == FileSystemChangeType.Created).Should().HaveCountGreaterThanOrEqualTo(1);
            }
            finally
            {
                cts.Cancel(); // Ensure monitoring is stopped
                cts.Dispose();
                
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }
    }
}