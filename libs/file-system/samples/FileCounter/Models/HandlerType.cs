namespace FileCounter.Models;

/// <summary>
/// Types of counting handlers available.
/// </summary>
public enum HandlerType
{
    /// <summary>
    /// Basic counting with minimal memory footprint.
    /// </summary>
    Simple,
    
    /// <summary>
    /// Extended counting with per-extension breakdown.
    /// </summary>
    Detailed,
    
    /// <summary>
    /// Comprehensive statistics with performance metrics.
    /// </summary>
    Summary
}