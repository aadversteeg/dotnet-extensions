namespace ContentTypeAnalyzer.Models
{
    public enum FileCategory
    {
        Image,       // image/* MIME types
        Document,    // PDFs, Office docs, text files
        Archive,     // Compressed files
        Audio,       // audio/* MIME types
        Video,       // video/* MIME types
        Text,        // Plain text, code files
        Application, // Executables, libraries
        Data,        // Databases, data files
        Unknown      // Unrecognized types
    }
}