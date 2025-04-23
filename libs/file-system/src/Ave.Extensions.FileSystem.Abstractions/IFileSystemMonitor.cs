using System;
using System.Threading;
using System.Threading.Tasks;

namespace Ave.Extensions.FileSystem
{
    /// <summary>
    /// Represents a file system monitoring interface that can watch directories for changes.
    /// </summary>
    public interface IFileSystemMonitor : IDisposable
    {
        /// <summary>
        /// Event that is raised when a file system change is detected.
        /// </summary>
        event EventHandler<FileSystemChangeEventArgs> FileChanged;
        
        /// <summary>
        /// Begins watching a directory for changes.
        /// </summary>
        /// <param name="path">The directory path to watch.</param>
        /// <param name="includeSubdirectories">Whether to include subdirectories in the watch.</param>
        /// <param name="filter">The file filter to apply.</param>
        /// <param name="notifyFilters">The types of changes to watch for.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A monitoring handle that can be used to control the watch.</returns>
        Task<IMonitoringHandle> WatchDirectoryAsync(
            string path, 
            bool includeSubdirectories, 
            string filter, 
            FileSystemNotifyFilters notifyFilters, 
            CancellationToken cancellationToken = default);
    }
    
    /// <summary>
    /// Represents a handle to an active file system monitor.
    /// </summary>
    public interface IMonitoringHandle : IDisposable
    {
        /// <summary>
        /// Gets the path being monitored.
        /// </summary>
        string Path { get; }
        
        /// <summary>
        /// Stops the monitoring.
        /// </summary>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task StopAsync(CancellationToken cancellationToken = default);
    }
    
    /// <summary>
    /// Event arguments for file system change events.
    /// </summary>
    public class FileSystemChangeEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FileSystemChangeEventArgs"/> class.
        /// </summary>
        /// <param name="fullPath">The full path of the changed file.</param>
        /// <param name="changeType">The type of change that occurred.</param>
        public FileSystemChangeEventArgs(string fullPath, FileSystemChangeType changeType)
        {
            FullPath = fullPath;
            ChangeType = changeType;
        }

        /// <summary>
        /// Gets the full path of the changed file.
        /// </summary>
        public string FullPath { get; }

        /// <summary>
        /// Gets the type of change that occurred.
        /// </summary>
        public FileSystemChangeType ChangeType { get; }
    }
}