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
}