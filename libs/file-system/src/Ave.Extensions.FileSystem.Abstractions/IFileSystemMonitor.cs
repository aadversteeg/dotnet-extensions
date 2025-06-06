using System;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem.Models;

namespace Ave.Extensions.FileSystem
{
    /// <summary>
    /// Represents a file system monitoring interface that can watch directories for changes.
    /// </summary>
    public interface IFileSystemMonitor
    {
        /// <summary>
        /// Starts monitoring a file system location with the specified options.
        /// </summary>
        /// <param name="options">The monitoring options.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A monitoring session that can be used to receive changes and control monitoring.</returns>
        Task<IMonitoringSession> StartMonitoringAsync(
            MonitoringOptions options,
            CancellationToken cancellationToken = default);
    }
    
    /// <summary>
    /// Represents an active monitoring session for file system changes.
    /// </summary>
    public interface IMonitoringSession : IDisposable
    {
        /// <summary>
        /// Gets the path being monitored.
        /// </summary>
        string Path { get; }
        
        /// <summary>
        /// Gets a value indicating whether the monitoring session is currently active.
        /// </summary>
        bool IsActive { get; }
        
        /// <summary>
        /// Gets an observable stream of file system changes.
        /// </summary>
        IObservable<FileSystemChangeEventArgs> Changes { get; }
        
        /// <summary>
        /// Stops the monitoring session.
        /// </summary>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task StopAsync(CancellationToken cancellationToken = default);
    }
    
}