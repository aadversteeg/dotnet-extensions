using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Local;
using Ave.Extensions.FileSystem.Local.PathMappers;
using FluentAssertions;
using Xunit;

namespace UnitTests.Extensions.FileSystem.Local
{
    /// <summary>
    /// ReadAllTextAsync and WriteAllTextAsync use File.ReadAllTextAsync / File.WriteAllTextAsync on net10.0
    /// and an own implementation on netstandard2.0. These tests pin the behaviour both assets must share
    /// (run them against the netstandard2.0 asset with -p:UseNs20=true).
    /// </summary>
    public class LocalFileSystemTextFileTests : IDisposable
    {
        private static readonly byte[] Utf8ByteOrderMark = [0xEF, 0xBB, 0xBF];

        private readonly string _testDirectory;
        private readonly LocalFileSystem _fileSystem;
        private readonly IPathMapper _pathMapper;

        public LocalFileSystemTextFileTests()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), $"FSTests_{Guid.NewGuid()}");
            Directory.CreateDirectory(_testDirectory);

            _pathMapper = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? (IPathMapper)new WindowsPathMapper()
                : new UnixPathMapper();
            _fileSystem = new LocalFileSystem(_pathMapper);
        }

        [Fact(DisplayName = "LFT-001: WriteAllTextAsync writes UTF-8 without a byte order mark")]
        public async Task LFT001()
        {
            // Arrange
            var file = Path.Combine(_testDirectory, "written.txt");
            var content = "café € 1";

            // Act
            var result = await _fileSystem.WriteAllTextAsync(Canonical(file), content);

            // Assert
            result.IsSuccess.Should().BeTrue();
            File.ReadAllBytes(file).Should().Equal(new UTF8Encoding(false).GetBytes(content));
        }

        [Fact(DisplayName = "LFT-002: WriteAllTextAsync replaces the content of an existing longer file")]
        public async Task LFT002()
        {
            // Arrange
            var file = Path.Combine(_testDirectory, "existing.txt");
            File.WriteAllText(file, "a much longer original content");

            // Act
            var result = await _fileSystem.WriteAllTextAsync(Canonical(file), "short");

            // Assert
            result.IsSuccess.Should().BeTrue();
            File.ReadAllText(file).Should().Be("short");
        }

        [Fact(DisplayName = "LFT-003: ReadAllTextAsync reads UTF-8 and drops a byte order mark")]
        public async Task LFT003()
        {
            // Arrange
            var withMark = Path.Combine(_testDirectory, "with-mark.txt");
            var withoutMark = Path.Combine(_testDirectory, "without-mark.txt");
            var content = "café € 1";
            var bytes = new UTF8Encoding(false).GetBytes(content);
            var markedBytes = new byte[Utf8ByteOrderMark.Length + bytes.Length];
            Utf8ByteOrderMark.CopyTo(markedBytes, 0);
            bytes.CopyTo(markedBytes, Utf8ByteOrderMark.Length);
            File.WriteAllBytes(withMark, markedBytes);
            File.WriteAllBytes(withoutMark, bytes);

            // Act
            var marked = await _fileSystem.ReadAllTextAsync(Canonical(withMark));
            var unmarked = await _fileSystem.ReadAllTextAsync(Canonical(withoutMark));

            // Assert
            marked.Value.Should().Be(content);
            unmarked.Value.Should().Be(content);
        }

        [Fact(DisplayName = "LFT-004: ReadAllTextAsync detects a UTF-16 byte order mark")]
        public async Task LFT004()
        {
            // Arrange
            var file = Path.Combine(_testDirectory, "utf16.txt");
            var content = "café € 1";
            File.WriteAllText(file, content, Encoding.Unicode);

            // Act
            var result = await _fileSystem.ReadAllTextAsync(Canonical(file));

            // Assert
            result.Value.Should().Be(content);
        }

        [Fact(DisplayName = "LFT-005: ReadAllTextAsync honours a cancelled token")]
        public async Task LFT005()
        {
            // Arrange
            var file = Path.Combine(_testDirectory, "read.txt");
            File.WriteAllText(file, "content");
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act
            Func<Task> act = () => _fileSystem.ReadAllTextAsync(Canonical(file), cts.Token);

            // Assert
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact(DisplayName = "LFT-006: WriteAllTextAsync honours a cancelled token and leaves no file")]
        public async Task LFT006()
        {
            // Arrange
            var file = Path.Combine(_testDirectory, "not-written.txt");
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act
            Func<Task> act = () => _fileSystem.WriteAllTextAsync(Canonical(file), "content", cts.Token);

            // Assert
            await act.Should().ThrowAsync<OperationCanceledException>();
            File.Exists(file).Should().BeFalse();
        }

        public void Dispose()
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }

        private CanonicalPath Canonical(string systemPath) => _pathMapper.MapToCanonicalPath(systemPath).Value;
    }
}
