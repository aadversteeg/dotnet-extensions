using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Traversal;
using Ave.Extensions.FileSystem.Traversal.Handlers;
using Ave.Extensions.Functional;
using FluentAssertions;
using UnitTests.Extensions.FileSystem.TestHelpers;
using Xunit;

namespace UnitTests.Extensions.FileSystem.Traversal.Handlers
{
    public class PathCollectorHandlerTests
    {
        [Fact(DisplayName = "PCH-001: Constructor initializes with search pattern and filters")]
        public void PCH001()
        {
            // Arrange & Act
            var handler = new PathCollectorHandler("*.txt", includeFiles: true, includeDirectories: false);

            // Assert
            handler.Should().NotBeNull();
            handler.CollectedPaths.Should().NotBeNull();
            handler.CollectedPaths.Should().BeEmpty();
        }

        [Fact(DisplayName = "PCH-002: OnTraversalStartAsync returns success")]
        public async Task PCH002()
        {
            // Arrange
            var handler = new PathCollectorHandler("*", includeFiles: true, includeDirectories: true);
            var rootPath = "/test".ToCanonicalPath();

            // Act
            var result = await handler.OnTraversalStartAsync(rootPath);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().Be(Unit.Value);
        }

        [Fact(DisplayName = "PCH-003: OnFileAsync collects matching files when includeFiles is true")]
        public async Task PCH003()
        {
            // Arrange
            var handler = new PathCollectorHandler("*.txt", includeFiles: true, includeDirectories: false);
            var fileEntry = new FileEntry(
                path: "/test/file.txt".ToCanonicalPath(),
                info: new Ave.Extensions.FileSystem.Models.FileInfo(
                    new EntryInfo("file.txt", DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, FileAttributes.Normal),
                    1024,
                    ".txt"));
            var context = TraversalContext.CreateInitial("/test".ToCanonicalPath());

            // Act
            var result = await handler.OnFileAsync(fileEntry, context);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Action.Should().Be(TraversalAction.Continue);
            handler.CollectedPaths.Should().Contain("/test/file.txt".ToCanonicalPath());
        }

        [Fact(DisplayName = "PCH-004: OnFileAsync skips non-matching files")]
        public async Task PCH004()
        {
            // Arrange
            var handler = new PathCollectorHandler("*.txt", includeFiles: true, includeDirectories: false);
            var fileEntry = new FileEntry(
                path: "/test/file.doc".ToCanonicalPath(),
                info: new Ave.Extensions.FileSystem.Models.FileInfo(
                    new EntryInfo("file.doc", DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, FileAttributes.Normal),
                    1024,
                    ".doc"));
            var context = TraversalContext.CreateInitial("/test".ToCanonicalPath());

            // Act
            var result = await handler.OnFileAsync(fileEntry, context);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Action.Should().Be(TraversalAction.Continue);
            handler.CollectedPaths.Should().BeEmpty();
        }

        [Fact(DisplayName = "PCH-005: OnFileAsync ignores files when includeFiles is false")]
        public async Task PCH005()
        {
            // Arrange
            var handler = new PathCollectorHandler("*.txt", includeFiles: false, includeDirectories: true);
            var fileEntry = new FileEntry(
                path: "/test/file.txt".ToCanonicalPath(),
                info: new Ave.Extensions.FileSystem.Models.FileInfo(
                    new EntryInfo("file.txt", DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, FileAttributes.Normal),
                    1024,
                    ".txt"));
            var context = TraversalContext.CreateInitial("/test".ToCanonicalPath());

            // Act
            var result = await handler.OnFileAsync(fileEntry, context);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Action.Should().Be(TraversalAction.Continue);
            handler.CollectedPaths.Should().BeEmpty();
        }

        [Fact(DisplayName = "PCH-006: OnDirectoryAsync collects matching directories when includeDirectories is true")]
        public async Task PCH006()
        {
            // Arrange
            var handler = new PathCollectorHandler("test*", includeFiles: false, includeDirectories: true);
            var directoryEntry = new DirectoryEntry(
                path: "/root/testdir".ToCanonicalPath(),
                info: new Ave.Extensions.FileSystem.Models.DirectoryInfo(
                    new EntryInfo("testdir", DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, FileAttributes.Directory)));
            var context = TraversalContext.CreateInitial("/root".ToCanonicalPath()).NextLevel("/root/testdir".ToCanonicalPath()); // Depth > 0 to include directory

            // Act
            var result = await handler.OnDirectoryAsync(directoryEntry, context);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Action.Should().Be(TraversalAction.Continue);
            handler.CollectedPaths.Should().Contain("/root/testdir".ToCanonicalPath());
        }

