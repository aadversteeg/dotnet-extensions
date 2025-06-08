using MimeDetective;

namespace ContentTypeAnalyzer.Services
{
    public static class ContentInspectorFactory
    {
        public static ContentInspector? Create(string definitionPack, string usageType)
        {
            // For now, return null to use extension-based detection only
            // TODO: Implement proper ContentInspector creation when 
            // the correct MimeDetective API is available
            return null;
        }
    }
}