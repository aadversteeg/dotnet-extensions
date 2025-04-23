using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Ave.Extensions.FileSystem
{
    /// <summary>
    /// Implementation of <see cref="IFileSystemMonitor"/> that monitors file system changes.
    /// </summary>
    internal class FileSystemMonitor : IFileSystemMonitor
    {
        private readonly IReadOnlyFileSystem _fileSystem;
        private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="FileSystemMonitor"/> class.
        /// </summary>
        /// <param name="fileSystem">The file system to use for path normalization.</param>
        internal FileSystemMonitor(IReadOnlyFileSystem fileSystem)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _watchers = new ConcurrentDictionary<string, FileSystemWatcher>();
        }

        /// <inheritdoc />
        public event EventHandler<FileSystemChangeEventArgs>? FileChanged;

        /// <inheritdoc />
        public async Task<IMonitoringHandle> WatchDirectoryAsync(
            string path, 
            bool includeSubdirectories, 
            string filter, 
            FileSystemNotifyFilters notifyFilters,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Path cannot be null or empty.", nameof(path));
            }

            if (string.IsNullOrWhiteSpace(filter))
            {
                filter = "*";
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Verify directory exists
            var exists = await _fileSystem.DirectoryExistsAsync(path, cancellationToken);
            if (!exists)
            {
                throw new DirectoryNotFoundException($"Directory not found: {path}");
            }

            // Normalize path if we have access to the FileSystemService
            string normalizedPath = path;
            if (_fileSystem is FileSystemService fileSystemService)
            {
                normalizedPath = fileSystemService.NormalizePath(path);
            }
            else
            {
                normalizedPath = Path.GetFullPath(path);
            }

            // Create unique ID for this watcher
            var watcherId = Guid.NewGuid().ToString();

            // Create watcher
            var watcher = new FileSystemWatcher
            {
                Path = normalizedPath,
                Filter = filter,
                IncludeSubdirectories = includeSubdirectories,
                NotifyFilter = ConvertToFileSystemWatcherNotifyFilters(notifyFilters)
            };

            // Set up event handlers
            watcher.Created += (s, e) => OnFileSystemChange(s, e, FileSystemChangeType.Created);
            watcher.Deleted += (s, e) => OnFileSystemChange(s, e, FileSystemChangeType.Deleted);
            watcher.Changed += (s, e) => OnFileSystemChange(s, e, FileSystemChangeType.Changed);
            watcher.Renamed += (s, e) => OnFileSystemChange(s, e, FileSystemChangeType.Renamed);
            watcher.Error += (s, e) => OnFileSystemError(s, e);

            // Store the watcher
            _watchers.TryAdd(watcherId, watcher);

            // Start watching
            watcher.EnableRaisingEvents = true;

            // Return handle
            return new MonitoringHandle(this, watcherId, normalizedPath);
        }

        /// <summary>
        /// Stops monitoring a directory.
        /// </summary>
        /// <param name="watcherId">The ID of the watcher to stop.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        internal Task StopWatchingAsync(string watcherId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_watchers.TryRemove(watcherId, out var watcher))
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Converts FileSystemNotifyFilters to System.IO.NotifyFilters.
        /// </summary>
        private static NotifyFilters ConvertToFileSystemWatcherNotifyFilters(FileSystemNotifyFilters filters)
        {
            var result = NotifyFilters.FileName; // Default

            if (filters == FileSystemNotifyFilters.None)
            {
                return result;
            }

            if (filters.HasFlag(FileSystemNotifyFilters.FileName))
                result |= NotifyFilters.FileName;

            if (filters.HasFlag(FileSystemNotifyFilters.DirectoryName))
                result |= NotifyFilters.DirectoryName;

            if (filters.HasFlag(FileSystemNotifyFilters.Attributes))
                result |= NotifyFilters.Attributes;

            if (filters.HasFlag(FileSystemNotifyFilters.Size))
                result |= NotifyFilters.Size;

            if (filters.HasFlag(FileSystemNotifyFilters.LastWrite))
                result |= NotifyFilters.LastWrite;

            if (filters.HasFlag(FileSystemNotifyFilters.LastAccess))
                result |= NotifyFilters.LastAccess;

            if (filters.HasFlag(FileSystemNotifyFilters.CreationTime))
                result |= NotifyFilters.CreationTime;

            if (filters.HasFlag(FileSystemNotifyFilters.Security))
                result |= NotifyFilters.Security;

            return result;
        }

        /// <summary>
        /// Handles file system change events.
        /// </summary>
        private void OnFileSystemChange(object sender, FileSystemEventArgs e, FileSystemChangeType changeType)
        {
            FileChanged?.Invoke(this, new FileSystemChangeEventArgs(e.FullPath, changeType));
        }

        /// <summary>
        /// Handles file system error events.
        /// </summary>
        private void OnFileSystemError(object sender, ErrorEventArgs e)
        {
            FileChanged?.Invoke(this, new FileSystemChangeEventArgs(string.Empty, FileSystemChangeType.Error));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes the resources used by this instance.
        /// </summary>
        /// <param name="disposing">Whether the method is being called from the Dispose method.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                // Stop and dispose all watchers
                foreach (var watcher in _watchers.Values)
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                }
                
                _watchers.Clear();
            }

            _disposed = true;
        }

        /// <summary>
        /// Implementation of <see cref="IMonitoringHandle"/>.
        /// </summary>
        private class MonitoringHandle : IMonitoringHandle
        {
            private readonly FileSystemMonitor _monitor;
            private readonly string _watcherId;
            private bool _disposed;

            /// <summary>
            /// Initializes a new instance of the <see cref="MonitoringHandle"/> class.
            /// </summary>
            /// <param name="monitor">The monitor that created this handle.</param>
            /// <param name="watcherId">The ID of the watcher.</param>
            /// <param name="path">The path being monitored.</param>
            public MonitoringHandle(FileSystemMonitor monitor, string watcherId, string path)
            {
                _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
                _watcherId = watcherId ?? throw new ArgumentNullException(nameof(watcherId));
                Path = path ?? throw new ArgumentNullException(nameof(path));
            }

            /// <inheritdoc />
            public string Path { get; }

            /// <inheritdoc />
            public Task StopAsync(CancellationToken cancellationToken = default)
            {
                return _monitor.StopWatchingAsync(_watcherId, cancellationToken);
            }

            /// <inheritdoc />
            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _monitor.StopWatchingAsync(_watcherId).GetAwaiter().GetResult();
                _disposed = true;
            }
        }
    }
}