        [Fact(DisplayName = "PCH-007: OnDirectoryAsync skips root directory at depth 0")]
        public async Task PCH007()
        {
            // Arrange
            var handler = new PathCollectorHandler("*", includeFiles: false, includeDirectories: true);
            var directoryEntry = new DirectoryEntry(
                path: "/root".ToCanonicalPath(),
                info: new Ave.Extensions.FileSystem.Models.DirectoryInfo(
                    new EntryInfo("root", DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, FileAttributes.Directory)));
            var context = TraversalContext.CreateInitial("/root".ToCanonicalPath()); // Depth 0 = root directory

            // Act
            var result = await handler.OnDirectoryAsync(directoryEntry, context);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Action.Should().Be(TraversalAction.Continue);
            handler.CollectedPaths.Should().BeEmpty(); // Root directory should not be collected
        }

        [Fact(DisplayName = "PCH-008: OnDirectoryAsync ignores directories when includeDirectories is false")]
        public async Task PCH008()
        {
            // Arrange
            var handler = new PathCollectorHandler("*", includeFiles: true, includeDirectories: false);
            var directoryEntry = new DirectoryEntry(
                path: "/root/subdir".ToCanonicalPath(),
                info: new Ave.Extensions.FileSystem.Models.DirectoryInfo(
                    new EntryInfo("subdir", DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, FileAttributes.Directory)));
            var context = TraversalContext.CreateInitial("/root".ToCanonicalPath()).NextLevel("/root/subdir".ToCanonicalPath());

            // Act
            var result = await handler.OnDirectoryAsync(directoryEntry, context);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Action.Should().Be(TraversalAction.Continue);
            handler.CollectedPaths.Should().BeEmpty();
        }

        [Fact(DisplayName = "PCH-009: OnTraversalCompleteAsync returns success")]
        public async Task PCH009()
        {
            // Arrange
            var handler = new PathCollectorHandler("*", includeFiles: true, includeDirectories: true);
            var summary = new TraversalSummary(10, 5, 0, TimeSpan.FromSeconds(1), false);

            // Act
            var result = await handler.OnTraversalCompleteAsync(summary);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().Be(Unit.Value);
        }

        [Fact(DisplayName = "PCH-010: CollectedPaths returns read-only list")]
        public void PCH010()
        {
            // Arrange
            var handler = new PathCollectorHandler("*", includeFiles: true, includeDirectories: true);

            // Act
            var collectedPaths = handler.CollectedPaths;

            // Assert
            collectedPaths.Should().NotBeNull();
            collectedPaths.Should().BeAssignableTo<IReadOnlyList<CanonicalPath>>();
            
            // Should not be able to cast to mutable list
            collectedPaths.Should().NotBeAssignableTo<List<CanonicalPath>>();
        }

        [Fact(DisplayName = "PCH-011: Wildcard pattern matches all files")]
        public async Task PCH011()
        {
            // Arrange
            var handler = new PathCollectorHandler("*", includeFiles: true, includeDirectories: false);
            var entries = new[]
            {
                new FileEntry("/test/file1.txt".ToCanonicalPath(), new Ave.Extensions.FileSystem.Models.FileInfo(new EntryInfo("file1.txt", DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, FileAttributes.Normal), 100, ".txt")),
                new FileEntry("/test/file2.doc".ToCanonicalPath(), new Ave.Extensions.FileSystem.Models.FileInfo(new EntryInfo("file2.doc", DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, FileAttributes.Normal), 200, ".doc")),
                new FileEntry("/test/readme".ToCanonicalPath(), new Ave.Extensions.FileSystem.Models.FileInfo(new EntryInfo("readme", DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, FileAttributes.Normal), 50, null))
            };
            var context = TraversalContext.CreateInitial("/test".ToCanonicalPath());

            // Act
            foreach (var entry in entries)
            {
                await handler.OnFileAsync(entry, context);
            }

            // Assert
            handler.CollectedPaths.Should().HaveCount(3);
            handler.CollectedPaths.Should().Contain("/test/file1.txt".ToCanonicalPath());
            handler.CollectedPaths.Should().Contain("/test/file2.doc".ToCanonicalPath());
            handler.CollectedPaths.Should().Contain("/test/readme".ToCanonicalPath());
        }
    }
}