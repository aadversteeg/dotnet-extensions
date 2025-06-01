using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Ave.Extensions.FileSystem.InMemory
{
    /// <summary>
    /// A write-only stream that writes to the in-memory file system when disposed.
    /// </summary>
    internal class WriteStream : MemoryStream
    {
        private readonly InMemoryFileSystem _fileSystem;
        private readonly CanonicalPath _path;
        private readonly ILogger _logger;
        private bool _disposed;

        public WriteStream(InMemoryFileSystem fileSystem, CanonicalPath path, ILogger logger)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _path = path;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                try
                {
                    // Write the content when disposed
                    WriteContent().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to write file content during dispose: {Path}", _path);
                }
                finally
                {
                    _disposed = true;
                }
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                try
                {
                    await WriteContent();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to write file content during async dispose: {Path}", _path);
                }
                finally
                {
                    _disposed = true;
                }
            }

            await base.DisposeAsync();
        }

        private async Task WriteContent()
        {
            // Reset position to beginning and get all content
            Position = 0;
            var content = ToArray();
            
            // Write to the file system
            var result = await _fileSystem.WriteFileContentAsync(_path, content);
            if (result.IsFailure)
            {
                throw new IOException($"Failed to write file content: {result.Error.Message}");
            }
        }
    }
}