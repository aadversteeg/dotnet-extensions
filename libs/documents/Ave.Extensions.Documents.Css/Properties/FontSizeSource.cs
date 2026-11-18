namespace Ave.Extensions.Documents.Css.Properties;

/// <summary>
/// Where a computed font size comes from, as Chromium keeps it (measured in Chromium 159; Firefox does the same): the
/// absolute-size keyword of CSS Fonts 4 §2.5 it is, if it is one, and whether it is absolute (from a length such as px,
/// or calc()) rather than from a keyword, directly or through em, percentages, larger and smaller. A keyword size is
/// taken from the keyword table of the element's own family, where monospace has medium = 13px; a size that is not
/// absolute is scaled by 13/16 (or 16/13) where the family changes to or from monospace.
/// </summary>
/// <param name="Keyword">The keyword, lowercase, or null.</param>
/// <param name="Absolute">Whether the size is absolute.</param>
internal readonly record struct FontSizeSource(string? Keyword, bool Absolute)
{
    /// <summary>The initial value, medium.</summary>
    public static FontSizeSource Medium => new("medium", false);
}
