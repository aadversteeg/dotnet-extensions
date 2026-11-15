using System;
using System.IO;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ave.Extensions.FileSystem.Local
{
    /// <summary>
    /// Represents an active monitoring session for file system changes.
    /// </summary>
    internal class MonitoringSession : IMonitoringSession
    {
        private readonly MonitoringOptions _options;
        private readonly IPathMapper _pathMapper;
        private readonly ILogger _logger;
        private readonly Subject<FileSystemChangedEvent> _changesSubject;
        private readonly object _lock = new object();
        private FileSystemWatcher? _watcher;
        private bool _disposed;
        private bool _isActive;

        /// <summary>
        /// Initializes a new instance of the <see cref="MonitoringSession"/> class.
        /// </summary>
        /// <param name="options">The monitoring options.</param>
        /// <param name="pathMapper">Path mapper for canonical path support.</param>
        /// <param name="logger">Logger instance.</param>
        public MonitoringSession(MonitoringOptions options, IPathMapper pathMapper, ILogger? logger = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _pathMapper = pathMapper ?? throw new ArgumentNullException(nameof(pathMapper));
            _logger = logger ?? NullLogger.Instance;
            _changesSubject = new Subject<FileSystemChangedEvent>();

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
            FileSystemWatcher? watcher;

            lock (_lock)
            {
                if (!_isActive || _disposed)
                    return Task.CompletedTask;

                _isActive = false;
                watcher = DetachWatcher();

                _logger.LogInformation("Monitoring session stopped: {Path}", _options.Path.Value);
            }

            DisposeWatcher(watcher);

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            FileSystemWatcher? watcher;

            lock (_lock)
            {
                if (_disposed)
                    return;

                _disposed = true;
                _isActive = false;
                watcher = DetachWatcher();

                _changesSubject.OnCompleted();
                _changesSubject.Dispose();
            }

            DisposeWatcher(watcher);
        }

        /// <summary>
        /// Takes ownership of the watcher off the field. Must be called under <see cref="_lock"/>;
        /// disposal itself happens outside the lock so a callback thread blocked on the lock can
        /// never be waited on from inside it.
        /// </summary>
        private FileSystemWatcher? DetachWatcher()
        {
            var watcher = _watcher;
            _watcher = null;

            if (watcher != null)
            {
                watcher.Created -= OnFileSystemEvent;
                watcher.Changed -= OnFileSystemEvent;
                watcher.Deleted -= OnFileSystemEvent;
                watcher.Renamed -= OnFileSystemRenamed;
                watcher.Error -= OnError;
            }

            return watcher;
        }

        private static void DisposeWatcher(FileSystemWatcher? watcher)
        {
            if (watcher == null)
                return;

            try
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            catch
            {
                // Ignore disposal errors
            }
        }

        /// <summary>
        /// Publishes a change to subscribers. FileSystemWatcher raises its events on thread pool
        /// threads that keep arriving after Dispose has torn the subject down, so every publication
        /// goes through here: the lock both drops post-disposal notifications and serialises
        /// delivery, which Subject&lt;T&gt; requires and concurrent watcher callbacks otherwise break.
        /// </summary>
        private void PublishChange(FileSystemChangedEvent change)
        {
            lock (_lock)
            {
                if (_disposed)
                    return;

                _changesSubject.OnNext(change);
            }
        }

        /// <summary>
        /// Publishes a terminal error to subscribers, dropping it if the session is already
        /// disposed. Nothing may throw out of here: these calls sit in the catch blocks of
        /// FileSystemWatcher callbacks, so an exception escapes onto a thread pool thread with no
        /// handler and takes the process down.
        /// </summary>
        private void PublishError(Exception error)
        {
            lock (_lock)
            {
                if (_disposed)
                    return;

                _changesSubject.OnError(error);
            }
        }

        private void StartMonitoring()
        {
            lock (_lock)
            {
                if (_disposed)
                    return;

                try
                {
                    // Convert canonical path to system path using path mapper
                    var systemPathResult = _pathMapper.MapToSystemPath(_options.Path);
                    if (systemPathResult.IsFailure)
                    {
                        PublishError(new ArgumentException(systemPathResult.Error.Message));
                        return;
                    }

                    var physicalPath = systemPathResult.Value;
                    if (!Directory.Exists(physicalPath))
                    {
                        PublishError(new DirectoryNotFoundException($"Directory not found: {_options.Path.Value}"));
                        return;
                    }

                    // Create single watcher for all files
                    _watcher = CreateWatcher(physicalPath);
                    if (_watcher != null)
                    {
                        _watcher.EnableRaisingEvents = true;
                        _isActive = true;
                    }
                }
                catch (Exception ex)
                {
                    PublishError(ex);
                }
            }
        }

        private FileSystemWatcher? CreateWatcher(string path)
        {
            try
            {
                var watcher = new FileSystemWatcher(path)
                {
                    IncludeSubdirectories = _options.IncludeSubdirectories,
                    NotifyFilter = (System.IO.NotifyFilters)_options.NotifyFilters,
                    InternalBufferSize = _options.BufferSize
                };

                // Subscribe to events
                watcher.Created += OnFileSystemEvent;
                watcher.Changed += OnFileSystemEvent;
                watcher.Deleted += OnFileSystemEvent;
                watcher.Renamed += OnFileSystemRenamed;
                watcher.Error += OnError;

                return watcher;
            }
            catch (Exception ex)
            {
                PublishError(ex);
                return null;
            }
        }

        private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
        {
            try
            {
                FileSystemChangeType changeType;
                switch (e.ChangeType)
                {
                    case WatcherChangeTypes.Created:
                        changeType = FileSystemChangeType.Created;
                        break;
                    case WatcherChangeTypes.Changed:
                        changeType = FileSystemChangeType.Changed;
                        break;
                    case WatcherChangeTypes.Deleted:
                        changeType = FileSystemChangeType.Deleted;
                        break;
                    default:
                        changeType = FileSystemChangeType.Created;
                        break;
                }

                var canonicalPathResult = _pathMapper.MapToCanonicalPath(e.FullPath);
                if (canonicalPathResult.IsFailure)
                {
                    // Path mapping failure is a critical error in monitoring
                    PublishError(new InvalidOperationException(
                        $"Failed to map system path to canonical path during monitoring: {canonicalPathResult.Error.Message}"));
                    return;
                }
                var eventArgs = new FileSystemChangedEvent(canonicalPathResult.Value, changeType);
                
                if (ShouldIncludeChange(eventArgs))
                {
                    PublishChange(eventArgs);
                }
            }
            catch (Exception ex)
            {
                PublishError(ex);
            }
        }

        private void OnFileSystemRenamed(object sender, RenamedEventArgs e)
        {
            try
            {
                // Emit both delete (old name) and create (new name) events for renames
                var oldCanonicalPathResult = _pathMapper.MapToCanonicalPath(e.OldFullPath);
                var newCanonicalPathResult = _pathMapper.MapToCanonicalPath(e.FullPath);
                
                if (oldCanonicalPathResult.IsFailure)
                {
                    PublishError(new InvalidOperationException(
                        $"Failed to map old path during rename monitoring: {oldCanonicalPathResult.Error.Message}"));
                    return;
                }
                
                if (newCanonicalPathResult.IsFailure)
                {
                    PublishError(new InvalidOperationException(
                        $"Failed to map new path during rename monitoring: {newCanonicalPathResult.Error.Message}"));
                    return;
                }
                
                var deleteEvent = new FileSystemChangedEvent(oldCanonicalPathResult.Value, FileSystemChangeType.Deleted);
                if (ShouldIncludeChange(deleteEvent))
                {
                    PublishChange(deleteEvent);
                }
                
                var createEvent = new FileSystemChangedEvent(newCanonicalPathResult.Value, FileSystemChangeType.Created);
                if (ShouldIncludeChange(createEvent))
                {
                    PublishChange(createEvent);
                }
            }
            catch (Exception ex)
            {
                PublishError(ex);
            }
        }

        private void OnError(object sender, ErrorEventArgs e)
        {
            _logger.LogWarning(e.GetException(), "File system watcher error: {Path}", _options.Path.Value);
            PublishError(e.GetException());
        }

        private bool ShouldIncludeChange(FileSystemChangedEvent changeArgs)
        {
            try
            {
                // Convert canonical path to system path for file system operations
                var systemPathResult = _pathMapper.MapToSystemPath(changeArgs.Path);
                if (systemPathResult.IsFailure)
                {
                    // Path mapping failure should not be silently ignored
                    throw new InvalidOperationException(
                        $"Failed to map canonical path to system path during filtering: {systemPathResult.Error.Message}");
                }

                var systemPath = systemPathResult.Value;

                // Check hidden/system file filters
                if (File.Exists(systemPath))
                {
                    var attributes = File.GetAttributes(systemPath);
                    
                    if (!_options.IncludeHidden && attributes.HasFlag(FileAttributes.Hidden))
                        return false;
                    
                    if (!_options.IncludeSystem && attributes.HasFlag(FileAttributes.System))
                        return false;
                }
                else if (Directory.Exists(systemPath))
                {
                    var attributes = File.GetAttributes(systemPath);
                    
                    if (!_options.IncludeHidden && attributes.HasFlag(FileAttributes.Hidden))
                        return false;
                    
                    if (!_options.IncludeSystem && attributes.HasFlag(FileAttributes.System))
                        return false;
                }

                // Apply custom filter if provided
                if (_options.CustomFilter != null && !_options.CustomFilter(changeArgs))
                    return false;

                return true;
            }
            catch
            {
                // If we can't determine whether to include the change, include it by default
                return true;
            }
        }
    }
}
