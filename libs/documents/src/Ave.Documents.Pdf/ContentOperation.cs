using System.Collections.Immutable;

namespace Ave.Documents.Pdf;

/// <summary>
/// One instruction in a content stream: operands followed by an operator, for example <c>0 0 1 rg</c>
/// (ISO 32000-1 §7.8.2: "A content stream consists of a sequence of operands followed by an operator").
/// Create instructions with <see cref="ContentOperators"/>; write them with <see cref="ContentStream"/>.
/// </summary>
/// <param name="Operator">The operator, for example <c>rg</c>.</param>
/// <param name="Operands">The operands, in order.</param>
public sealed record ContentOperation(string Operator, ImmutableArray<PdfObject> Operands);
