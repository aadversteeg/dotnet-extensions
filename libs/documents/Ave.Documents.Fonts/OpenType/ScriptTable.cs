
namespace Ave.Documents.Fonts.OpenType;

/// <summary>A script table: its tag and its default language system (OpenType spec, "Script table").</summary>
/// <param name="Tag">The script tag, for example <c>latn</c> or <c>DFLT</c>.</param>
/// <param name="DefaultLanguage">The default language system, or null when the font has none for this script.</param>
public sealed record ScriptTable(string Tag, LanguageSystem? DefaultLanguage);
