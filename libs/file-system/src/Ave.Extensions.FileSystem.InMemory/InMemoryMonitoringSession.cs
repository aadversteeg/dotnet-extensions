using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem.Models;

namespace Ave.Extensions.FileSystem.InMemory
{
    /// <summary>
    /// In-memory implementation of monitoring session that subscribes to file system events.
    /// </summary>
    internal class InMemoryMonitoringSession : IMonitoringSession
    {
        private readonly MonitoringOptions _options;
        private readonly InMemoryFileSystem _fileSystem;
        private readonly Subject<FileSystemChangedEvent> _changesSubject;
        private readonly object _lock = new object();
        private bool _disposed;
        private bool _isActive;
        private IDisposable? _subscription;

        /// <summary>
        /// Initializes a new instance of the <see cref="InMemoryMonitoringSession"/> class.
        /// </summary>
        /// <param name="options">The monitoring options.</param>
        /// <param name="fileSystem">The in-memory file system instance.</param>
        public InMemoryMonitoringSession(MonitoringOptions options, InMemoryFileSystem fileSystem)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _changesSubject = new Subject<FileSystemChangedEvent>();
            _isActive = true;
            
            StartMonitoring();
        }

        /// <inheritdoc />
        public CanonicalPath Path => _options.Path;

        /// <inheritdoc />
        public bool IsActive
        {
            get
            {
                lock (_lock)
                {
                    return _isActive && !_disposed;
                }
            }
        }

        /// <inheritdoc />
        public IObservable<FileSystemChangedEvent> Changes => _changesSubject.AsObservable();

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                if (!_isActive || _disposed)
                    return Task.CompletedTask;

                _isActive = false;
                _subscription?.Dispose();
                _subscription = null;
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed)
                    return;

                _disposed = true;
                _isActive = false;
                
                _subscription?.Dispose();
                _subscription = null;
                
                _changesSubject.OnCompleted();
                _changesSubject.Dispose();
            }
        }

        private void StartMonitoring()
        {
            lock (_lock)
            {
                if (_disposed)
                    return;

                // Subscribe to file system changes from the InMemoryFileSystem's global event stream
                _subscription = _fileSystem.FileSystemEvents
                    .Where(evt => ShouldNotify(evt))
                    .Subscribe(
                        evt => _changesSubject.OnNext(evt),
                        ex => _changesSubject.OnError(ex)
                    );
            }
        }

        private bool ShouldNotify(FileSystemChangedEvent evt)
        {
            // Check if the event is within the monitored path
            var eventPath = evt.Path;
            var watchPath = _options.Path;
            var comparer = _fileSystem.PathComparer;

            bool isInPath = comparer.Equals(eventPath, watchPath) ||
                           (_options.IncludeSubdirectories && comparer.IsChildOf(eventPath, watchPath));

            if (!isInPath)
                return false;
            
            // Check change type filters
            if (!_options.NotifyFilters.HasFlag(GetNotifyFilter(evt.ChangeType)))
                return false;

            // Apply custom filter if provided
            if (_options.CustomFilter != null && !_options.CustomFilter(evt))
                return false;
            
            return true;
        }

        private static FileSystemNotifyFilters GetNotifyFilter(FileSystemChangeType changeType)
        {
            return changeType switch
            {
                FileSystemChangeType.Created => FileSystemNotifyFilters.FileName,
                FileSystemChangeType.Deleted => FileSystemNotifyFilters.FileName,
                FileSystemChangeType.Changed => FileSystemNotifyFilters.LastWrite,
                FileSystemChangeType.Renamed => FileSystemNotifyFilters.FileName,
                _ => FileSystemNotifyFilters.FileName
            };
        }

    }
}