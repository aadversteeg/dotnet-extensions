using Ave.Extensions.FileSystem.Traversal;

namespace ContentTypeAnalyzer.Models
{
    public class AnalysisOptions
    {
        public string Path { get; set; } = ".";
        public HandlerType HandlerType { get; set; } = HandlerType.Simple;
        public FileCategory? FilterCategory { get; set; }
        public double MinimumConfidence { get; set; } = 0.7;
        public string DefinitionPack { get; set; } = "default";
        public string UsageType { get; set; } = "personal";
        public TraversalStrategyHint Strategy { get; set; } = TraversalStrategyHint.Auto;
        public string? OutputPath { get; set; }
        public string? ExportFormat { get; set; }
        public int MaxBytes { get; set; } = 8192;
        public bool FallbackExtension { get; set; } = true;
        public int? MaxDepth { get; set; }
    }
}