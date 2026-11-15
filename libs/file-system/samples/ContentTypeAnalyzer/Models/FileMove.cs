namespace ContentTypeAnalyzer.Models
{
    public record FileMove(
        string SourcePath,
        string TargetPath,
        string Reason,
        bool RequiresRename
    );
}