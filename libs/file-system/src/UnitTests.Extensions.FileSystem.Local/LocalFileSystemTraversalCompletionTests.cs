using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Local;
using Ave.Extensions.FileSystem.Local.PathMappers;
using Ave.Extensions.FileSystem.Mapping;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Traversal;
using Ave.Extensions.Functional;
using FluentAssertions;
using UnitTests.Extensions.FileSystem.Local.TestHelpers;

namespace UnitTests.Extensions.FileSystem.Local
{
    /// <summary>
    /// LocalFileSystem is the implementation the file cache walks a real tree with, and the one
    /// that met the failing exits in the field: a directory deleted by build churn between the
    /// listing and the traversal. Completion has to be reported on those exits too, because the
    /// streaming handler behind EnumerateEntriesAsync waits for nothing else.
    /// </summary>
    public class LocalFileSystemTraversalCompletionTests
    {
        private static IPathMapper CreatePathMapper() =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? new WindowsPathMapper()
                : new UnixPathMapper();

        [Fact(DisplayName = "LFC-001: Completion is reported when the directory is gone")]
        public async Task LFC001()
        {
            // Arrange
            var pathMapper = CreatePathMapper();
            var fileSystem = new LocalFileSystem(pathMapper);
            var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            var canonical = pathMapper.MapToCanonicalPath(missing);
            canonical.IsSuccess.Should().BeTrue();
            var handler = new RecordingHandler();

            // Act
            var result = await fileSystem.TraverseAsync(canonical.Value, handler, IdentityMapper.Instance);

            // Assert
            result.IsFailure.Should().BeTrue();
            handler.CompleteCalls.Should().Be(1, "a failed traversal still has to close its handler");
        }

        [Fact(DisplayName = "LFC-002: Completion is reported when the traversal fails part-way through")]
        public async Task LFC002()
        {
            // Arrange
            var pathMapper = CreatePathMapper();
            var fileSystem = new LocalFileSystem(pathMapper);
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                await File.WriteAllTextAsync(Path.Combine(tempDir, "one.txt"), "one");
                await File.WriteAllTextAsync(Path.Combine(tempDir, "two.txt"), "two");

                var canonical = pathMapper.MapToCanonicalPath(tempDir);
                canonical.IsSuccess.Should().BeTrue();
                var handler = new RecordingHandler { FailOnFileNumber = 1 };

                // Act
                var result = await fileSystem.TraverseAsync(canonical.Value, handler, IdentityMapper.Instance);

                // Assert
                result.IsFailure.Should().BeTrue();
                handler.CompleteCalls.Should().Be(1);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact(DisplayName = "LFC-003: Completion is reported exactly once on the success path")]
        public async Task LFC003()
        {
            // Arrange
            var pathMapper = CreatePathMapper();
            var fileSystem = new LocalFileSystem(pathMapper);
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                await File.WriteAllTextAsync(Path.Combine(tempDir, "one.txt"), "one");

                var canonical = pathMapper.MapToCanonicalPath(tempDir);
                canonical.IsSuccess.Should().BeTrue();
                var handler = new RecordingHandler();

                // Act
                var result = await fileSystem.TraverseAsync(canonical.Value, handler, IdentityMapper.Instance);

                // Assert - the guarantee must not turn into a second notification.
                result.IsSuccess.Should().BeTrue();
                handler.CompleteCalls.Should().Be(1);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        private sealed class RecordingHandler : ITraversalHandler<FileSystemEntry>
        {
            public int? FailOnFileNumber { get; init; }

            public int FilesSeen { get; private set; }

            public int CompleteCalls { get; private set; }

            public Task<Result<Unit, Error>> OnTraversalStartAsync(
                CanonicalPath rootPath,
                CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<Unit, Error>.Success(Unit.Value));

            public Task<Result<TraversalControl, Error>> OnDirectoryEnterAsync(
                FileSystemEntry directory,
                TraversalContext context,
                CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<TraversalControl, Error>.Success(TraversalControl.Continue));

            public Task<Result<TraversalControl, Error>> OnFileAsync(
                FileSystemEntry file,
                TraversalContext context,
                CancellationToken cancellationToken = default)
            {
                FilesSeen++;

                if (FailOnFileNumber == FilesSeen)
                {
                    return Task.FromResult(Result<TraversalControl, Error>.Failure(
                        new Error(FileSystemErrorCodes.Unknown, "file refused")));
                }

                return Task.FromResult(Result<TraversalControl, Error>.Success(TraversalControl.Continue));
            }

            public Task<Result<TraversalControl, Error>> OnDirectoryAsync(
                FileSystemEntry directory,
                TraversalContext context,
                CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<TraversalControl, Error>.Success(TraversalControl.Continue));

            public Task<Result<Unit, Error>> OnTraversalCompleteAsync(
                TraversalSummary summary,
                CancellationToken cancellationToken = default)
            {
                CompleteCalls++;
                return Task.FromResult(Result<Unit, Error>.Success(Unit.Value));
            }
        }
    }
}

