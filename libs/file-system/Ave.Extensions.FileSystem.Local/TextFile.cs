using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ave.Extensions.FileSystem.Local
{
    /// <summary>
    /// Reads and writes whole text files asynchronously. On net10.0 this is <see cref="File"/>; netstandard2.0
    /// has no File.ReadAllTextAsync / File.WriteAllTextAsync, so the same behaviour is built on an async
    /// <see cref="FileStream"/>: UTF-8 (BOM detected on read, no BOM on write) and the cancellation token is
    /// checked for every buffer that is copied.
    /// </summary>
    internal static class TextFile
    {
#if NETSTANDARD2_0
        private const int BufferSize = 4096;
        private const int CopyBufferSize = 81920;

        public static async Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true))
            using (var content = new MemoryStream())
            {
                await file.CopyToAsync(content, CopyBufferSize, cancellationToken);
                content.Position = 0;

                using (var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        public static async Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(contents);
            using (var content = new MemoryStream(bytes, writable: false))
            using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, BufferSize, useAsync: true))
            {
                await content.CopyToAsync(file, CopyBufferSize, cancellationToken);
                await file.FlushAsync(cancellationToken);
            }
        }
#else
        public static Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken)
        {
            return File.ReadAllTextAsync(path, cancellationToken);
        }

        public static Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken)
        {
            return File.WriteAllTextAsync(path, contents, cancellationToken);
        }
#endif
    }
}
