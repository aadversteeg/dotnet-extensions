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

        [Fact(DisplayName = "PCH-003: HandleFileAsync collects matching files when includeFiles is true")]
        public async Task PCH003()
        {
            // Arrange
            var handler = new PathCollectorHandler("*.txt", includeFiles: true, includeDirectories: false);
            var fileEntry = new FileSystemEntry(
                path: "/test/file.txt".ToCanonicalPath(),
                isDirectory: false,
                size: 1024,
                extension: ".txt",
                createdUtc: DateTime.UtcNow,
                modifiedUtc: DateTime.UtcNow,
                lastAccessUtc: DateTime.UtcNow,
                attributes: FileAttributes.Normal);
            var context = TraversalContext.CreateInitial("/test".ToCanonicalPath());

            // Act
            var result = await handler.HandleFileAsync(fileEntry, context);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Action.Should().Be(TraversalAction.Continue);
            handler.CollectedPaths.Should().Contain("/test/file.txt".ToCanonicalPath());
        }

        [Fact(DisplayName = "PCH-004: HandleFileAsync skips non-matching files")]
        public async Task PCH004()
        {
            // Arrange
            var handler = new PathCollectorHandler("*.txt", includeFiles: true, includeDirectories: false);
            var fileEntry = new FileSystemEntry(
                path: "/test/file.doc".ToCanonicalPath(),
                isDirectory: false,
                size: 1024,
                extension: ".doc",
                createdUtc: DateTime.UtcNow,
                modifiedUtc: DateTime.UtcNow,
                lastAccessUtc: DateTime.UtcNow,
                attributes: FileAttributes.Normal);
            var context = TraversalContext.CreateInitial("/test".ToCanonicalPath());

            // Act
            var result = await handler.HandleFileAsync(fileEntry, context);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Action.Should().Be(TraversalAction.Continue);
            handler.CollectedPaths.Should().BeEmpty();
        }

        [Fact(DisplayName = "PCH-005: HandleFileAsync ignores files when includeFiles is false")]
        public async Task PCH005()
        {
            // Arrange
            var handler = new PathCollectorHandler("*.txt", includeFiles: false, includeDirectories: true);
            var fileEntry = new FileSystemEntry(
                path: "/test/file.txt".ToCanonicalPath(),
                isDirectory: false,
                size: 1024,
                extension: ".txt",
                createdUtc: DateTime.UtcNow,
                modifiedUtc: DateTime.UtcNow,
                lastAccessUtc: DateTime.UtcNow,
                attributes: FileAttributes.Normal);
            var context = TraversalContext.CreateInitial("/test".ToCanonicalPath());

            // Act
            var result = await handler.HandleFileAsync(fileEntry, context);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Action.Should().Be(TraversalAction.Continue);
            handler.CollectedPaths.Should().BeEmpty();
        }

        [Fact(DisplayName = "PCH-006: HandleDirectoryAsync collects matching directories when includeDirectories is true")]
        public async Task PCH006()
        {
            // Arrange
            var handler = new PathCollectorHandler("test*", includeFiles: false, includeDirectories: true);
            var directoryEntry = new FileSystemEntry(
                path: "/root/testdir".ToCanonicalPath(),
                isDirectory: true,
                size: null,
                extension: null,
                createdUtc: DateTime.UtcNow,
                modifiedUtc: DateTime.UtcNow,
                lastAccessUtc: DateTime.UtcNow,
                attributes: FileAttributes.Directory);
            var context = TraversalContext.CreateInitial("/root".ToCanonicalPath()).NextLevel("/root/testdir".ToCanonicalPath()); // Depth > 0 to include directory

            // Act
            var result = await handler.HandleDirectoryAsync(directoryEntry, context);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Action.Should().Be(TraversalAction.Continue);
            handler.CollectedPaths.Should().Contain("/root/testdir".ToCanonicalPath());
        }

        [Fact(DisplayName = "PCH-007: HandleDirectoryAsync skips root directory at depth 0")]
        public async Task PCH007()
        {
            // Arrange
            var handler = new PathCollectorHandler("*", includeFiles: false, includeDirectories: true);
            var directoryEntry = new FileSystemEntry(
                path: "/root".ToCanonicalPath(),
                isDirectory: true,
                size: null,
                extension: null,
                createdUtc: DateTime.UtcNow,
                modifiedUtc: DateTime.UtcNow,
                lastAccessUtc: DateTime.UtcNow,
                attributes: FileAttributes.Directory);
            var context = TraversalContext.CreateInitial("/root".ToCanonicalPath()); // Depth 0 = root directory

            // Act
            var result = await handler.HandleDirectoryAsync(directoryEntry, context);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Action.Should().Be(TraversalAction.Continue);
            handler.CollectedPaths.Should().BeEmpty(); // Root directory should not be collected
        }

        [Fact(DisplayName = "PCH-008: HandleDirectoryAsync ignores directories when includeDirectories is false")]
        public async Task PCH008()
        {
            // Arrange
            var handler = new PathCollectorHandler("*", includeFiles: true, includeDirectories: false);
            var directoryEntry = new FileSystemEntry(
                path: "/root/subdir".ToCanonicalPath(),
                isDirectory: true,
                size: null,
                extension: null,
                createdUtc: DateTime.UtcNow,
                modifiedUtc: DateTime.UtcNow,
                lastAccessUtc: DateTime.UtcNow,
                attributes: FileAttributes.Directory);
            var context = TraversalContext.CreateInitial("/root".ToCanonicalPath()).NextLevel("/root/subdir".ToCanonicalPath());

            // Act
            var result = await handler.HandleDirectoryAsync(directoryEntry, context);

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
                new FileSystemEntry("/test/file1.txt".ToCanonicalPath(), false, 100, ".txt", DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, FileAttributes.Normal),
                new FileSystemEntry("/test/file2.doc".ToCanonicalPath(), false, 200, ".doc", DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, FileAttributes.Normal),
                new FileSystemEntry("/test/readme".ToCanonicalPath(), false, 50, null, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, FileAttributes.Normal)
            };
            var context = TraversalContext.CreateInitial("/test".ToCanonicalPath());

            // Act
            foreach (var entry in entries)
            {
                await handler.HandleFileAsync(entry, context);
            }

            // Assert
            handler.CollectedPaths.Should().HaveCount(3);
            handler.CollectedPaths.Should().Contain("/test/file1.txt".ToCanonicalPath());
            handler.CollectedPaths.Should().Contain("/test/file2.doc".ToCanonicalPath());
            handler.CollectedPaths.Should().Contain("/test/readme".ToCanonicalPath());
        }
    }
}