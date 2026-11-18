using System.Linq;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents.Core;

/// <summary>
/// Attaches "where in the user's code" to an error. Each layer adds what it knows: the font layer knows the
/// character, the layout layer knows the node and its source location.
/// </summary>
public static class ErrorSourceExtensions
{
    /// <summary>
    /// Adds the source file and line to an error's metadata. For several errors combined into one
    /// (<see cref="ErrorCodes.Aggregate"/>) the location is added to every part, so each part can still
    /// be reported on its own.
    /// </summary>
    /// <param name="error">The error.</param>
    /// <param name="location">Where the node or call that caused it was written.</param>
    /// <returns>The error with <see cref="ErrorMetadataKeys.SourceFile"/> and
    /// <see cref="ErrorMetadataKeys.SourceLine"/> set.</returns>
    public static Error WithSource(this Error error, SourceLocation location) =>
        error.Code == ErrorCodes.Aggregate
            ? Errors.Aggregate([.. error.Parts().Select(part => part.WithSource(location))])
            : error.With(ErrorMetadataKeys.SourceFile, location.File).With(ErrorMetadataKeys.SourceLine, location.Line);

    /// <summary>
    /// Adds a place in a text a front-end read (an HTML document, a style sheet) to an error's metadata: the text's
    /// name, the line and the column. For combined errors the place is added to every part, as
    /// <see cref="WithSource(Error, SourceLocation)"/> does.
    /// </summary>
    /// <param name="error">The error.</param>
    /// <param name="sourceName">The name of the text, as its reader was given it (a file name or a URL).</param>
    /// <param name="position">The line and column in that text.</param>
    /// <returns>The error with <see cref="ErrorMetadataKeys.SourceFile"/>, <see cref="ErrorMetadataKeys.SourceLine"/>
    /// and <see cref="ErrorMetadataKeys.SourceColumn"/> set.</returns>
    public static Error WithSource(this Error error, string sourceName, TextPosition position) =>
        error.Code == ErrorCodes.Aggregate
            ? Errors.Aggregate([.. error.Parts().Select(part => part.WithSource(sourceName, position))])
            : error.With(ErrorMetadataKeys.SourceFile, sourceName)
                .With(ErrorMetadataKeys.SourceLine, position.Line)
                .With(ErrorMetadataKeys.SourceColumn, position.Column);
}
