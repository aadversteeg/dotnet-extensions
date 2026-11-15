namespace Ave.Documents.DocumentModel;

/// <summary>
/// Names the layout algorithm of a box: its inner display type (CSS Display 3 §2.2). OPEN on purpose: a front-end
/// may name an algorithm the engine does not ship (a Typst flow, an RDL report body) and register it with layout.
/// </summary>
/// <remarks>
/// Three ids are not algorithms of their own but the layout-internal display types of a tracks box (CSS Display 3
/// §2.4: <c>table-header-group</c>, <c>table-row-group</c>, <c>table-row</c>): the tracks algorithm lays them out
/// itself. Anywhere else they are invalid content, because no algorithm is registered under them.
/// </remarks>
/// <param name="Name">The algorithm name, for example <c>flow</c>.</param>
public readonly record struct LayoutAlgorithmId(string Name)
{
    /// <summary>Block flow: stacks child boxes from top to bottom (CSS 2.1 §9.4.1).</summary>
    public static LayoutAlgorithmId Flow => new("flow");

    /// <summary>Inline formatting context: sets text runs in lines (CSS 2.1 §9.4.2).</summary>
    public static LayoutAlgorithmId Inline => new("inline");

    /// <summary>
    /// Tracks: columns and rows of cells, for tables (CSS 2.1 §17) and for a plain row of blocks side by side. Its
    /// children are row groups; their children rows; their children the cells.
    /// </summary>
    public static LayoutAlgorithmId Tracks => new("tracks");

    /// <summary>A replaced element: one image (CSS 2.1 §10.3.2), its only child an image node.</summary>
    public static LayoutAlgorithmId Image => new("image");

    /// <summary>A group of header rows inside a tracks box, repeated on every page the table continues on
    /// (<c>display: table-header-group</c>, CSS 2.1 §17.2).</summary>
    public static LayoutAlgorithmId TableHeaderGroup => new("table-header-group");

    /// <summary>A group of body rows inside a tracks box (<c>display: table-row-group</c>).</summary>
    public static LayoutAlgorithmId TableRowGroup => new("table-row-group");

    /// <summary>A row inside a row group (<c>display: table-row</c>); its children are the cells.</summary>
    public static LayoutAlgorithmId TableRow => new("table-row");

    /// <summary>Returns the name.</summary>
    /// <returns>The text form.</returns>
    public override string ToString() => Name;
}
