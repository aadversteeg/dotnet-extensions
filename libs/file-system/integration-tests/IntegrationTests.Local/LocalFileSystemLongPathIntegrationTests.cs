using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Local;
using Ave.Extensions.FileSystem.Local.PathMappers;
using Ave.Extensions.FileSystem.Mapping;
using Ave.Extensions.FileSystem.Models;
using FluentAssertions;
using Xunit;

namespace IntegrationTests.FileSystem.Local
{
    /// <summary>
    /// Paths of 260 characters or more (folders: 248) under a short root, through the public API. On .NET Framework
    /// without the LongPathsEnabled machine setting these only work because the library adds the \\?\ prefix; run
    /// them there with -p:UseNet48=true. No path that the library returns may carry the prefix.
    /// </summary>
    public class LocalFileSystemLongPathIntegrationTests : IDisposable
    {
        private const int LongPathLength = 260;
        private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(10);

        private readonly string _root;
        private readonly string _longDirectory;
        private readonly IPathMapper _pathMapper;
        private readonly LocalFileSystem _fileSystem;

        public LocalFileSystemLongPathIntegrationTests()
        {
            _root = Path.Combine(Path.GetTempPath(), $"lp-{Guid.NewGuid():N}".Substring(0, 11));
            Directory.CreateDirectory(_root);

            // Four names of 60 characters: the folder is longer than 248 characters, its files longer than 260,
            // and every single name stays far below the 255 that Linux allows.
            _longDirectory = Path.Combine(_root, Segment('a'), Segment('b'), Segment('c'), Segment('d'));

            _pathMapper = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? new WindowsPathMapper()
                : new UnixPathMapper();
            _fileSystem = new LocalFileSystem(_pathMapper);
        }

        [Fact(DisplayName = "LFSLP-001: CreateDirectoryAsync creates a folder with a long path and DirectoryExistsAsync finds it")]
        public async Task LFSLP001()
        {
            _longDirectory.Length.Should().BeGreaterThan(248);

            var result = await _fileSystem.CreateDirectoryAsync(Canonical(_longDirectory));

            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
            Directory.Exists(Raw(_longDirectory)).Should().BeTrue();
            var exists = await _fileSystem.DirectoryExistsAsync(Canonical(_longDirectory));
            exists.IsSuccess.Should().BeTrue();
            exists.Value.Should().BeTrue();
        }

        [Fact(DisplayName = "LFSLP-002: WriteAllTextAsync, ReadAllTextAsync and FileExistsAsync work on a file with a long path")]
        public async Task LFSLP002()
        {
            var file = await CreateLongDirectoryAsync("file.txt");
            file.Length.Should().BeGreaterThan(LongPathLength);

            var write = await _fileSystem.WriteAllTextAsync(Canonical(file), "long path content");

            write.IsSuccess.Should().BeTrue(write.IsFailure ? write.Error.Message : null);
            File.Exists(Raw(file)).Should().BeTrue();
            var read = await _fileSystem.ReadAllTextAsync(Canonical(file));
            read.IsSuccess.Should().BeTrue(read.IsFailure ? read.Error.Message : null);
            read.Value.Should().Be("long path content");
            var exists = await _fileSystem.FileExistsAsync(Canonical(file));
            exists.IsSuccess.Should().BeTrue();
            exists.Value.Should().BeTrue();
        }

        [Fact(DisplayName = "LFSLP-003: GetEntryAsync gives the info of a file and a folder with a long path")]
        public async Task LFSLP003()
        {
            var file = await CreateLongFileAsync("info.txt", "12345");

            var fileEntry = await _fileSystem.GetEntryAsync(Canonical(file), IdentityMapper.Instance);
            var directoryEntry = await _fileSystem.GetEntryAsync(Canonical(_longDirectory), IdentityMapper.Instance);

            fileEntry.IsSuccess.Should().BeTrue(fileEntry.IsFailure ? fileEntry.Error.Message : null);
            fileEntry.Value.Path.Should().Be(Canonical(file));
            fileEntry.Value.IsDirectory.Should().BeFalse();
            fileEntry.Value.Size.Should().Be(5);
            fileEntry.Value.Extension.Should().Be(".txt");
            directoryEntry.IsSuccess.Should().BeTrue(directoryEntry.IsFailure ? directoryEntry.Error.Message : null);
            directoryEntry.Value.Path.Should().Be(Canonical(_longDirectory));
            directoryEntry.Value.IsDirectory.Should().BeTrue();
        }

