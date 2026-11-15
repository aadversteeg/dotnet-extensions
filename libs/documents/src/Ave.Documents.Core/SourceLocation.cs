using System.IO;

namespace Ave.Documents.Core;

/// <summary>
/// Where a piece of the document was written: the caller's source file and line. The code front-end fills it
/// from <c>[CallerFilePath]</c> and <c>[CallerLineNumber]</c>, which the C# compiler writes into the IL as
/// literals, so it costs nothing at run time and survives obfuscation. Every error the engine reports about a
/// node carries this location, so it points at the user's line, not at a stack trace.
/// </summary>
/// <param name="File">The full path of the source file as the compiler saw it.</param>
/// <param name="Line">The 1-based line number; 0 when unknown.</param>
public readonly record struct SourceLocation(string File, int Line)
{
    /// <summary>
    /// Formats as <c>FileName.cs:42</c>. Only the file NAME is written, not the full path, so text output
    /// (golden files) is the same on every machine and in every checkout directory.
    /// </summary>
    /// <returns>The text form.</returns>
    public override string ToString() => $"{Path.GetFileName(File)}:{Line}";
}
