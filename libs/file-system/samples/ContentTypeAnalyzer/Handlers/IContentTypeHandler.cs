using Ave.Extensions.FileSystem.Traversal;
using Ave.Extensions.FileSystem.MimeDetective.Models;
using ContentTypeAnalyzer.Models;

namespace ContentTypeAnalyzer.Handlers
{
    public interface IContentTypeHandler : ITraversalHandler<FileSystemEntryWithContentType>
    {
        AnalysisStatistics Statistics { get; }
        int AnalyzedFiles { get; }
        int ErrorCount { get; }
    }
}