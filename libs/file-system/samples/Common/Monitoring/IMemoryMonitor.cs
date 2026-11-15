using System;

namespace FileSystemSamples.Common.Monitoring;

/// <summary>
/// Interface for monitoring memory usage during operations.
/// </summary>
public interface IMemoryMonitor : IDisposable
{
    /// <summary>
    /// Gets the current memory usage in bytes.
    /// </summary>
    long CurrentMemoryUsage { get; }
    
    /// <summary>
    /// Gets the peak memory usage recorded since monitoring started.
    /// </summary>
    long PeakMemoryUsage { get; }
    
    /// <summary>
    /// Gets the memory warning threshold in bytes.
    /// </summary>
    long WarningThreshold { get; }
    
    /// <summary>
    /// Starts memory monitoring.
    /// </summary>
    void Start();
    
    /// <summary>
    /// Stops memory monitoring.
    /// </summary>
    void Stop();
    
    /// <summary>
    /// Event raised when memory usage exceeds the warning threshold.
    /// </summary>
    event EventHandler<MemoryWarningEventArgs>? MemoryWarning;
}

/// <summary>
/// Event arguments for memory warning events.
/// </summary>
public class MemoryWarningEventArgs : EventArgs
{
    /// <summary>
    /// Gets the current memory usage in bytes.
    /// </summary>
    public long CurrentUsage { get; }
    
    /// <summary>
    /// Gets the warning threshold in bytes.
    /// </summary>
    public long Threshold { get; }
    
    /// <summary>
    /// Gets the timestamp when the warning was triggered.
    /// </summary>
    public DateTime Timestamp { get; }
    
    /// <summary>
    /// Initializes a new instance of the MemoryWarningEventArgs class.
    /// </summary>
    /// <param name="currentUsage">The current memory usage in bytes.</param>
    /// <param name="threshold">The warning threshold in bytes.</param>
    public MemoryWarningEventArgs(long currentUsage, long threshold)
    {
        CurrentUsage = currentUsage;
        Threshold = threshold;
        Timestamp = DateTime.UtcNow;
    }
}