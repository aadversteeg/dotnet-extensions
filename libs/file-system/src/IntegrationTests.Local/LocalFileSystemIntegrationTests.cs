using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Local;
using Ave.Extensions.FileSystem.Local.PathMappers;
using FluentAssertions;
using IntegrationTests.FileSystem.Local.TestHelpers;
using Xunit;

namespace IntegrationTests.FileSystem.Local
{
    /// <summary>
    /// Integration tests for LocalFileSystem with path mapping.
    /// </summary>
    public class LocalFileSystemIntegrationTests : IDisposable
    {
        private readonly string _tempDirectory;
        private readonly IPathMapper? _pathMapper;
        private readonly LocalFileSystem _fileSystemService;

        public LocalFileSystemIntegrationTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), $"FileSystemIntegrationTests_{Guid.NewGuid()}");
            Directory.CreateDirectory(_tempDirectory);

            // Use appropriate path mapper for the platform
            _pathMapper = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
                ? new WindowsPathMapper() 
                : new UnixPathMapper();
            
            _fileSystemService = new LocalFileSystem(_pathMapper);
        }

        [Fact(DisplayName = "LFSIT-001: LocalFileSystem with path mapping should handle canonical paths")]
        public async Task LFSIT001()
        {
            // Arrange
            var testContent = "Hello, World!";
            var fileName = "test.txt";
            var systemPath = Path.Combine(_tempDirectory, fileName);
            
            // Create canonical path based on platform
            string canonicalPath;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Convert C:\temp\... to /c/temp/...
                var drive = _tempDirectory[0].ToString().ToLowerInvariant();
                var pathWithoutDrive = _tempDirectory.Substring(2).Replace('\\', '/');
                canonicalPath = $"/{drive}{pathWithoutDrive}/{fileName}";
            }
            else
            {
                canonicalPath = $"{_tempDirectory}/{fileName}";
            }

            // Act - Write using canonical path
            var writeResult = await _fileSystemService.WriteAllTextAsync(canonicalPath.ToCanonicalPath(), testContent);
            writeResult.IsSuccess.Should().BeTrue();

            // Assert - File should exist at system path
            File.Exists(systemPath).Should().BeTrue();

            // Act - Read using canonical path
            var readResult = await _fileSystemService.ReadAllTextAsync(canonicalPath.ToCanonicalPath());
            readResult.IsSuccess.Should().BeTrue();
            readResult.Value.Should().Be(testContent);

            // Act - Check existence using canonical path
            var existsResult = await _fileSystemService.FileExistsAsync(canonicalPath.ToCanonicalPath());
            existsResult.IsSuccess.Should().BeTrue();
            existsResult.Value.Should().BeTrue();
        }

        [Fact(DisplayName = "LFSIT-002: LocalFileSystem GetFiles should map paths back to canonical")]
        public async Task LFSIT002()
        {
            // Arrange
            var fileName1 = "file1.txt";
            var fileName2 = "file2.txt";
            var systemPath1 = Path.Combine(_tempDirectory, fileName1);
            var systemPath2 = Path.Combine(_tempDirectory, fileName2);
            
            // Create test files
            await File.WriteAllTextAsync(systemPath1, "content1");
            await File.WriteAllTextAsync(systemPath2, "content2");

            // Create canonical directory path
            string canonicalDirPath;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var drive = _tempDirectory[0].ToString().ToLowerInvariant();
                var pathWithoutDrive = _tempDirectory.Substring(2).Replace('\\', '/');
                canonicalDirPath = $"/{drive}{pathWithoutDrive}";
            }
            else
            {
                canonicalDirPath = _tempDirectory;
            }

            // Act
            var filesResult = await _fileSystemService.GetFilesAsync(canonicalDirPath.ToCanonicalPath());

            // Assert
            filesResult.IsSuccess.Should().BeTrue();
            filesResult.Value.Should().HaveCount(2);
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // On Windows, paths should be mapped back to canonical format
                filesResult.Value.Should().AllSatisfy(path => path.ToString().Should().StartWith("/"));
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }
    }
}