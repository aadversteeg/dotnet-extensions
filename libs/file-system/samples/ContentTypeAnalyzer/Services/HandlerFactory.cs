using System;
using ContentTypeAnalyzer.Handlers;
using ContentTypeAnalyzer.Models;

namespace ContentTypeAnalyzer.Services
{
    public class HandlerFactory
    {
        public IContentTypeHandler CreateHandler(HandlerType type, ContentAnalyzerOptions options)
        {
            return type switch
            {
                HandlerType.Simple => new SimpleContentTypeHandler(),
                HandlerType.Misnamed => new MisnamedFilesHandler(),
                HandlerType.Organize => new OrganizeFilesHandler(),
                HandlerType.Detailed => new DetailedAnalysisHandler(),
                _ => throw new ArgumentException($"Unknown handler type: {type}")
            };
        }
    }
}