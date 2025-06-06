using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ave.Extensions.FileSystem.InMemory
{
    /// <summary>
    /// In-memory implementation of file system monitoring.
    /// </summary>
    internal class FileSystemMonitor : IFileSystemMonitor
    {
        private readonly ILogger _logger;
        private readonly Subject<FileSystemChangeEventArgs> _changeSubject;
        private readonly ConcurrentDictionary<string, MonitoringSession> _sessions;
        private bool _disposed;

        public FileSystemMonitor(ILogger? logger = null)
        {
            _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
            _changeSubject = new Subject<FileSystemChangeEventArgs>();
            _sessions = new ConcurrentDictionary<string, MonitoringSession>();
        }

        /// <inheritdoc />
        public Task<IMonitoringSession> StartMonitoringAsync(
            MonitoringOptions options,
            CancellationToken cancellationToken = default)
        {
            var sessionId = Guid.NewGuid().ToString();
            
            var session = new MonitoringSession(
                sessionId,
                options.Path,
                options,
                _logger,
                () => _sessions.TryRemove(sessionId, out _));
            
            _sessions.TryAdd(sessionId, session);
            
            // Subscribe to relevant changes
            var subscription = _changeSubject
                .Where(evt => ShouldNotify(evt, options.Path, options))
                .Subscribe(evt => session.NotifyChange(evt));
            
            session.SetSubscription(subscription);
            
            return Task.FromResult<IMonitoringSession>(session);
        }

        internal void NotifyChange(string path, FileSystemChangeType changeType)
        {
            if (_disposed)
                return;
            
            var evt = new FileSystemChangeEventArgs(path, changeType);
            
            _changeSubject.OnNext(evt);
        }

        private static bool ShouldNotify(FileSystemChangeEventArgs evt, string monitorPath, MonitoringOptions options)
        {
            // Normalize paths for comparison
            var eventPath = NormalizePath(evt.FullPath);
            var watchPath = NormalizePath(monitorPath);
            
            // Check if the event is within the monitored path
            bool isInPath = eventPath == watchPath || 
                           (options.IncludeSubdirectories && eventPath.StartsWith(watchPath + "/", StringComparison.OrdinalIgnoreCase));
            
            if (!isInPath)
                return false;
            
            // Check change type filters
            if (!options.NotifyFilters.HasFlag(GetNotifyFilter(evt.ChangeType)))
                return false;
            
            // Check include patterns
            var fileName = System.IO.Path.GetFileName(eventPath);
            if (options.IncludePatterns.Any())
            {
                bool matchesInclude = options.IncludePatterns.Any(pattern => MatchesPattern(fileName, pattern));
                if (!matchesInclude)
                    return false;
            }
            
            // Check exclude patterns
            if (options.ExcludePatterns.Any())
            {
                bool matchesExclude = options.ExcludePatterns.Any(pattern => MatchesPattern(fileName, pattern));
                if (matchesExclude)
                    return false;
            }
            
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

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "/";
            
            var normalized = path.Replace('\\', '/');
            while (normalized.Contains("//"))
            {
                normalized = normalized.Replace("//", "/");
            }
            
            if (!normalized.StartsWith('/'))
                normalized = '/' + normalized;
            
            if (normalized.Length > 1 && normalized.EndsWith('/'))
                normalized = normalized.TrimEnd('/');
            
            return normalized;
        }

        private static bool MatchesPattern(string name, string pattern)
        {
            if (pattern == "*") return true;
            
            var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
            
            return System.Text.RegularExpressions.Regex.IsMatch(name, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _changeSubject?.Dispose();
                
                foreach (var session in _sessions.Values)
                {
                    session.Dispose();
                }
                _sessions.Clear();
                
                _disposed = true;
            }
        }
    }
}