        [Fact(DisplayName = "LFSLP-004: Listing the top level of a folder with a long path gives canonical paths without a prefix")]
        public async Task LFSLP004()
        {
            var file = await CreateLongFileAsync("listed.txt", "x");
            var subDirectory = Path.Combine(_longDirectory, "sub");
            (await _fileSystem.CreateDirectoryAsync(Canonical(subDirectory))).IsSuccess.Should().BeTrue();

            var files = await _fileSystem.GetFilesAsync(Canonical(_longDirectory));
            var listed = await _fileSystem.ListFilesAsync(Canonical(_longDirectory));
            var directories = await _fileSystem.GetDirectoriesAsync(Canonical(_longDirectory));

            files.IsSuccess.Should().BeTrue(files.IsFailure ? files.Error.Message : null);
            files.Value.Should().Equal(Canonical(file));
            listed.IsSuccess.Should().BeTrue(listed.IsFailure ? listed.Error.Message : null);
            listed.Value.Should().Equal(Canonical(file));
            directories.IsSuccess.Should().BeTrue(directories.IsFailure ? directories.Error.Message : null);
            directories.Value.Should().Equal(Canonical(subDirectory));
            ShouldHaveNoPrefix(files.Value.Concat(listed.Value).Concat(directories.Value));
        }

        [Fact(DisplayName = "LFSLP-005: Listing recursively from a short root finds the files and folders with long paths")]
        public async Task LFSLP005()
        {
            var file = await CreateLongFileAsync("deep.txt", "x");

            var files = await _fileSystem.GetFilesAsync(Canonical(_root), "*", SearchOption.AllDirectories);
            var directories = await _fileSystem.GetDirectoriesAsync(Canonical(_root), "*", SearchOption.AllDirectories);

            files.IsSuccess.Should().BeTrue(files.IsFailure ? files.Error.Message : null);
            files.Value.Should().Equal(Canonical(file));
            directories.IsSuccess.Should().BeTrue(directories.IsFailure ? directories.Error.Message : null);
            directories.Value.Should().HaveCount(4).And.Contain(Canonical(_longDirectory));
            ShouldHaveNoPrefix(files.Value.Concat(directories.Value));
        }

        [Fact(DisplayName = "LFSLP-006: MoveFileAsync and MoveDirectoryAsync move entries with long paths")]
        public async Task LFSLP006()
        {
            var file = await CreateLongFileAsync("move-me.txt", "moved");
            var movedFile = Path.Combine(_longDirectory, "moved.txt");
            var movedDirectory = Path.Combine(Path.GetDirectoryName(_longDirectory)!, Segment('e'));

            var moveFile = await _fileSystem.MoveFileAsync(Canonical(file), Canonical(movedFile));
            var moveDirectory = await _fileSystem.MoveDirectoryAsync(Canonical(_longDirectory), Canonical(movedDirectory));

            moveFile.IsSuccess.Should().BeTrue(moveFile.IsFailure ? moveFile.Error.Message : null);
            moveDirectory.IsSuccess.Should().BeTrue(moveDirectory.IsFailure ? moveDirectory.Error.Message : null);
            Directory.Exists(Raw(_longDirectory)).Should().BeFalse();
            var read = await _fileSystem.ReadAllTextAsync(Canonical(Path.Combine(movedDirectory, "moved.txt")));
            read.IsSuccess.Should().BeTrue(read.IsFailure ? read.Error.Message : null);
            read.Value.Should().Be("moved");
        }

        [Fact(DisplayName = "LFSLP-007: CopyDirectoryAsync copies a folder recursively into a long path")]
        public async Task LFSLP007()
        {
            var source = Path.Combine(_root, Segment('s'));
            var sourceFile = Path.Combine(source, Segment('t'), Segment('u'), Segment('v'), "copied.txt");
            (await _fileSystem.CreateDirectoryAsync(Canonical(Path.GetDirectoryName(sourceFile)!))).IsSuccess.Should().BeTrue();
            (await _fileSystem.WriteAllTextAsync(Canonical(sourceFile), "copied")).IsSuccess.Should().BeTrue();
            var destination = Path.Combine(_root, Segment('w'));
            var destinationFile = destination + sourceFile.Substring(source.Length);
            destinationFile.Length.Should().BeGreaterThan(LongPathLength);

            var copy = await _fileSystem.CopyDirectoryAsync(Canonical(source), Canonical(destination));

            copy.IsSuccess.Should().BeTrue(copy.IsFailure ? copy.Error.Message : null);
            var read = await _fileSystem.ReadAllTextAsync(Canonical(destinationFile));
            read.IsSuccess.Should().BeTrue(read.IsFailure ? read.Error.Message : null);
            read.Value.Should().Be("copied");
            File.Exists(Raw(sourceFile)).Should().BeTrue("a copy keeps the source");
        }

        [Fact(DisplayName = "LFSLP-008: DeleteDirectoryAsync recursive deletes a tree with long paths")]
        public async Task LFSLP008()
        {
            await CreateLongFileAsync("delete-me.txt", "x");
            var top = Path.Combine(_root, Segment('a'));

            var delete = await _fileSystem.DeleteDirectoryAsync(Canonical(top), recursive: true);

            delete.IsSuccess.Should().BeTrue(delete.IsFailure ? delete.Error.Message : null);
            Directory.Exists(Raw(top)).Should().BeFalse();
        }

