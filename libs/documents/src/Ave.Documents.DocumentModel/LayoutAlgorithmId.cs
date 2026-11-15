namespace Ave.Documents.DocumentModel;

/// <summary>
/// Names the layout algorithm of a box. OPEN on purpose: a front-end may name an algorithm the engine does not
/// ship (a Typst flow, an RDL report body) and register it with layout. The two that exist in stage 0 are below.
/// </summary>
/// <param name="Name">The algorithm name, for example <c>flow</c>.</param>
public readonly record struct LayoutAlgorithmId(string Name)
{
    /// <summary>Block flow: stacks child boxes from top to bottom (CSS 2.1 §9.4.1).</summary>
    public static LayoutAlgorithmId Flow => new("flow");

    /// <summary>Inline formatting context: sets text runs in lines (CSS 2.1 §9.4.2).</summary>
    public static LayoutAlgorithmId Inline => new("inline");

    /// <summary>Returns the name.</summary>
    /// <returns>The text form.</returns>
    public override string ToString() => Name;
}
