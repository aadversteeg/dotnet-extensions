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
            lock (_lock)
            {
                if (!_isActive || _disposed)
                    return Task.CompletedTask;

                _isActive = false;

                if (_watcher != null)
                {
                    try
                    {
                        _watcher.EnableRaisingEvents = false;
                        _watcher.Dispose();
                        _watcher = null;
                    }
                    catch
                    {
                        // Ignore disposal errors
                    }
                }

                _logger.LogInformation("Monitoring session stopped: {Path}", _options.Path.Value);
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

                if (_watcher != null)
                {
                    try
                    {
                        _watcher.EnableRaisingEvents = false;
                        _watcher.Dispose();
                        _watcher = null;
                    }
                    catch
                    {
                        // Ignore disposal errors
                    }
                }
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

                try
                {
                    // Convert canonical path to system path using path mapper
                    var systemPathResult = _pathMapper.MapToSystemPath(_options.Path);
                    if (systemPathResult.IsFailure)
                    {
                        _changesSubject.OnError(new ArgumentException(systemPathResult.Error.Message));
                        return;
                    }

                    var physicalPath = systemPathResult.Value;
                    if (!Directory.Exists(physicalPath))
                    {
                        _changesSubject.OnError(new DirectoryNotFoundException($"Directory not found: {_options.Path.Value}"));
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
                    _changesSubject.OnError(ex);
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
                _changesSubject.OnError(ex);
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
                    _changesSubject.OnError(new InvalidOperationException(
                        $"Failed to map system path to canonical path during monitoring: {canonicalPathResult.Error.Message}"));
                    return;
                }
                var eventArgs = new FileSystemChangedEvent(canonicalPathResult.Value, changeType);
                
                if (ShouldIncludeChange(eventArgs))
                {
                    _changesSubject.OnNext(eventArgs);
                }
            }
            catch (Exception ex)
            {
                _changesSubject.OnError(ex);
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
                    _changesSubject.OnError(new InvalidOperationException(
                        $"Failed to map old path during rename monitoring: {oldCanonicalPathResult.Error.Message}"));
                    return;
                }
                
                if (newCanonicalPathResult.IsFailure)
                {
                    _changesSubject.OnError(new InvalidOperationException(
                        $"Failed to map new path during rename monitoring: {newCanonicalPathResult.Error.Message}"));
                    return;
                }
                
                var deleteEvent = new FileSystemChangedEvent(oldCanonicalPathResult.Value, FileSystemChangeType.Deleted);
                if (ShouldIncludeChange(deleteEvent))
                {
                    _changesSubject.OnNext(deleteEvent);
                }
                
                var createEvent = new FileSystemChangedEvent(newCanonicalPathResult.Value, FileSystemChangeType.Created);
                if (ShouldIncludeChange(createEvent))
                {
                    _changesSubject.OnNext(createEvent);
                }
            }
            catch (Exception ex)
            {
                _changesSubject.OnError(ex);
            }
        }

        private void OnError(object sender, ErrorEventArgs e)
        {
            _logger.LogWarning(e.GetException(), "File system watcher error: {Path}", _options.Path.Value);
            _changesSubject.OnError(e.GetException());
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