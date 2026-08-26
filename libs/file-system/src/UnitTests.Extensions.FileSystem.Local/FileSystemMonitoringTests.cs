using System;
using System.IO;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Local;
using Ave.Extensions.FileSystem.Local.PathMappers;
using Ave.Extensions.FileSystem.Models;
using FluentAssertions;

namespace UnitTests.Extensions.FileSystem.Local
{
    public class FileSystemMonitoringTests
    {
        private static IPathMapper CreatePathMapper()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
                ? new WindowsPathMapper() 
                : new UnixPathMapper();
        }

        [Fact(DisplayName = "FSM-001: CreateMonitoringSessionAsync returns valid session")]
        public async Task FSM001()
        {
            // Arrange
            var pathMapper = CreatePathMapper();
            var fileSystem = new LocalFileSystem(pathMapper);
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                var canonicalPathResult = pathMapper.MapToCanonicalPath(tempDir);
                canonicalPathResult.IsSuccess.Should().BeTrue();
                var options = MonitoringOptions.ForPath(canonicalPathResult.Value);

                // Act
                var result = await fileSystem.CreateMonitoringSessionAsync(options);

                // Assert
                result.IsSuccess.Should().BeTrue();
                result.Value.Should().NotBeNull();
                result.Value.Should().BeAssignableTo<IMonitoringSession>();
                
                // Clean up
                result.Value.Dispose();
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact(DisplayName = "FSM-002: Session detects file creation")]
        public async Task FSM002()
        {
            // Arrange
            var pathMapper = CreatePathMapper();
            var fileSystem = new LocalFileSystem(pathMapper);
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                var canonicalPathResult = pathMapper.MapToCanonicalPath(tempDir);
                canonicalPathResult.IsSuccess.Should().BeTrue();
                var options = MonitoringOptions.ForPath(canonicalPathResult.Value);
                var sessionResult = await fileSystem.CreateMonitoringSessionAsync(options);
                sessionResult.IsSuccess.Should().BeTrue();
                
                using var session = sessionResult.Value;
                var events = session.Changes.Take(1).Timeout(TimeSpan.FromSeconds(5));
                var eventReceived = false;
                FileSystemChangedEvent? receivedEvent = null;

                events.Subscribe(evt =>
                {
                    receivedEvent = evt;
                    eventReceived = true;
                });

                // Give the monitor time to start
                await Task.Delay(100);

                // Act
                var testFile = Path.Combine(tempDir, "test.txt");
                await File.WriteAllTextAsync(testFile, "test content");

                // Wait for event
                var timeout = DateTime.UtcNow.AddSeconds(3);
                while (!eventReceived && DateTime.UtcNow < timeout)
                {
                    await Task.Delay(50);
                }

                // Assert
                eventReceived.Should().BeTrue("file creation should trigger an event");
                receivedEvent.Should().NotBeNull();
                receivedEvent.Value.ChangeType.Should().Be(FileSystemChangeType.Created);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact(DisplayName = "FSM-003: Session can be stopped")]
        public async Task FSM003()
        {
            // Arrange
            var pathMapper = CreatePathMapper();
            var fileSystem = new LocalFileSystem(pathMapper);
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                var canonicalPathResult = pathMapper.MapToCanonicalPath(tempDir);
                canonicalPathResult.IsSuccess.Should().BeTrue();
                var options = MonitoringOptions.ForPath(canonicalPathResult.Value);
                var sessionResult = await fileSystem.CreateMonitoringSessionAsync(options);
                sessionResult.IsSuccess.Should().BeTrue();
                
                using var session = sessionResult.Value;
                session.IsActive.Should().BeTrue();

                // Act
                await session.StopAsync();

                // Assert
                session.IsActive.Should().BeFalse();
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact(DisplayName = "FSM-004: Disposing a session while watcher events are in flight does not kill the process")]
        public async Task FSM004()
        {
            // FileSystemWatcher raises its events on thread pool threads and Dispose does not wait
            // for the ones already running. If a publication reaches the subject after Dispose tore
            // it down, the ObjectDisposedException is raised on a thread pool thread with no
            // handler above it - which does not fail a test, it takes the whole process out. So the
            // assertion here is simply "we got to the end": with the guard removed this test aborts
            // the run instead of failing it.
            var pathMapper = CreatePathMapper();
            var fileSystem = new LocalFileSystem(pathMapper);
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            using var churnCts = new CancellationTokenSource();
            var churn = Task.Run(async () =>
            {
                var counter = 0;
                while (!churnCts.IsCancellationRequested)
                {
                    var file = Path.Combine(tempDir, $"churn{counter++ % 64}.txt");
                    try
                    {
                        await File.WriteAllTextAsync(file, counter.ToString(), churnCts.Token);
                        File.Delete(file);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (IOException)
                    {
                        // The directory is being hammered; losing a write is fine, the point is churn.
                    }
                }
            });

            var errorsAfterDispose = 0;

            try
            {
                for (var i = 0; i < 40; i++)
                {
                    var canonicalPathResult = pathMapper.MapToCanonicalPath(tempDir);
                    canonicalPathResult.IsSuccess.Should().BeTrue();
                    var sessionResult = await fileSystem.CreateMonitoringSessionAsync(
                        MonitoringOptions.ForPath(canonicalPathResult.Value));
                    sessionResult.IsSuccess.Should().BeTrue();

                    var session = sessionResult.Value;
                    var disposed = false;
                    using var subscription = session.Changes.Subscribe(
                        _ => { },
                        _ => { if (Volatile.Read(ref disposed)) Interlocked.Increment(ref errorsAfterDispose); });

                    // Let a batch of watcher callbacks get under way before pulling the rug out.
                    await Task.Delay(15);

                    Volatile.Write(ref disposed, true);
                    session.Dispose();
                }
            }
            finally
            {
                churnCts.Cancel();
                await churn;

                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }

            errorsAfterDispose.Should().Be(0, "a disposed session must stop publishing, not fault its subscribers");
        }
    }
}