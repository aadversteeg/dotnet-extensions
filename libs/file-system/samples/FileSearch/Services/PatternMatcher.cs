using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace FileSearch.Services;

public class PatternMatcher
{
    private readonly ILogger<PatternMatcher> _logger;

    public PatternMatcher(ILogger<PatternMatcher> logger)
    {
        _logger = logger;
    }

    public Regex ConvertToRegex(string pattern, bool caseSensitive)
    {
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        var options = caseSensitive 
            ? RegexOptions.None 
            : RegexOptions.IgnoreCase;

        return new Regex(regexPattern, options | RegexOptions.Compiled);
    }

    public bool IsMatch(string fileName, string pattern, bool caseSensitive)
    {
        var regex = ConvertToRegex(pattern, caseSensitive);
        return regex.IsMatch(fileName);
    }
}