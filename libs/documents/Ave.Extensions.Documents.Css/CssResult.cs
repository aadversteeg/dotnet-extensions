using Ave.Extensions.Documents.Core;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents.Css;

/// <summary>A result of the CSS reader that always exists, with what was found on the way: a style sheet with the
/// diagnostics of reading it, a computed style with those of computing it. Which diagnostics refuse a document is
/// decided by <see cref="CssDiagnostics.ClassOf"/>.</summary>
/// <typeparam name="T">The result's type.</typeparam>
/// <param name="Value">The result.</param>
/// <param name="Diagnostics">The diagnostics, in the order they were found.</param>
public sealed record CssResult<T>(T Value, ValueList<Error> Diagnostics);
