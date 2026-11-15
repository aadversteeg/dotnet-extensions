using System;
using System.Threading;

namespace Ave.Extensions.FileSystem.Progress
{
    /// <summary>
    /// Helper class for reporting progress at reasonable intervals.
    /// </summary>
    internal class ProgressReporter
    {
        private readonly IProgress<ICopyProgress> _progress;
        private readonly TimeSpan _minInterval;
        private DateTime _lastReport = DateTime.MinValue;
        private long _lastBytesReported = 0;
        private readonly object _lock = new object();

        public ProgressReporter(IProgress<ICopyProgress> progress, TimeSpan minInterval)
        {
            _progress = progress;
            _minInterval = minInterval;
        }

        /// <summary>
        /// Reports progress if enough time has elapsed or significant progress was made.
        /// </summary>
        public void ReportProgress(CopyProgress progress, bool force = false)
        {
            if (_progress == null) return;

            lock (_lock)
            {
                var now = DateTime.UtcNow;
                var timeSinceLastReport = now - _lastReport;
                var bytesSinceLastReport = progress.BytesTransferred - _lastBytesReported;

                // Report if forced, enough time passed, or significant progress
                if (force || 
                    timeSinceLastReport >= _minInterval ||
                    bytesSinceLastReport > 1024 * 1024) // 1MB progress
                {
                    _progress.Report(progress);
                    _lastReport = now;
                    _lastBytesReported = progress.BytesTransferred;
                }
            }
        }
    }
}