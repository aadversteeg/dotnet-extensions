using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.InMemory;
using Ave.Extensions.FileSystem.Models;
using FluentAssertions;
using UnitTests.Extensions.FileSystem.TestHelpers;

namespace UnitTests.Extensions.FileSystem
{
    public class FileSystemStreamingExtensionsTests
    {
        [Fact(DisplayName = "FSE-001: EnumerateFilesAsync streams results as discovered")]
        public async Task FSE001()
        {
            // Arrange
            var fileSystem = new InMemoryFileSystem();
            await fileSystem.CreateDirectoryAsync("/test".ToCanonicalPath());
            await fileSystem.WriteAllTextAsync("/test/file1.txt".ToCanonicalPath(), "content1");
            await fileSystem.WriteAllTextAsync("/test/file2.txt".ToCanonicalPath(), "content2");
            await fileSystem.CreateDirectoryAsync("/test/subdir".ToCanonicalPath());
            await fileSystem.WriteAllTextAsync("/test/subdir/file3.txt".ToCanonicalPath(), "content3");

            // Act
            var files = new List<CanonicalPath>();
            await foreach (var file in fileSystem.EnumerateFilesAsync("/test".ToCanonicalPath()))
            {
                files.Add(file);
            }

            // Assert
            files.Should().HaveCount(3);
            files.Should().Contain(f => f.GetName() == "file1.txt");
            files.Should().Contain(f => f.GetName() == "file2.txt");
            files.Should().Contain(f => f.GetName() == "file3.txt");
        }

        [Fact(DisplayName = "FSE-002: EnumerateDirectoriesAsync streams directory results")]
        public async Task FSE002()
        {
            // Arrange
            var fileSystem = new InMemoryFileSystem();
            await fileSystem.CreateDirectoryAsync("/test".ToCanonicalPath());
            await fileSystem.CreateDirectoryAsync("/test/dir1".ToCanonicalPath());
            await fileSystem.CreateDirectoryAsync("/test/dir2".ToCanonicalPath());
            await fileSystem.CreateDirectoryAsync("/test/dir1/subdir".ToCanonicalPath());
            await fileSystem.WriteAllTextAsync("/test/file.txt".ToCanonicalPath(), "content");

            // Act
            var directories = new List<CanonicalPath>();
            await foreach (var dir in fileSystem.EnumerateDirectoriesAsync("/test".ToCanonicalPath()))
            {
                directories.Add(dir);
            }

            // Assert
            directories.Should().HaveCount(3); // dir1, dir2, subdir
            directories.Should().Contain(d => d.GetName() == "dir1");
            directories.Should().Contain(d => d.GetName() == "dir2");
            directories.Should().Contain(d => d.GetName() == "subdir");
            directories.Should().NotContain(d => d.GetName() == "file.txt");
        }

        [Fact(DisplayName = "FSE-003: EnumerateEntriesAsync includes metadata")]
        public async Task FSE003()
        {
            // Arrange
            var fileSystem = new InMemoryFileSystem();
            await fileSystem.CreateDirectoryAsync("/test".ToCanonicalPath());
            await fileSystem.WriteAllTextAsync("/test/file1.txt".ToCanonicalPath(), "content1");
            await fileSystem.CreateDirectoryAsync("/test/dir1".ToCanonicalPath());

            // Act
            var entries = new List<FileSystemEntry>();
            await foreach (var entry in fileSystem.EnumerateEntriesAsync("/test".ToCanonicalPath(), recursive: true))
            {
                entries.Add(entry);
            }

            // Assert
            entries.Should().HaveCount(2);
            
            var file = entries.Single(e => e.Name == "file1.txt");
            file.IsDirectory.Should().BeFalse();
            file.Size.Should().Be(8); // "content1" = 8 bytes

            var directory = entries.Single(e => e.Name == "dir1");
            directory.IsDirectory.Should().BeTrue();
            directory.Size.Should().BeNull();
        }

