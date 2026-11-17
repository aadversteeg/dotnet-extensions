namespace Ave.Documents.Typesetting.LineBreaking;

/// <summary>A place where a line may (or must) end: before the UTF-16 code unit at <see cref="Offset"/>.</summary>
/// <param name="Offset">The index in the text of the first code unit of the next line. The end of the text is
/// <c>text.Length</c>.</param>
/// <param name="IsMandatory">True for a hard line break (after a line separator, UAX #14 LB4/LB5, and at the end of
/// the text, LB3); false where a line may break but need not.</param>
public readonly record struct BreakOpportunity(int Offset, bool IsMandatory);
