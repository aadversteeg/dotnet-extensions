using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem.Models;

namespace Ave.Extensions.FileSystem.Local
{
    /// <summary>
    /// Implementation of <see cref="IFileSystemMonitor"/> that monitors file system changes.
    /// </summary>
    public class FileSystemMonitor : IFileSystemMonitor
    {
        /// <inheritdoc />
        public Task<IMonitoringSession> StartMonitoringAsync(
            MonitoringOptions options,
            CancellationToken cancellationToken = default)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            cancellationToken.ThrowIfCancellationRequested();

            var session = new MonitoringSession(options);
            return Task.FromResult<IMonitoringSession>(session);
        }
    }

}