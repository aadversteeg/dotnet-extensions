using Ave.Documents.Core;
using Ave.Documents.Pdf;

namespace Ave.Documents.Render.Pdf.Tagging;

/// <summary>
/// The standard structure type of each role (ISO 32000-1 §14.8.4) and the attributes that go with it. Every role names
/// its standard type itself, and a role without one is refused (<see cref="StructureTree"/>), so only standard types
/// are written and the file needs no role map (veraPDF rules 6.7.3.4-1 and 7.1-5 apply to non-standard types only).
/// </summary>
internal static class StructureTypes
{
    /// <summary>The structure type of a role: the standard type the role names itself
    /// (<see cref="SemanticRole.StandardStructureType"/>); null for a role that is no structure.</summary>
    /// <param name="role">The role.</param>
    /// <returns>The type, or null.</returns>
    public static PdfName? Of(SemanticRole role) => role.StandardStructureType is { } type ? new PdfName(type) : null;

    /// <summary>
    /// The attribute object of a table cell (§14.8.5.7, Table 349, owner <c>/Table</c>): a header cell's
    /// <c>/Scope /Column</c> — in this engine header cells come only from header rows, so each heads the column(s) it
    /// spans (PDF/UA: veraPDF rule 7.5-1, "TH shall have a Scope attribute"); and <c>/ColSpan</c> and <c>/RowSpan</c>
    /// when the cell covers more than one column or row, which is how a reader rebuilds the table's grid (rules
    /// 7.2-41 and 7.2-42). Null for anything that needs no attributes.
    /// </summary>
    /// <param name="role">The role.</param>
    /// <param name="span">The cell's span.</param>
    /// <returns>The attribute dictionary, or null.</returns>
    public static PdfDictionary? Attributes(SemanticRole role, CellSpan span)
    {
        var isHeader = role == SemanticRole.TableHeaderCell;
        if (!isHeader && role != SemanticRole.TableCell)
        {
            return null;
        }

        return isHeader || span != CellSpan.Single
            ? PdfDictionary.Of(
                ("O", new PdfName("Table")),
                ("Scope", isHeader ? new PdfName("Column") : null),
                ("ColSpan", span.Columns > 1 ? new PdfInteger(span.Columns) : null),
                ("RowSpan", span.Rows > 1 ? new PdfInteger(span.Rows) : null))
            : null;
    }
}
