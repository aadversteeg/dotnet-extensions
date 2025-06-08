using Microsoft.Extensions.Logging;

namespace FileSearch.Services;

public class SizeParser
{
    private readonly ILogger<SizeParser> _logger;

    public SizeParser(ILogger<SizeParser> logger)
    {
        _logger = logger;
    }

    public long? Parse(string? sizeStr)
    {
        if (string.IsNullOrEmpty(sizeStr))
            return null;

        sizeStr = sizeStr.ToUpper().Trim();
        
        var multipliers = new (string suffix, long multiplier)[]
        {
            ("TB", 1024L * 1024 * 1024 * 1024),
            ("GB", 1024L * 1024 * 1024),
            ("MB", 1024 * 1024),
            ("KB", 1024),
            ("B", 1)
        };

        foreach (var (suffix, multiplier) in multipliers)
        {
            if (sizeStr.EndsWith(suffix))
            {
                var numberStr = sizeStr.Substring(0, sizeStr.Length - suffix.Length);
                if (double.TryParse(numberStr, out var number))
                {
                    return (long)(number * multiplier);
                }
            }
        }

        if (long.TryParse(sizeStr, out var bytes))
        {
            return bytes;
        }

        _logger.LogWarning("Unable to parse size: {SizeStr}", sizeStr);
        return null;
    }

    public string FormatSize(long bytes)
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
}