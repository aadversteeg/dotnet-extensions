using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.DocumentModel;

/// <summary>
/// One decoration applied to a run of text (CSS Text Decoration 3 §2, "Line Decoration"): a line that a decorating box, the run's own
/// element or an ancestor, draws across its text. The font is the decorating box's, which gives the line its thickness
/// and its position (Blink <c>TextDecorationInfo::ResolveDecorationAt</c>: "decoration.used_font =
/// decorating_box->GetUsedFont()"; cases <c>dl-whose</c>, <c>dl-nest</c>); it is not a <see cref="TextStyle"/>, which would
/// hold decorations again. A front-end hands over the decorations each run gets, as Chromium keeps them on the computed
/// style of each text (<c>ComputedStyle::AppliedTextDecorations</c>); the model does not resolve inheritance.
/// </summary>
/// <param name="Line">The line.</param>
/// <param name="Color">The line's colour (<c>text-decoration-color</c>; its initial value <c>currentcolor</c> is the
/// decorating box's text colour).</param>
/// <param name="FontFamily">The decorating box's font family.</param>
/// <param name="FontSize">The decorating box's font size, positive.</param>
/// <param name="FontWeight">The decorating box's font weight.</param>
/// <param name="FontStyle">The decorating box's font style.</param>
public sealed record TextDecoration(TextDecorationLine Line, Color Color, string FontFamily, Length FontSize, FontWeight FontWeight, FontStyle FontStyle = FontStyle.Normal);
