using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Local;
using Ave.Extensions.FileSystem.Local.PathMappers;
using Ave.Extensions.FileSystem.Mapping;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.Functional;
using FluentAssertions;
using UnitTests.Extensions.FileSystem.Local.TestHelpers;
using Xunit;

namespace UnitTests.Extensions.FileSystem.Local.Mapping
{
    public class LocalFileSystemMappingTests : IDisposable
    {
        private readonly string _testDirectory;
        private readonly LocalFileSystem _fileSystem;

        public LocalFileSystemMappingTests()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), $"LocalFileSystemMappingTests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_testDirectory);
            // Create a mock path mapper that maps our test paths to the test directory
            var pathMapper = new TestPathMapper(_testDirectory);
            _fileSystem = new LocalFileSystem(pathMapper);
        }

        [Fact(DisplayName = "LFSM-001: GetEntryAsync with IdentityMapper returns same entry")]
        public async Task LFSM001()
        {
            // Arrange
            var testFile = Path.Combine(_testDirectory, "test.txt");
            await File.WriteAllTextAsync(testFile, "content");
            var path = CanonicalPath.Create("/test.txt").Value;
            var mapper = IdentityMapper.Instance;

            // Act
            var result = await _fileSystem.GetEntryAsync(path, mapper);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeOfType<FileSystemEntry>();
            result.Value.Path.Should().Be(path);
            result.Value.IsDirectory.Should().BeFalse();
            result.Value.Size.Should().Be(7); // "content" length
        }

        [Fact(DisplayName = "LFSM-002: GetEntryAsync with custom mapper returns custom type")]
        public async Task LFSM002()
        {
            // Arrange
            var testFile = Path.Combine(_testDirectory, "test.txt");
            await File.WriteAllTextAsync(testFile, "content");
            var path = CanonicalPath.Create("/test.txt").Value;
            var mapper = new FileInfoDtoMapper();

            // Act
            var result = await _fileSystem.GetEntryAsync(path, mapper);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeOfType<FileInfoDto>();
            result.Value.Name.Should().Be("test.txt");
            result.Value.FullPath.Should().Be(path.Value);
            result.Value.SizeInBytes.Should().Be(7);
            result.Value.IsDirectory.Should().BeFalse();
        }

        [Fact(DisplayName = "LFSM-003: GetEntryAsync when mapper throws returns failure")]
        public async Task LFSM003()
        {
            // Arrange
            var testFile = Path.Combine(_testDirectory, "test.txt");
            await File.WriteAllTextAsync(testFile, "content");
            var path = CanonicalPath.Create("/test.txt").Value;
            var mapper = new ThrowingMapper();

            // Act
            var result = await _fileSystem.GetEntryAsync(path, mapper);

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Message.Should().Contain("Mapper error");
        }

        [Fact(DisplayName = "LFSM-004: GetEntryAsync with null mapper throws ArgumentNullException")]
        public async Task LFSM004()
        {
            // Arrange
            var path = CanonicalPath.Create("/test.txt").Value;

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => _fileSystem.GetEntryAsync<FileSystemEntry>(path, null!));
        }

        [Fact(DisplayName = "LFSM-005: GetEntryAsync with directory returns mapped directory")]
        public async Task LFSM005()
        {
            // Arrange
            var testDir = Path.Combine(_testDirectory, "testdir");
            Directory.CreateDirectory(testDir);
            var path = CanonicalPath.Create("/testdir").Value;
            var mapper = new FileInfoDtoMapper();

            // Act
            var result = await _fileSystem.GetEntryAsync(path, mapper);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeOfType<FileInfoDto>();
            result.Value.Name.Should().Be("testdir");
            result.Value.FullPath.Should().Be(path.Value);
            result.Value.SizeInBytes.Should().BeNull();
            result.Value.IsDirectory.Should().BeTrue();
        }

        public void Dispose()
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
        }

        // Test helpers
        private class TestPathMapper : IPathMapper
        {
            private readonly string _baseDirectory;

            public TestPathMapper(string baseDirectory)
            {
                _baseDirectory = baseDirectory;
            }

            public Result<string, Error> MapToSystemPath(CanonicalPath canonicalPath)
            {
                // Map canonical paths like "/test.txt" to the test directory
                var relativePath = canonicalPath.Value.TrimStart('/');
                var systemPath = Path.Combine(_baseDirectory, relativePath);
                return Result<string, Error>.Success(systemPath);
            }

            public Result<CanonicalPath, Error> MapToCanonicalPath(string systemPath)
            {
                // Map system paths back to canonical paths
                if (systemPath.StartsWith(_baseDirectory))
                {
                    var relativePath = systemPath.Substring(_baseDirectory.Length).TrimStart(Path.DirectorySeparatorChar);
                    var canonicalPath = "/" + relativePath.Replace(Path.DirectorySeparatorChar, '/');
                    return CanonicalPath.Create(canonicalPath);
                }
                return CanonicalPath.Create(systemPath);
            }
        }

        private class FileInfoDto
        {
            public string Name { get; set; } = string.Empty;
            public string FullPath { get; set; } = string.Empty;
            public long? SizeInBytes { get; set; }
            public DateTime LastModified { get; set; }
            public bool IsDirectory { get; set; }
        }

        private class FileInfoDtoMapper : IFileSystemEntryMapper<FileInfoDto>
        {
            public Task<Result<FileInfoDto, FileSystemError>> MapFileAsync(
                IFileEntry entry,
                CancellationToken cancellationToken = default)
            {
                var dto = new FileInfoDto
                {
                    Name = entry.Info.Info.Name,
                    FullPath = entry.Path.Value,
                    SizeInBytes = entry.Info.Size,
                    LastModified = entry.Info.Info.ModifiedUtc,
                    IsDirectory = false
                };
                
                return Task.FromResult(Result<FileInfoDto, FileSystemError>.Success(dto));
            }

            public Task<Result<FileInfoDto, FileSystemError>> MapDirectoryAsync(
                IDirectoryEntry entry,
                CancellationToken cancellationToken = default)
            {
                var dto = new FileInfoDto
                {
                    Name = entry.Info.Info.Name,
                    FullPath = entry.Path.Value,
                    SizeInBytes = null,
                    LastModified = entry.Info.Info.ModifiedUtc,
                    IsDirectory = true
                };
                
                return Task.FromResult(Result<FileInfoDto, FileSystemError>.Success(dto));
            }
        }

        private class ThrowingMapper : IFileSystemEntryMapper<FileSystemEntry>
        {
            public Task<Result<FileSystemEntry, FileSystemError>> MapFileAsync(
                IFileEntry entry,
                CancellationToken cancellationToken = default)
            {
                throw new InvalidOperationException("Mapper error");
            }

            public Task<Result<FileSystemEntry, FileSystemError>> MapDirectoryAsync(
                IDirectoryEntry entry,
                CancellationToken cancellationToken = default)
            {
                throw new InvalidOperationException("Mapper error");
            }
        }
    }
}