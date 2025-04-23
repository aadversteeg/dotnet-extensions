using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Ave.Extensions.FileSystem
{
    /// <summary>
    /// Extension methods for the file system monitor.
    /// </summary>
    public static class FileSystemMonitorExtensions
    {
        /// <summary>
        /// Watches a directory for changes and returns an async stream of file system change events.
        /// </summary>
        /// <param name="monitor">The file system monitor.</param>
        /// <param name="path">The directory path to watch.</param>
        /// <param name="includeSubdirectories">Whether to include subdirectories in the watch.</param>
        /// <param name="filter">The file filter to apply.</param>
        /// <param name="notifyFilters">The types of changes to watch for.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>An async stream of file system change events.</returns>
        public static async IAsyncEnumerable<FileSystemChangeEventArgs> WatchDirectoryAsStreamAsync(
            this IFileSystemMonitor monitor,
            string path,
            bool includeSubdirectories,
            string filter,
            FileSystemNotifyFilters notifyFilters,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (monitor == null)
            {
                throw new ArgumentNullException(nameof(monitor));
            }

            var handle = await monitor.WatchDirectoryAsync(path, includeSubdirectories, filter, notifyFilters, cancellationToken);
            var waitEvent = new AutoResetEvent(false);
            FileSystemChangeEventArgs? currentEvent = null;

            using var registration = cancellationToken.Register(() =>
            {
                waitEvent.Set();
            });

            try
            {
                // Set up event handler
                EventHandler<FileSystemChangeEventArgs> handler = (sender, args) =>
                {
                    currentEvent = args;
                    waitEvent.Set();
                };

                monitor.FileChanged += handler;

                try 
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        // Reset for the next event
                        currentEvent = null as FileSystemChangeEventArgs;
                        
                        // Wait for an event or cancellation
                        var signaled = waitEvent.WaitOne(TimeSpan.FromMilliseconds(-1), true);
                        
                        // Check for cancellation
                        cancellationToken.ThrowIfCancellationRequested();
                        
                        // If we have an event, yield it
                        if (currentEvent != null)
                        {
                            yield return currentEvent;
                        }
                    }
                }
                finally
                {
                    // Remove event handler
                    monitor.FileChanged -= handler;
                }
            }
            finally
            {
                // Clean up
                await handle.StopAsync(CancellationToken.None);
                handle.Dispose();
                waitEvent.Dispose();
            }
        }
    }
}