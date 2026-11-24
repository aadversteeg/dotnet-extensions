using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Pdf;

namespace Ave.Extensions.Documents.Render.Pdf.Tagging;

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
    /// The attributes of an element (§14.7.5, Table 323, <c>/A</c>: "a single attribute object or array of attribute
    /// objects"): its table attributes (<see cref="TableAttributes"/>) and its layout attributes
    /// (<see cref="LayoutAttributes"/>). Null when it has none, the object itself when it has one, an array otherwise.
    /// </summary>
    /// <param name="role">The role.</param>
    /// <param name="span">The cell's span.</param>
    /// <param name="box">The element's bounding box, or null (<see cref="ElementBounds"/>).</param>
    /// <returns>The value of <c>/A</c>, or null.</returns>
    public static PdfObject? Attributes(SemanticRole role, CellSpan span, UserSpaceBox? box)
    {
        PdfDictionary[] owners = [.. new[] { TableAttributes(role, span), LayoutAttributes(box) }.OfType<PdfDictionary>()];
        return owners.Length switch
        {
            0 => null,
            1 => owners[0],
            _ => new PdfArray([.. owners]),
        };
    }

    /// <summary>
    /// The layout attribute object (§14.8.5.4, owner <c>/Layout</c>) with the element's <c>/BBox</c>: left, bottom,
    /// right and top in default user space (§14.8.5.4.3, Table 344). Null without a box.
    /// </summary>
    private static PdfDictionary? LayoutAttributes(UserSpaceBox? box) => box is { } edges
        ? PdfDictionary.Of(
            ("O", new PdfName("Layout")),
            ("BBox", PdfArray.OfNumbers(edges.Left, edges.Bottom, edges.Right, edges.Top)))
        : null;

    /// <summary>
    /// The attribute object of a table cell (§14.8.5.7, Table 349, owner <c>/Table</c>): a header cell's
    /// <c>/Scope</c>, what it heads: <c>/Column</c>, <c>/Row</c> or <c>/Both</c>, from its role (PDF/UA: veraPDF rule
    /// 7.5-1, "TH shall have a Scope attribute"); and <c>/ColSpan</c> and <c>/RowSpan</c>
    /// when the cell covers more than one column or row, which is how a reader rebuilds the table's grid (rules
    /// 7.2-41 and 7.2-42). Null for anything that needs no attributes.
    /// </summary>
    /// <param name="role">The role.</param>
    /// <param name="span">The cell's span.</param>
    /// <returns>The attribute dictionary, or null.</returns>
    private static PdfDictionary? TableAttributes(SemanticRole role, CellSpan span)
    {
        var header = role as SemanticRole.TableHeaderCellRole;
        if (header is null && role != SemanticRole.TableCell)
        {
            return null;
        }

        return header is not null || span != CellSpan.Single
            ? PdfDictionary.Of(
                ("O", new PdfName("Table")),
                ("Scope", header is null ? null : new PdfName(header.Scope.ToString())),
                ("ColSpan", span.Columns > 1 ? new PdfInteger(span.Columns) : null),
                ("RowSpan", span.Rows > 1 ? new PdfInteger(span.Rows) : null))
            : null;
    }
}
