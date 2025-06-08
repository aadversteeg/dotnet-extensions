using System;
using System.Collections.Generic;

namespace ContentTypeAnalyzer.Models
{
    public class AnalysisStatistics
    {
        public int TotalFiles { get; set; }
        public int ErrorCount { get; set; }
        public long TotalSize { get; set; }
        public Dictionary<string, int> ContentTypeCounts { get; set; } = new();
        public Dictionary<FileCategory, int> CategoryCounts { get; set; } = new();
        public Dictionary<string, int> ConfidenceLevels { get; set; } = new();
        public TimeSpan Duration { get; set; }
    }
}