        [Fact(DisplayName = "LFSLP-009: A change to a file with a long path arrives with its canonical path")]
        public async Task LFSLP009()
        {
            await CreateLongDirectoryAsync();
            var file = Path.Combine(_longDirectory, "watched.txt");
            var changes = new ConcurrentQueue<FileSystemChangedEvent>();
            var session = await _fileSystem.CreateMonitoringSessionAsync(MonitoringOptions.Recursive(Canonical(_root)));
            session.IsSuccess.Should().BeTrue(session.IsFailure ? session.Error.Message : null);

            using (session.Value)
            using (session.Value.Changes.Subscribe(changes.Enqueue))
            {
                session.Value.IsActive.Should().BeTrue();
                (await _fileSystem.WriteAllTextAsync(Canonical(file), "watched")).IsSuccess.Should().BeTrue();

                var arrived = await WaitForAsync(() => changes.Any(c => c.Path == Canonical(file)));

                arrived.Should().BeTrue($"a change for {file} should arrive");
                ShouldHaveNoPrefix(changes.Select(c => c.Path));
            }
        }

        [Fact(DisplayName = "LFSLP-010: A change to a hidden file with a long path is filtered when IncludeHidden is false")]
        public async Task LFSLP010()
        {
            // Arranged without the library, so that only the filter of the monitoring session is under test.
            Directory.CreateDirectory(Raw(_longDirectory));
            // A leading dot makes the file hidden on Linux and macOS; on Windows the attribute does.
            var hidden = Path.Combine(_longDirectory, ".hidden.txt");
            var visible = Path.Combine(_longDirectory, "visible.txt");
            File.WriteAllText(Raw(hidden), "hidden");
            File.WriteAllText(Raw(visible), "visible");
            File.SetAttributes(Raw(hidden), File.GetAttributes(Raw(hidden)) | FileAttributes.Hidden);
            var changes = new ConcurrentQueue<FileSystemChangedEvent>();
            var options = MonitoringOptions.Recursive(Canonical(_root));
            options.IncludeHidden = false;
            var session = await _fileSystem.CreateMonitoringSessionAsync(options);
            session.IsSuccess.Should().BeTrue(session.IsFailure ? session.Error.Message : null);

            using (session.Value)
            using (session.Value.Changes.Subscribe(changes.Enqueue))
            {
                // The hidden file changes first; once the change of the visible file has arrived, a change of the
                // hidden one would have arrived too.
                AppendRaw(hidden, " changed");
                AppendRaw(visible, " changed");

                var arrived = await WaitForAsync(() => changes.Any(c => c.Path == Canonical(visible)));
                await Task.Delay(500);

                arrived.Should().BeTrue($"the change of {visible} should arrive");
                changes.Should().NotContain(c => c.Path == Canonical(hidden));
                ShouldHaveNoPrefix(changes.Select(c => c.Path));
            }
        }

        public void Dispose()
        {
            if (!Directory.Exists(Raw(_root)))
                return;

            foreach (var file in Directory.GetFiles(Raw(_root), "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Raw(_root), recursive: true);
        }

        private static string Segment(char letter) => letter + "-" + new string(letter, 58);

        // Raw System.IO calls of the tests themselves (arrange, assert, cleanup) do not go through the library, so on
        // .NET Framework they need the \\?\ prefix for a long path themselves.
        private static string Raw(string path)
        {
#if NETFRAMEWORK
            return @"\\?\" + path;
#else
            return path;
#endif
        }

        private static void AppendRaw(string path, string text)
        {
            using var stream = new FileStream(Raw(path), FileMode.Append, FileAccess.Write, FileShare.Read);
            var bytes = Encoding.UTF8.GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
        }

        private CanonicalPath Canonical(string systemPath)
        {
            var result = _pathMapper.MapToCanonicalPath(systemPath);
            if (result.IsFailure)
                throw new ArgumentException($"Invalid test path: {systemPath}. Error: {result.Error.Message}", nameof(systemPath));
            return result.Value;
        }

        private async Task<string> CreateLongDirectoryAsync(string? fileName = null)
        {
            var result = await _fileSystem.CreateDirectoryAsync(Canonical(_longDirectory));
            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
            return fileName == null ? _longDirectory : Path.Combine(_longDirectory, fileName);
        }

        private async Task<string> CreateLongFileAsync(string fileName, string contents)
        {
            var file = await CreateLongDirectoryAsync(fileName);
            file.Length.Should().BeGreaterThan(LongPathLength);
            var result = await _fileSystem.WriteAllTextAsync(Canonical(file), contents);
            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
            return file;
        }

        private static void ShouldHaveNoPrefix(IEnumerable<CanonicalPath> paths)
        {
            paths.Should().OnlyContain(p => !p.Value.Contains("?"));
        }

        private static async Task<bool> WaitForAsync(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow + EventTimeout;
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                    return true;
                await Task.Delay(50);
            }
            return condition();
        }
    }
}