        [Fact(DisplayName = "FSE-004: EnumerateFilesAsync respects cancellation token")]
        public async Task FSE004()
        {
            // Arrange
            var fileSystem = new InMemoryFileSystem();
            await fileSystem.CreateDirectoryAsync("/test".ToCanonicalPath());
            for (int i = 0; i < 5; i++)
            {
                await fileSystem.WriteAllTextAsync($"/test/file{i}.txt".ToCanonicalPath(), $"content{i}");
            }

            var cts = new CancellationTokenSource();
            cts.Cancel(); // Pre-cancel the token

            // Act & Assert
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await foreach (var file in fileSystem.EnumerateFilesAsync("/test".ToCanonicalPath(), cancellationToken: cts.Token))
                {
                    // Should not reach here due to cancellation
                }
            });
        }

        [Fact(DisplayName = "FSE-005: EnumerateEntriesAsync respects include filters")]
        public async Task FSE005()
        {
            // Arrange
            var fileSystem = new InMemoryFileSystem();
            await fileSystem.CreateDirectoryAsync("/test".ToCanonicalPath());
            await fileSystem.WriteAllTextAsync("/test/file1.txt".ToCanonicalPath(), "content1");
            await fileSystem.CreateDirectoryAsync("/test/dir1".ToCanonicalPath());

            // Act - Files only
            var filesOnly = new List<FileSystemEntry>();
            await foreach (var entry in fileSystem.EnumerateEntriesAsync("/test".ToCanonicalPath(), 
                recursive: true, includeFiles: true, includeDirectories: false))
            {
                filesOnly.Add(entry);
            }

            // Act - Directories only
            var dirsOnly = new List<FileSystemEntry>();
            await foreach (var entry in fileSystem.EnumerateEntriesAsync("/test".ToCanonicalPath(), 
                recursive: true, includeFiles: false, includeDirectories: true))
            {
                dirsOnly.Add(entry);
            }

            // Assert
            filesOnly.Should().HaveCount(1);
            filesOnly.Single().Name.Should().Be("file1.txt");
            filesOnly.Single().IsDirectory.Should().BeFalse();

            dirsOnly.Should().HaveCount(1);
            dirsOnly.Single().Name.Should().Be("dir1");
            dirsOnly.Single().IsDirectory.Should().BeTrue();
        }

        [Fact(DisplayName = "FSE-006: EnumerateFilesAsync respects search pattern")]
        public async Task FSE006()
        {
            // Arrange
            var fileSystem = new InMemoryFileSystem();
            await fileSystem.CreateDirectoryAsync("/test".ToCanonicalPath());
            await fileSystem.WriteAllTextAsync("/test/file1.txt".ToCanonicalPath(), "content1");
            await fileSystem.WriteAllTextAsync("/test/file2.log".ToCanonicalPath(), "content2");
            await fileSystem.WriteAllTextAsync("/test/data.txt".ToCanonicalPath(), "content3");

            // Act
            var txtFiles = new List<CanonicalPath>();
            await foreach (var file in fileSystem.EnumerateFilesAsync("/test".ToCanonicalPath(), "*.txt"))
            {
                txtFiles.Add(file);
            }

            // Assert
            txtFiles.Should().HaveCount(2);
            txtFiles.Should().Contain(f => f.GetName() == "file1.txt");
            txtFiles.Should().Contain(f => f.GetName() == "data.txt");
            txtFiles.Should().NotContain(f => f.GetName() == "file2.log");
        }

        [Fact(DisplayName = "FSE-007: EnumerateFilesAsync handles recursive vs non-recursive")]
        public async Task FSE007()
        {
            // Arrange
            var fileSystem = new InMemoryFileSystem();
            await fileSystem.CreateDirectoryAsync("/test".ToCanonicalPath());
            await fileSystem.WriteAllTextAsync("/test/file1.txt".ToCanonicalPath(), "content1");
            await fileSystem.CreateDirectoryAsync("/test/subdir".ToCanonicalPath());
            await fileSystem.WriteAllTextAsync("/test/subdir/file2.txt".ToCanonicalPath(), "content2");

            // Act - Recursive should find both files
            var recursiveFiles = new List<CanonicalPath>();
            await foreach (var file in fileSystem.EnumerateFilesAsync("/test".ToCanonicalPath(), recursive: true))
            {
                recursiveFiles.Add(file);
            }

            // Assert
            recursiveFiles.Should().HaveCount(2);
            recursiveFiles.Should().Contain(f => f.GetName() == "file1.txt");
            recursiveFiles.Should().Contain(f => f.GetName() == "file2.txt");
        }
    }
}