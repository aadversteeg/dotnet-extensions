using System;
using System.IO;
using System.Threading.Tasks;
using Azure.Storage.Files.Shares;
using Microsoft.Extensions.Logging;

namespace Ave.Extensions.FileSystem.AzureFileStorage
{
    /// <summary>
    /// A write-only stream that uploads to Azure File Share when disposed.
    /// </summary>
#if NETSTANDARD2_0
    internal class WriteStream : MemoryStream, IAsyncDisposable
#else
    internal class WriteStream : MemoryStream
#endif
    {
        private readonly ShareFileClient _fileClient;
        private readonly ILogger _logger;
        private bool _disposed;

        public WriteStream(ShareFileClient fileClient, ILogger logger)
        {
            _fileClient = fileClient ?? throw new ArgumentNullException(nameof(fileClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                try
                {
                    // Upload the content when disposed
                    UploadContent().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to upload file content during dispose");
                }
                finally
                {
                    _disposed = true;
                }
            }

            base.Dispose(disposing);
        }

#if NETSTANDARD2_0
        // netstandard2.0's Stream has no DisposeAsync to override. Implementing IAsyncDisposable explicitly
        // keeps the write asynchronous for every caller that disposes through IAsyncDisposable. .NET Core 3.0
        // up to .NET 9 load the netstandard2.1 asset, which overrides Stream.DisposeAsync.
        async ValueTask IAsyncDisposable.DisposeAsync()
        {
            await UploadContentOnDisposeAsync();
            Dispose();
        }
#else
        public override async ValueTask DisposeAsync()
        {
            await UploadContentOnDisposeAsync();
            await base.DisposeAsync();
        }
#endif

        private async Task UploadContentOnDisposeAsync()
        {
            if (!_disposed)
            {
                try
                {
                    await UploadContent();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to upload file content during async dispose");
                }
                finally
                {
                    _disposed = true;
                }
            }
        }

        private async Task UploadContent()
        {
            if (Length == 0)
            {
                // Create empty file
                await _fileClient.CreateAsync(0);
                return;
            }

            // Reset position to beginning
            Position = 0;

            // Create file with the correct size
            await _fileClient.CreateAsync(Length);

            // Upload the content
            await _fileClient.UploadAsync(this);
        }
    }
}