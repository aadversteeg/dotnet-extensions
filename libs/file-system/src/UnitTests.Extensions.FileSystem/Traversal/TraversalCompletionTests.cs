using System;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.InMemory;
using Ave.Extensions.FileSystem.Mapping;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Traversal;
using Ave.Extensions.Functional;
using FluentAssertions;
using UnitTests.Extensions.FileSystem.TestHelpers;

namespace UnitTests.Extensions.FileSystem.Traversal
{
    /// <summary>
    /// OnTraversalCompleteAsync is a lifecycle contract, not a success callback: the streaming
    /// handlers behind EnumerateEntriesAsync/EnumerateFilesAsync hold their consumer open until it
    /// fires, so a traversal that skips it on a failure exit leaves that consumer waiting forever.
    /// These tests pin the contract at the file system level, where the guarantee lives.
    /// </summary>
    public class TraversalCompletionTests
    {
        [Fact(DisplayName = "TCP-001: Completion is reported when the starting entry cannot be resolved")]
        public async Task TCP001()
        {
            // Arrange - the field case: the directory is gone by the time the traversal starts.
            var fileSystem = new InMemoryFileSystem();
            var handler = new RecordingHandler();

            // Act
            var result = await fileSystem.TraverseAsync(
                "/does-not-exist".ToCanonicalPath(), handler, IdentityMapper.Instance);

            // Assert
            result.IsFailure.Should().BeTrue();
            handler.CompleteCalls.Should().Be(1, "a failed traversal still has to close its handler");
        }

        [Fact(DisplayName = "TCP-002: Completion is reported when the traversal fails part-way through")]
        public async Task TCP002()
        {
            // Arrange
            var fileSystem = await CreateTreeAsync();
            var handler = new RecordingHandler { FailOnFileNumber = 2 };

            // Act
            var result = await fileSystem.TraverseAsync(
                "/test".ToCanonicalPath(), handler, IdentityMapper.Instance);

            // Assert
            result.IsFailure.Should().BeTrue();
            handler.FilesSeen.Should().Be(2, "the traversal should stop at the failing entry");
            handler.CompleteCalls.Should().Be(1);
        }

        [Fact(DisplayName = "TCP-003: Completion is reported when a handler throws")]
        public async Task TCP003()
        {
            // Arrange
            var fileSystem = await CreateTreeAsync();
            var handler = new RecordingHandler { ThrowOnFileNumber = 1 };

            // Act
            var result = await fileSystem.TraverseAsync(
                "/test".ToCanonicalPath(), handler, IdentityMapper.Instance);

            // Assert
            result.IsFailure.Should().BeTrue();
            handler.CompleteCalls.Should().Be(1);
        }

        [Fact(DisplayName = "TCP-004: Completion is reported when the start callback itself fails")]
        public async Task TCP004()
        {
            // Arrange
            var fileSystem = await CreateTreeAsync();
            var handler = new RecordingHandler { FailOnStart = true };

            // Act
            var result = await fileSystem.TraverseAsync(
                "/test".ToCanonicalPath(), handler, IdentityMapper.Instance);

            // Assert
            result.IsFailure.Should().BeTrue();
            handler.CompleteCalls.Should().Be(1, "a handler that refused the start still has to be closed");
        }

        [Fact(DisplayName = "TCP-005: Completion is reported exactly once on the success path")]
        public async Task TCP005()
        {
            // Arrange
            var fileSystem = await CreateTreeAsync();
            var handler = new RecordingHandler();

            // Act
            var result = await fileSystem.TraverseAsync(
                "/test".ToCanonicalPath(), handler, IdentityMapper.Instance);

            // Assert - the guarantee must not turn into a second notification.
            result.IsSuccess.Should().BeTrue();
            handler.CompleteCalls.Should().Be(1);
            handler.LastSummary.FilesProcessed.Should().Be(3);
        }

        [Fact(DisplayName = "TCP-006: Completion is reported when the traversal is cancelled")]
        public async Task TCP006()
        {
            // Arrange
            var fileSystem = await CreateTreeAsync();
            var handler = new RecordingHandler { CancelOnFileNumber = 1 };

            // Act
            var act = async () => await fileSystem.TraverseAsync(
                "/test".ToCanonicalPath(), handler, IdentityMapper.Instance);

            // Assert - cancellation is exactly the case that must still release the consumer.
            await act.Should().ThrowAsync<OperationCanceledException>();
            handler.CompleteCalls.Should().Be(1);
        }

        private static async Task<InMemoryFileSystem> CreateTreeAsync()
        {
            var fileSystem = new InMemoryFileSystem();
            await fileSystem.CreateDirectoryAsync("/test".ToCanonicalPath());
            await fileSystem.WriteAllTextAsync("/test/file1.txt".ToCanonicalPath(), "one");
            await fileSystem.WriteAllTextAsync("/test/file2.txt".ToCanonicalPath(), "two");
            await fileSystem.CreateDirectoryAsync("/test/sub".ToCanonicalPath());
            await fileSystem.WriteAllTextAsync("/test/sub/file3.txt".ToCanonicalPath(), "three");
            return fileSystem;
        }

        /// <summary>
        /// Records the lifecycle callbacks it receives and can be told to fail, throw or cancel at a
        /// chosen file, so each exit path out of TraverseAsync can be driven from a test.
        /// </summary>
        private sealed class RecordingHandler : ITraversalHandler<FileSystemEntry>
        {
            public bool FailOnStart { get; init; }

            public int? FailOnFileNumber { get; init; }

            public int? ThrowOnFileNumber { get; init; }

            public int? CancelOnFileNumber { get; init; }

            public int FilesSeen { get; private set; }

            public int CompleteCalls { get; private set; }

            public TraversalSummary LastSummary { get; private set; }

            public Task<Result<Unit, Error>> OnTraversalStartAsync(
                CanonicalPath rootPath,
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult(FailOnStart
                    ? Result<Unit, Error>.Failure(new Error(FileSystemErrorCodes.Unknown, "start refused"))
                    : Result<Unit, Error>.Success(Unit.Value));
            }

            public Task<Result<TraversalControl, Error>> OnDirectoryEnterAsync(
                FileSystemEntry directory,
                TraversalContext context,
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult(Result<TraversalControl, Error>.Success(TraversalControl.Continue));
            }

            public Task<Result<TraversalControl, Error>> OnFileAsync(
                FileSystemEntry file,
                TraversalContext context,
                CancellationToken cancellationToken = default)
            {
                FilesSeen++;

                if (ThrowOnFileNumber == FilesSeen)
                    throw new InvalidOperationException("handler blew up");

                if (CancelOnFileNumber == FilesSeen)
                    throw new OperationCanceledException();

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
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult(Result<TraversalControl, Error>.Success(TraversalControl.Continue));
            }

            public Task<Result<Unit, Error>> OnTraversalCompleteAsync(
                TraversalSummary summary,
                CancellationToken cancellationToken = default)
            {
                CompleteCalls++;
                LastSummary = summary;
                return Task.FromResult(Result<Unit, Error>.Success(Unit.Value));
            }
        }
    }
}

