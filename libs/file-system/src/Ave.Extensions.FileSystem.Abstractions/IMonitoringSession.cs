using System;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem.Models;

namespace Ave.Extensions.FileSystem
{
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