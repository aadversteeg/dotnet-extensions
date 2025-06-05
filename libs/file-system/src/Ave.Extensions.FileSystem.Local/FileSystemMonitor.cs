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

            if (string.IsNullOrEmpty(options.Path))
                throw new ArgumentException("Path cannot be null or empty.", nameof(options));

            cancellationToken.ThrowIfCancellationRequested();

            var session = new MonitoringSession(options);
            return Task.FromResult<IMonitoringSession>(session);
        }
    }

    /// <summary>
    /// Represents an active monitoring session for file system changes.
    /// </summary>
    internal class MonitoringSession : IMonitoringSession
    {
        private readonly MonitoringOptions _options;
        private readonly Subject<FileSystemChangeEventArgs> _changesSubject;
        private readonly List<FileSystemWatcher> _watchers;
        private readonly object _lock = new object();
        private bool _disposed;
        private bool _isActive;

        /// <summary>
        /// Initializes a new instance of the <see cref="MonitoringSession"/> class.
        /// </summary>
        /// <param name="options">The monitoring options.</param>
        public MonitoringSession(MonitoringOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _changesSubject = new Subject<FileSystemChangeEventArgs>();
            _watchers = new List<FileSystemWatcher>();
            
            StartMonitoring();
        }

        /// <inheritdoc />
        public string Path => _options.Path;

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
        public IObservable<FileSystemChangeEventArgs> Changes => _changesSubject.AsObservable();

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                if (!_isActive || _disposed)
                    return Task.CompletedTask;

                _isActive = false;
                
                foreach (var watcher in _watchers)
                {
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
                
                _watchers.Clear();
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

                foreach (var watcher in _watchers)
                {
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

                _watchers.Clear();
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
                    if (!Directory.Exists(_options.Path))
                    {
                        _changesSubject.OnError(new DirectoryNotFoundException($"Directory not found: {_options.Path}"));
                        return;
                    }

                    // Create watchers for each include pattern
                    foreach (var pattern in _options.IncludePatterns)
                    {
                        var watcher = CreateWatcher(_options.Path, pattern);
                        if (watcher != null)
                        {
                            _watchers.Add(watcher);
                            watcher.EnableRaisingEvents = true;
                        }
                    }

                    _isActive = _watchers.Count > 0;
                }
                catch (Exception ex)
                {
                    _changesSubject.OnError(ex);
                }
            }
        }

        private FileSystemWatcher? CreateWatcher(string path, string pattern)
        {
            try
            {
                var watcher = new FileSystemWatcher(path, pattern)
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
                var changeType = e.ChangeType switch
                {
                    WatcherChangeTypes.Created => FileSystemChangeType.Created,
                    WatcherChangeTypes.Changed => FileSystemChangeType.Changed,
                    WatcherChangeTypes.Deleted => FileSystemChangeType.Deleted,
                    _ => FileSystemChangeType.Created
                };

                var eventArgs = new FileSystemChangeEventArgs(e.FullPath, changeType);
                
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
                var deleteEvent = new FileSystemChangeEventArgs(e.OldFullPath, FileSystemChangeType.Deleted);
                var createEvent = new FileSystemChangeEventArgs(e.FullPath, FileSystemChangeType.Created);

                if (ShouldIncludeChange(deleteEvent))
                {
                    _changesSubject.OnNext(deleteEvent);
                }

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
            _changesSubject.OnError(e.GetException());
        }

        private bool ShouldIncludeChange(FileSystemChangeEventArgs changeArgs)
        {
            try
            {
                var path = changeArgs.FullPath;
                var fileName = System.IO.Path.GetFileName(path);

                // Check exclude patterns
                if (_options.ExcludePatterns.Any(pattern => MatchesPattern(fileName, pattern)))
                    return false;

                // Check hidden/system file filters
                if (File.Exists(path))
                {
                    var attributes = File.GetAttributes(path);
                    
                    if (!_options.IncludeHidden && attributes.HasFlag(FileAttributes.Hidden))
                        return false;
                    
                    if (!_options.IncludeSystem && attributes.HasFlag(FileAttributes.System))
                        return false;
                }
                else if (Directory.Exists(path))
                {
                    var attributes = File.GetAttributes(path);
                    
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

        private static bool MatchesPattern(string fileName, string pattern)
        {
            // Simple wildcard matching - could be enhanced with more sophisticated pattern matching
            if (pattern == "*")
                return true;

            if (pattern.Contains('*'))
            {
                var regexPattern = "^" + pattern.Replace("*", ".*").Replace("?", ".") + "$";
                return System.Text.RegularExpressions.Regex.IsMatch(fileName, regexPattern, 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }

            return string.Equals(fileName, pattern, StringComparison.OrdinalIgnoreCase);
        }
    }
}