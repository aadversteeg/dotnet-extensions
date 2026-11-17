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
}
