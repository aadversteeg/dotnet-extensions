using System;

namespace FileSystemSamples.Common.Export.Formatters;

public abstract class BaseFormatter<T> : IFormatter<T>
{
    public abstract string Format(T data);
    public abstract string FileExtension { get; }
    public abstract string ContentType { get; }
    
    protected static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        
        while (Math.Round(number / 1024) >= 1 && counter < suffixes.Length - 1)
        {
            number /= 1024;
            counter++;
        }
        
        return $"{number:N1} {suffixes[counter]}";
    }
    
    protected static string FormatTimeSpan(TimeSpan timeSpan)
    {
        if (timeSpan.TotalSeconds < 1)
            return $"{timeSpan.TotalMilliseconds:F1}ms";
        if (timeSpan.TotalMinutes < 1)
            return $"{timeSpan.TotalSeconds:F1}s";
        if (timeSpan.TotalHours < 1)
            return $"{timeSpan.Minutes}m {timeSpan.Seconds}s";
        return $"{(int)timeSpan.TotalHours}h {timeSpan.Minutes}m {timeSpan.Seconds}s";
    }
}