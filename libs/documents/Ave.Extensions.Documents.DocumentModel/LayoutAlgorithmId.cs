namespace Ave.Extensions.Documents.DocumentModel;

/// <summary>
/// Names the layout algorithm of a box: its inner display type (CSS Display 3 §2.2). The engine ships six —
/// <see cref="Flow"/>, <see cref="Inline"/>, <see cref="Tracks"/>, <see cref="Image"/>, <see cref="MarginBoxes"/> and
/// <see cref="ListItem"/> —
/// and layout looks them up by this name. The parts of a table are not algorithms (<see cref="TableDisplay"/>).
/// </summary>
/// <param name="Name">The algorithm name, for example <c>flow</c>.</param>
public readonly record struct LayoutAlgorithmId(string Name)
{
    /// <summary>Block flow: stacks child boxes from top to bottom (CSS 2.1 §9.4.1).</summary>
    public static LayoutAlgorithmId Flow => new("flow");

    /// <summary>Inline formatting context: sets text runs, and image boxes among them, in lines (CSS 2.1 §9.4.2).</summary>
    public static LayoutAlgorithmId Inline => new("inline");

    /// <summary>
    /// Tracks: columns and rows of cells, for tables (CSS 2.1 §17) and for a plain row of blocks side by side. Its
    /// children are row groups; their children rows; their children the cells.
    /// </summary>
    public static LayoutAlgorithmId Tracks => new("tracks");

    /// <summary>A replaced element: one image (CSS 2.1 §10.3.2), its only child an image node.</summary>
    public static LayoutAlgorithmId Image => new("image");

    /// <summary>
    /// The page-margin boxes of one page edge side by side (CSS Paged Media 3 §5, the rows of the top and bottom edges): at most
    /// one child box at each place of <see cref="BoxStyle.MarginBoxPlace"/>, their widths by §5.3.2. Only the content
    /// of a header or footer area with a fixed height (<see cref="PageArea.Height"/>), whose height it takes.
    /// </summary>
    public static LayoutAlgorithmId MarginBoxes => new("margin-boxes");

    /// <summary>
    /// A list item (CSS Lists 3 §3, <c>display: list-item</c> with an outside marker): its body, a flow box, laid out as
    /// block flow, and its label beside the body's first line, outside the item's content box. Its children are an
    /// optional label (<see cref="Core.SemanticRole.ListLabel"/>) and then its body (<see cref="Core.SemanticRole.ListBody"/>).
    /// </summary>
    public static LayoutAlgorithmId ListItem => new("list-item");

    /// <summary>Returns the name.</summary>
    /// <returns>The text form.</returns>
    public override string ToString() => Name;
}
