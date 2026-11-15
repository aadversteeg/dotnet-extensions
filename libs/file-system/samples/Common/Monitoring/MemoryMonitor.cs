using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace FileSystemSamples.Common.Monitoring;

/// <summary>
/// Monitors memory usage and provides warnings when thresholds are exceeded.
/// </summary>
public class MemoryMonitor : IMemoryMonitor
{
    private readonly ILogger<MemoryMonitor> _logger;
    private readonly Timer _monitoringTimer;
    private readonly long _warningThreshold;
    private long _peakMemoryUsage;
    private bool _disposed;
    private bool _isMonitoring;
    private bool _warningTriggered;

    /// <summary>
    /// Initializes a new instance of the MemoryMonitor class.
    /// </summary>
    /// <param name="logger">Logger for debug information.</param>
    /// <param name="warningThresholdMB">Warning threshold in megabytes (default: 500MB).</param>
    /// <param name="monitoringIntervalMs">Monitoring interval in milliseconds (default: 1000ms).</param>
    public MemoryMonitor(ILogger<MemoryMonitor> logger, int warningThresholdMB = 500, int monitoringIntervalMs = 1000)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _warningThreshold = warningThresholdMB * 1024L * 1024L; // Convert MB to bytes
        
        _monitoringTimer = new Timer(CheckMemoryUsage, null, Timeout.Infinite, monitoringIntervalMs);
        
        _logger.LogDebug("MemoryMonitor initialized with {ThresholdMB}MB warning threshold", warningThresholdMB);
    }

    /// <inheritdoc />
    public long CurrentMemoryUsage => GC.GetTotalMemory(false);

    /// <inheritdoc />
    public long PeakMemoryUsage => _peakMemoryUsage;

    /// <inheritdoc />
    public long WarningThreshold => _warningThreshold;

    /// <inheritdoc />
    public event EventHandler<MemoryWarningEventArgs>? MemoryWarning;

    /// <inheritdoc />
    public void Start()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MemoryMonitor));
            
        if (_isMonitoring)
            return;

        _isMonitoring = true;
        _warningTriggered = false;
        _peakMemoryUsage = CurrentMemoryUsage;
        
        // Start the monitoring timer
        _monitoringTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        
        _logger.LogInformation("Memory monitoring started. Warning threshold: {ThresholdMB:F1}MB", 
            _warningThreshold / (1024.0 * 1024.0));
    }

    /// <inheritdoc />
    public void Stop()
    {
        if (!_isMonitoring)
            return;

        _isMonitoring = false;
        _monitoringTimer.Change(Timeout.Infinite, Timeout.Infinite);
        
        _logger.LogInformation("Memory monitoring stopped. Peak usage: {PeakMB:F1}MB", 
            _peakMemoryUsage / (1024.0 * 1024.0));
    }

    private void CheckMemoryUsage(object? state)
    {
        if (!_isMonitoring || _disposed)
            return;

        try
        {
            var currentUsage = CurrentMemoryUsage;
            
            // Update peak usage
            if (currentUsage > _peakMemoryUsage)
            {
                _peakMemoryUsage = currentUsage;
            }

            // Check if we should trigger a warning
            if (!_warningTriggered && currentUsage > _warningThreshold)
            {
                _warningTriggered = true;
                
                _logger.LogWarning("Memory usage warning: {CurrentMB:F1}MB exceeds threshold of {ThresholdMB:F1}MB",
                    currentUsage / (1024.0 * 1024.0),
                    _warningThreshold / (1024.0 * 1024.0));

                MemoryWarning?.Invoke(this, new MemoryWarningEventArgs(currentUsage, _warningThreshold));
                
                // Suggest garbage collection
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                
                var afterGcUsage = CurrentMemoryUsage;
                _logger.LogInformation("After GC: memory usage reduced to {AfterGcMB:F1}MB", 
                    afterGcUsage / (1024.0 * 1024.0));
            }
            
            // Reset warning flag if usage drops significantly below threshold
            if (_warningTriggered && currentUsage < _warningThreshold * 0.8)
            {
                _warningTriggered = false;
                _logger.LogInformation("Memory usage returned to normal: {CurrentMB:F1}MB", 
                    currentUsage / (1024.0 * 1024.0));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during memory monitoring");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the unmanaged resources used by the MemoryMonitor and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            Stop();
            _monitoringTimer?.Dispose();
            _disposed = true;
        }
    }
}