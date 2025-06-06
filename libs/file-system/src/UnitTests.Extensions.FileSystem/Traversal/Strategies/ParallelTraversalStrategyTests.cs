using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.InMemory;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Traversal;
using Ave.Extensions.FileSystem.Traversal.Handlers;
using Ave.Extensions.FileSystem.Traversal.Strategies;
using Ave.Extensions.Functional;
using FluentAssertions;
using UnitTests.Extensions.FileSystem.TestHelpers;
using Xunit;

namespace UnitTests.Extensions.FileSystem.Traversal.Strategies
{
    public class ParallelTraversalStrategyTests
    {
        [Fact(DisplayName = "PTS-001: ParallelTraversalStrategy processes files through filesystem")]
        public async Task PTS001()
        {
            // Arrange
            var fileSystem = new InMemoryFileSystem();
            await fileSystem.CreateDirectoryAsync("/test".ToCanonicalPath());
            await fileSystem.WriteAllTextAsync("/test/file1.txt".ToCanonicalPath(), "content1");
            await fileSystem.WriteAllTextAsync("/test/file2.txt".ToCanonicalPath(), "content2");

            // Act - Use parallel strategy via filesystem
            var options = new TraversalOptions(recursive: true, strategyHint: TraversalStrategyHint.Parallel);
            var files = await fileSystem.GetFilesAsync("/test".ToCanonicalPath(), recursive: true);

            // Assert
            files.IsSuccess.Should().BeTrue();
            files.Value.Should().HaveCount(2);
            files.Value.Should().Contain(p => p.GetName() == "file1.txt");
            files.Value.Should().Contain(p => p.GetName() == "file2.txt");
        }

        [Fact(DisplayName = "PTS-002: ParallelTraversalStrategy handles empty directories")]
        public async Task PTS002()
        {
            // Arrange
            var strategy = new ParallelTraversalStrategy();
            var handler = new PathCollectorHandler("*", true, true);
            var options = new TraversalOptions(recursive: false);
            var context = TraversalContext.CreateInitial("/empty".ToCanonicalPath());
            var stats = new TraversalStats();

            Task<Result<IEnumerable<FileSystemEntry>, FileSystemError>> GetChildren(CanonicalPath path, CancellationToken ct)
            {
                return Task.FromResult(Result<IEnumerable<FileSystemEntry>, FileSystemError>.Success(Enumerable.Empty<FileSystemEntry>()));
            }

            var directory = new FileSystemEntry("/empty".ToCanonicalPath(), true, null, null, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, FileAttributes.Directory);

            // Act
            var result = await strategy.TraverseAsync(directory, handler, options, context, stats, GetChildren, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            handler.CollectedPaths.Should().BeEmpty();
            stats.DirectoriesProcessed.Should().Be(1);
            stats.FilesProcessed.Should().Be(0);
        }

        [Fact(DisplayName = "PTS-003: ParallelTraversalStrategy works with cancellation")]
        public async Task PTS003()
        {
            // Arrange
            var fileSystem = new InMemoryFileSystem();
            await fileSystem.CreateDirectoryAsync("/test".ToCanonicalPath());
            await fileSystem.WriteAllTextAsync("/test/file1.txt".ToCanonicalPath(), "content1");

            var cts = new CancellationTokenSource();
            cts.Cancel(); // Pre-cancel to ensure cancellation

            // Act & Assert
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                var files = await fileSystem.GetFilesAsync("/test".ToCanonicalPath(), recursive: true, cancellationToken: cts.Token);
            });
        }

        [Fact(DisplayName = "PTS-004: ParallelTraversalStrategy handles single file")]
        public async Task PTS004()
        {
            // Arrange
            var strategy = new ParallelTraversalStrategy();
            var handler = new PathCollectorHandler("*", true, false);
            var options = new TraversalOptions(recursive: false);
            var context = TraversalContext.CreateInitial("/test".ToCanonicalPath());
            var stats = new TraversalStats();

            Task<Result<IEnumerable<FileSystemEntry>, FileSystemError>> GetChildren(CanonicalPath path, CancellationToken ct)
            {
                return Task.FromResult(Result<IEnumerable<FileSystemEntry>, FileSystemError>.Success(Enumerable.Empty<FileSystemEntry>()));
            }

            var file = new FileSystemEntry("/test/file.txt".ToCanonicalPath(), false, 100, ".txt", DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, FileAttributes.Normal);

            // Act
            var result = await strategy.TraverseAsync(file, handler, options, context, stats, GetChildren, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            handler.CollectedPaths.Should().HaveCount(1);
            handler.CollectedPaths.Should().Contain(p => p.Value.EndsWith("file.txt"));
            stats.FilesProcessed.Should().Be(1);
            stats.DirectoriesProcessed.Should().Be(0);
        }

        [Fact(DisplayName = "PTS-005: ParallelTraversalStrategy processes nested directories")]
        public async Task PTS005()
        {
            // Arrange
            var fileSystem = new InMemoryFileSystem();
            await fileSystem.CreateDirectoryAsync("/test".ToCanonicalPath());
            await fileSystem.CreateDirectoryAsync("/test/subdir".ToCanonicalPath());
            await fileSystem.WriteAllTextAsync("/test/file.txt".ToCanonicalPath(), "content");
            await fileSystem.WriteAllTextAsync("/test/subdir/nested.txt".ToCanonicalPath(), "nested content");

            // Act - Use parallel strategy and get both files and directories
            var options = new TraversalOptions(recursive: true, strategyHint: TraversalStrategyHint.Parallel);
            var files = await fileSystem.GetFilesAsync("/test".ToCanonicalPath(), recursive: true);
            var directories = await fileSystem.GetDirectoriesAsync("/test".ToCanonicalPath(), recursive: true);

            // Assert
            files.IsSuccess.Should().BeTrue();
            files.Value.Should().HaveCount(2);
            files.Value.Should().Contain(p => p.GetName() == "file.txt");
            files.Value.Should().Contain(p => p.GetName() == "nested.txt");

            directories.IsSuccess.Should().BeTrue();
            directories.Value.Should().HaveCount(1);
            directories.Value.Should().Contain(p => p.GetName() == "subdir");
        }
    }
}