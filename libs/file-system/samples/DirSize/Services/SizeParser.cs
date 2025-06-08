using System;
using System.Globalization;

namespace DirSize.Services;

public static class SizeParser
{
    public static long? Parse(string? sizeStr)
    {
        if (string.IsNullOrEmpty(sizeStr))
            return null;

        sizeStr = sizeStr.ToUpperInvariant();
        
        var multipliers = new (string suffix, long multiplier)[]
        {
            ("TB", 1024L * 1024 * 1024 * 1024),
            ("GB", 1024L * 1024 * 1024),
            ("MB", 1024 * 1024),
            ("KB", 1024),
            ("B", 1)
        };

        // Support both decimal formats: 1.5MB and 1,5MB
        sizeStr = sizeStr.Replace(',', '.');

        foreach (var (suffix, multiplier) in multipliers)
        {
            if (sizeStr.EndsWith(suffix))
            {
                var numberStr = sizeStr[..^suffix.Length].Trim();
                if (double.TryParse(numberStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    return (long)(number * multiplier);
                }
            }
        }

        // Try parsing as plain number
        if (long.TryParse(sizeStr, out var bytes))
            return bytes;

        return null;
    }

    public static string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    public static string FormatTime(TimeSpan timeSpan)
    {
        if (timeSpan.TotalSeconds < 1)
            return $"{timeSpan.TotalMilliseconds:F0}ms";
        if (timeSpan.TotalMinutes < 1)
            return $"{timeSpan.TotalSeconds:F1}s";
        if (timeSpan.TotalHours < 1)
            return $"{timeSpan.TotalMinutes:F1}m";
        return $"{timeSpan.TotalHours:F1}h";
    }
}