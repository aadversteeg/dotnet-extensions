using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Rules;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents.Css.Cascade;

/// <summary>
/// The origins of CSS Cascade 5 §6.2, in order of precedence for normal declarations (important declarations reverse
/// it), with the author presentational hint origin of §6.5 between the user and the author origins.
/// </summary>
public enum CascadeOrigin
{
    /// <summary>The user agent's style sheet (for HTML, the rendering section of the HTML Standard).</summary>
    UserAgent,

    /// <summary>The user's style sheets.</summary>
    User,

    /// <summary>Presentational hints from the markup (§6.5): an origin of its own for the cascade, part of the author
    /// origin for <c>revert</c>. They are never important.</summary>
    PresentationalHint,

    /// <summary>The document's style sheets and <c>style</c> attributes.</summary>
    Author,
}

/// <summary>A style sheet with its origin, in the order the cascade receives them (their order of appearance).</summary>
/// <param name="Sheet">The style sheet.</param>
/// <param name="Origin">Its origin; a style sheet cannot be in <see cref="CascadeOrigin.PresentationalHint"/>.</param>
public sealed record CascadeSheet(StyleSheet Sheet, CascadeOrigin Origin);

/// <summary>
/// Loads the style sheet an <c>@import</c> names (CSS Cascade 5 §2). The caller resolves the URL (against the importing
/// style sheet's address), fetches and parses it, reports its own diagnostics, and returns the sheet; or null when it
/// could not, and the import is skipped. The cascade calls it once per <c>@import</c> it reaches whose conditions hold;
/// it never opens a file or a socket itself.
/// </summary>
/// <param name="import">The rule.</param>
/// <param name="importer">The style sheet it is in.</param>
/// <returns>The imported style sheet, or null.</returns>
public delegate StyleSheet? StyleSheetLoader(ImportRule import, StyleSheet importer);

/// <summary>A result of the CSS reader that always exists, with what was found on the way: a style sheet with the
/// diagnostics of reading it, a computed style with those of computing it. Which diagnostics refuse a document is
/// decided by <see cref="CssDiagnostics.ClassOf"/>.</summary>
/// <typeparam name="T">The result's type.</typeparam>
/// <param name="Value">The result.</param>
/// <param name="Diagnostics">The diagnostics, in the order they were found.</param>
public sealed record CssResult<T>(T Value, ValueList<Error> Diagnostics);
