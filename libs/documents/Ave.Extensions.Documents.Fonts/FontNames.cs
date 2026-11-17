namespace Ave.Extensions.Documents.Fonts;

/// <summary>The names of a font, read from its 'name' table.</summary>
/// <param name="Family">The family, for example <c>Lato</c> (typographic family, name ID 16, if present;
/// otherwise name ID 1).</param>
/// <param name="Subfamily">The style within the family, for example <c>Bold</c> (name ID 17 or 2).</param>
/// <param name="FullName">The full name, for example <c>Lato Bold</c> (name ID 4).</param>
/// <param name="PostScriptName">The PostScript name, for example <c>Lato-Bold</c> (name ID 6); used as the
/// font's name inside PDF files.</param>
public sealed record FontNames(string Family, string Subfamily, string FullName, string PostScriptName);
