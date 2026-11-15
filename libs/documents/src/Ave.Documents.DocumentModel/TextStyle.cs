using Ave.Documents.Core;

namespace Ave.Documents.DocumentModel;

/// <summary>The computed text properties of an <see cref="InlineRun"/>.</summary>
/// <param name="FontFamily">One family name (CSS Fonts 4 §2.1; a fallback list comes later).</param>
/// <param name="FontSize">The font size (CSS Fonts 4 §2.5), positive.</param>
/// <param name="FontWeight">The weight (CSS Fonts 4 §2.2).</param>
/// <param name="Color">The text colour (CSS Color 4 §3.2, property <c>color</c>).</param>
/// <param name="FontStyle">Upright or italic (CSS Fonts 4 §2.4).</param>
public sealed record TextStyle(string FontFamily, Length FontSize, FontWeight FontWeight, Color Color, FontStyle FontStyle = FontStyle.Normal);
