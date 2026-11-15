using System.Text.RegularExpressions;

namespace Ave.Extensions.FileSystem.Traversal
{
    /// <summary>
    /// Matches file and directory names against glob patterns.
    /// </summary>
    public sealed class PatternMatcher
    {
        private readonly Regex _regex;
        private readonly string _pattern;

        /// <summary>
        /// Initializes a new instance of the <see cref="PatternMatcher"/> class.
        /// </summary>
        /// <param name="pattern">The glob pattern to match against.</param>
        public PatternMatcher(string pattern)
        {
            _pattern = pattern ?? "*";
            
            // Convert glob pattern to regex
            var regexPattern = "^" + Regex.Escape(_pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
            _regex = new Regex(regexPattern, RegexOptions.IgnoreCase);
        }

        /// <summary>
        /// Determines whether the input matches the pattern.
        /// </summary>
        /// <param name="input">The input string to test.</param>
        /// <returns>true if the input matches the pattern; otherwise, false.</returns>
        public bool IsMatch(string input) => _regex.IsMatch(input);
        
        /// <summary>
        /// Gets the original pattern.
        /// </summary>
        public string Pattern => _pattern;
    }
}