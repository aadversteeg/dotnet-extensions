using Ave.Extensions.Documents.Core;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents.Css;

/// <summary>What a diagnostic means for the document it is about.</summary>
public enum DiagnosticClass
{
    /// <summary>A problem the reader recovered from as the standard says, as a browser does: the result is the
    /// browser's. Reported, never refused.</summary>
    Warning,

    /// <summary>Valid input with no effect in a static paged document, ignored by rule (listed in
    /// docs/html-css-profile.md).</summary>
    Ignored,

    /// <summary>Valid input this engine does not implement: the result would differ from a browser's, so a front-end
    /// refuses the document unless its caller asked for warnings only.</summary>
    Refusal,
}

/// <summary>The one place that says which class a CSS diagnostic is, and the one way to make one.</summary>
public static class CssDiagnostics
{
    /// <summary>The class of a diagnostic, from its code's branch (<see cref="CssErrorCodes"/>): Unsupported and the
    /// limits (TooLarge, TooDeep) refuse, Ignored is ignored, everything else is a warning.</summary>
    /// <param name="code">The diagnostic's code.</param>
    /// <returns>Its class.</returns>
    public static DiagnosticClass ClassOf(ErrorCode code)
    {
        if (InBranch(code, CssErrorCodes.Unsupported._) || code == CssErrorCodes.TooLarge || code == CssErrorCodes.TooDeep)
        {
            return DiagnosticClass.Refusal;
        }

        return InBranch(code, CssErrorCodes.Ignored._) ? DiagnosticClass.Ignored : DiagnosticClass.Warning;
    }

    private static bool InBranch(ErrorCode code, ErrorCode branch) => code == branch || code.IsChildOf(branch);

    /// <summary>A diagnostic at a place in a style sheet.</summary>
    /// <param name="code">The code.</param>
    /// <param name="message">What is wrong, for a person.</param>
    /// <param name="sourceName">The style sheet's name.</param>
    /// <param name="position">Where.</param>
    /// <returns>The error.</returns>
    internal static Error At(ErrorCode code, string message, string sourceName, TextPosition position) =>
        new Error(code, message).WithSource(sourceName, position);
}
