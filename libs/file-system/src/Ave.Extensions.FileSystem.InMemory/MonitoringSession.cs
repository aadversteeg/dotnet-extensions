using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem.Models;
using Microsoft.Extensions.Logging;

namespace Ave.Extensions.FileSystem.InMemory
{
    /// <summary>
    /// Represents a monitoring session for the in-memory file system.
    /// </summary>
    internal class MonitoringSession : IMonitoringSession
    {
        private readonly string _sessionId;
        private readonly string _path;
        private readonly MonitoringOptions _options;
        private readonly ILogger _logger;
        private readonly Action _onDispose;
        private readonly Subject<FileSystemChangeEventArgs> _eventSubject;
        private IDisposable? _subscription;
        private bool _disposed;

        public MonitoringSession(
            string sessionId,
            string path,
            MonitoringOptions options,
            ILogger logger,
            Action onDispose)
        {
            _sessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
            _path = path ?? throw new ArgumentNullException(nameof(path));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _onDispose = onDispose ?? throw new ArgumentNullException(nameof(onDispose));
            _eventSubject = new Subject<FileSystemChangeEventArgs>();
        }

        /// <inheritdoc />
        public string Path => _path;

        /// <inheritdoc />
        public bool IsActive => !_disposed;

        /// <inheritdoc />
        public IObservable<FileSystemChangeEventArgs> Changes => _eventSubject.AsObservable();

        internal void SetSubscription(IDisposable subscription)
        {
            _subscription = subscription;
        }

        internal void NotifyChange(FileSystemChangeEventArgs evt)
        {
            if (!_disposed)
            {
                _eventSubject.OnNext(evt);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            Dispose();
            return Task.CompletedTask;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _subscription?.Dispose();
                _eventSubject?.Dispose();
                _onDispose?.Invoke();
                _disposed = true;
            }
        }
    }
}