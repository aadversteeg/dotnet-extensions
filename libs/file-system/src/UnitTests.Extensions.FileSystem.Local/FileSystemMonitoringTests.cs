using System;
using System.IO;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Local;
using Ave.Extensions.FileSystem.Local.PathMappers;
using Ave.Extensions.FileSystem.Models;
using FluentAssertions;
using UnitTests.Extensions.FileSystem.Local.TestHelpers;

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

    }
}