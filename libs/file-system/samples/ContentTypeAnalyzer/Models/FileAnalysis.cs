using System;
using System.Collections.Generic;

namespace ContentTypeAnalyzer.Models
{
    public class FileAnalysis
    {
        public string Path { get; set; } = string.Empty;
        public string OriginalExtension { get; set; } = string.Empty;
        public string DetectedContentType { get; set; } = string.Empty;
        public string FileTypeDescription { get; set; } = string.Empty;
        public double Confidence { get; set; }
        public string[] AlternativeExtensions { get; set; } = Array.Empty<string>();
        public FileCategory Category { get; set; }
        public long Size { get; set; }
        public DateTime Modified { get; set; }
        public bool IsCorrectlyNamed { get; set; }
        public string SecurityAssessment { get; set; } = string.Empty;
        public Dictionary<string, object> Metadata { get; set; } = new();
    }
}