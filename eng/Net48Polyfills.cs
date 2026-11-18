// Compiled into the test projects only, and only with -p:UseNet48=true (see Directory.Build.targets).
// The test code (not the libraries) uses a few APIs that .NET Framework 4.8 does not have. These are the
// smallest stand-ins with the same observable result for the way the tests call them.
#if NETFRAMEWORK
global using DotnetExtensions.Net48Polyfills;

using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace System.Runtime.CompilerServices
{
    // Records and init accessors in test code.
    internal static class IsExternalInit
    {
    }
}

namespace DotnetExtensions.Net48Polyfills
{
    internal static class Net48Polyfills
    {
        // File.WriteAllTextAsync/ReadAllTextAsync on .NET use UTF-8 without a byte order mark, as File.WriteAllText
        // and File.ReadAllText do on .NET Framework.
        extension(File)
        {
            public static Task WriteAllTextAsync(string path, string? contents, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.WriteAllText(path, contents);
                return Task.CompletedTask;
            }

            public static Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(File.ReadAllText(path));
            }

            public static Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.WriteAllBytes(path, bytes);
                return Task.CompletedTask;
            }

            public static Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(File.ReadAllBytes(path));
            }

            public static FileSystemInfo CreateSymbolicLink(string path, string pathToTarget)
            {
                CreateLink(path, pathToTarget, directory: false);
                return new FileInfo(path);
            }
        }

        extension(Path)
        {
            // The tests only ask for the path of a file below relativeTo.
            public static string GetRelativePath(string relativeTo, string path)
            {
                var root = Path.GetFullPath(relativeTo);
                if (!root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                {
                    root += Path.DirectorySeparatorChar;
                }

                var relative = new Uri(root).MakeRelativeUri(new Uri(Path.GetFullPath(path)));
                return Uri.UnescapeDataString(relative.ToString()).Replace('/', Path.DirectorySeparatorChar);
            }
        }

        extension(string value)
        {
            public string[] Split(char separator, StringSplitOptions options) => value.Split(new[] { separator }, options);

            public bool StartsWith(char character) => value.Length > 0 && value[0] == character;
        }

        extension(Stream stream)
        {
            public async Task ReadExactlyAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
            {
                while (count > 0)
                {
                    var read = await stream.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        throw new EndOfStreamException();
                    }

                    offset += read;
                    count -= read;
                }
            }
        }

        extension(HashAlgorithm algorithm)
        {
            public Task<byte[]> ComputeHashAsync(Stream inputStream, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(algorithm.ComputeHash(inputStream));
            }
        }

        // .NET throws IOException when the link cannot be created (for example no symlink privilege); the tests
        // catch that and skip.
        internal static void CreateLink(string path, string pathToTarget, bool directory)
        {
            const int SymbolicLinkFlagDirectory = 0x1;
            const int SymbolicLinkFlagAllowUnprivilegedCreate = 0x2;
            var flags = SymbolicLinkFlagAllowUnprivilegedCreate | (directory ? SymbolicLinkFlagDirectory : 0);
            if (!CreateSymbolicLinkW(path, pathToTarget, flags))
            {
                throw new IOException(new Win32Exception(Marshal.GetLastWin32Error()).Message);
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool CreateSymbolicLinkW(string lpSymlinkFileName, string lpTargetFileName, int dwFlags);
    }

    // Separate class: the lowered members of extension(File) and extension(Directory) would collide.
    internal static class Net48DirectoryPolyfills
    {
        extension(Directory)
        {
            public static FileSystemInfo CreateSymbolicLink(string path, string pathToTarget)
            {
                Net48Polyfills.CreateLink(path, pathToTarget, directory: true);
                return new DirectoryInfo(path);
            }
        }
    }
}
#endif
