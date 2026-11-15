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
    internal class WriteStream : MemoryStream
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

        public override async ValueTask DisposeAsync()
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

            await base.DisposeAsync();
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