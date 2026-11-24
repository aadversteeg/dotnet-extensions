namespace Ave.Extensions.Documents.Core;

/// <summary>
/// What a node MEANS, independent of how it is laid out. A box laid out as a table is not necessarily a table
/// for a screen reader; roles are therefore separate from layout. Roles are the PDF standard structure types
/// (ISO 32000-1 §14.8.4), so tagged PDF is derived from them directly: every role names its own standard type
/// (<see cref="StandardStructureType"/>), so no role can exist without one. <see cref="Decorative"/> is the one
/// role that is not structure: it marks content that is not part of the document's structure at all (an
/// "artifact", §14.8.2.2).
/// </summary>
/// <remarks>
/// One encoding: one sealed record per role, all nested here behind a private constructor, so the cases below are
/// the only ones. The roles without parameters are singletons (records without fields, so equal to each other).
/// </remarks>
public abstract record SemanticRole
{
    private SemanticRole()
    {
    }

    /// <summary>
    /// The standard structure type the role is written as in a tagged PDF (ISO 32000-1 §14.8.4): <c>Document</c>,
    /// <c>Div</c>, <c>P</c>, <c>H1</c>–<c>H6</c>, <c>Table</c>, <c>Caption</c>, <c>THead</c>, <c>TBody</c>, <c>TR</c>, <c>TH</c>,
    /// <c>TD</c>, <c>Figure</c>, <c>L</c>, <c>LI</c>, <c>Lbl</c>, <c>LBody</c>, <c>Link</c> or <c>Span</c>. Null for <see cref="Decorative"/>, which is no structure, and for a heading level
    /// outside 1–6, for which no standard type exists.
    /// </summary>
    public abstract string? StandardStructureType { get; }

    /// <summary>The whole document: the root of the structure (PDF structure type <c>Document</c>, §14.8.4.2).</summary>
    public static SemanticRole Document { get; } = new DocumentRole();

    /// <summary>A grouping with no meaning of its own (PDF structure type <c>Div</c>, §14.8.4.2).</summary>
    public static SemanticRole Generic { get; } = new GenericRole();

    /// <summary>A paragraph (PDF structure type <c>P</c>, §14.8.4.3.2, Table 335).</summary>
    public static SemanticRole Paragraph { get; } = new ParagraphRole();

    /// <summary>A table (PDF structure type <c>Table</c>, §14.8.4.3.4).</summary>
    public static SemanticRole Table { get; } = new TableRole();

    /// <summary>The group of header rows of a table (PDF structure type <c>THead</c>, §14.8.4.3.4).</summary>
    public static SemanticRole TableHead { get; } = new TableHeadRole();

    /// <summary>A group of body rows of a table (PDF structure type <c>TBody</c>, §14.8.4.3.4).</summary>
    public static SemanticRole TableBody { get; } = new TableBodyRole();

    /// <summary>A row of a table (PDF structure type <c>TR</c>, §14.8.4.3.4).</summary>
    public static SemanticRole TableRow { get; } = new TableRowRole();

    /// <summary>The caption of a table (PDF structure type <c>Caption</c>, §14.8.4.3.4, Table 337: the first or the last child of its table).</summary>
    public static SemanticRole Caption { get; } = new CaptionRole();

    /// <summary>A header cell of a table that heads its column (PDF structure type <c>TH</c>, §14.8.4.3.4), as a cell of a
    /// header row does.</summary>
    public static SemanticRole TableHeaderCell { get; } = new TableHeaderCellRole(TableHeaderScope.Column);

    /// <summary>A header cell of a table that heads what <paramref name="scope"/> says (PDF structure type <c>TH</c>,
    /// §14.8.4.3.4, with the scope of §14.8.5.7): its column, its row, or both.</summary>
    /// <param name="scope">What the cell heads. Front-ends pass a defined value; the model check refuses another.</param>
    /// <returns>The role; for <see cref="TableHeaderScope.Column"/> the same as <see cref="TableHeaderCell"/>.</returns>
    public static SemanticRole TableHeaderCellFor(TableHeaderScope scope) =>
        scope == TableHeaderScope.Column ? TableHeaderCell : new TableHeaderCellRole(scope);

    /// <summary>A data cell of a table (PDF structure type <c>TD</c>, §14.8.4.3.4).</summary>
    public static SemanticRole TableCell { get; } = new TableCellRole();

    /// <summary>
    /// Decoration that carries no information, such as an ornamental image: not part of the document structure, so a
    /// screen reader skips it (tagged PDF: a layout artifact, ISO 32000-1 §14.8.2.2.2).
    /// </summary>
    public static SemanticRole Decorative { get; } = new DecorativeRole();

    /// <summary>A picture such as a logo or chart (PDF structure type <c>Figure</c>, §14.8.4.5).</summary>
    /// <param name="alternativeText">What the picture shows, in words, for readers who cannot see it (§14.9.3,
    /// the <c>/Alt</c> entry); null when the author gave none. PDF/UA refuses a figure without it.</param>
    /// <returns>The role.</returns>
    public static SemanticRole Figure(string? alternativeText) => new FigureRole(alternativeText);

    /// <summary>
    /// A link (PDF structure type <c>Link</c>, §14.8.4.4.2): the role of an inline group, never of a box. In a PDF it is a
    /// link annotation per part of the link on a line (§12.5.6.5), each with its description (<c>/Contents</c>, §12.5.2,
    /// Table 164; ISO 14289-1 §7.18.5), and a <c>Link</c> element that holds the link's content and an object reference to
    /// each annotation (§14.7.4.3).
    /// </summary>
    /// <param name="target">Where it leads.</param>
    /// <param name="description">The link in words, for readers who cannot see it: the element's title, else its text,
    /// else the alternative text of an image in it, else its URI. Front-ends fill it in by that order; it is never
    /// blank.</param>
    /// <returns>The role.</returns>
    public static SemanticRole Link(LinkTarget target, string description) => new LinkRole(target, description);

    /// <summary>A span of text with no meaning of its own beyond what it carries, such as a language (PDF structure type
    /// <c>Span</c>, §14.8.4.4.1): the role of an inline group, never of a box.</summary>
    public static SemanticRole Span { get; } = new SpanRole();

    /// <summary>A heading of the given level (PDF structure types <c>H1</c>–<c>H6</c>, §14.8.4.3.2).</summary>
    /// <param name="level">The level, 1 (most important) to 6. Front-ends validate the range.</param>
    /// <returns>The role.</returns>
    public static SemanticRole Heading(int level) => new HeadingRole(level);

    /// <summary>A list (PDF structure type <c>L</c>, ISO 32000-1 §14.8.4.3.3): its children are list items.</summary>
    /// <param name="numbering">How its items are numbered or marked, written as the list attribute <c>ListNumbering</c>
    /// (§14.8.5.5, Table 347).</param>
    /// <returns>The role.</returns>
    public static SemanticRole List(ListNumbering numbering) => new ListRole(numbering);

    /// <summary>One item of a list (PDF structure type <c>LI</c>, §14.8.4.3.3): an optional label, then its body.</summary>
    public static SemanticRole ListItem { get; } = new ListItemRole();

    /// <summary>The label of a list item, a bullet or a number (PDF structure type <c>Lbl</c>, §14.8.4.3.3).</summary>
    /// <param name="replacementText">The text a drawn bullet stands for (the structure element's <c>/ActualText</c>,
    /// §14.9.4), or null when the label is text itself.</param>
    /// <returns>The role.</returns>
    public static SemanticRole ListLabel(string? replacementText) => new ListLabelRole(replacementText);

    /// <summary>The content of a list item (PDF structure type <c>LBody</c>, §14.8.4.3.3).</summary>
    public static SemanticRole ListBody { get; } = new ListBodyRole();

    /// <summary>The role of <see cref="Document"/>.</summary>
    public sealed record DocumentRole : SemanticRole
    {
        /// <summary>Returns <c>Document</c>.</summary>
        public override string? StandardStructureType => "Document";

        /// <summary>Returns <c>document</c>.</summary>
        /// <returns>The text form.</returns>
        public override string ToString() => "document";
    }

    /// <summary>The role of <see cref="Generic"/>.</summary>
    public sealed record GenericRole : SemanticRole
    {
        /// <summary>Returns <c>Div</c>.</summary>
        public override string? StandardStructureType => "Div";

        /// <summary>Returns <c>generic</c>.</summary>
        /// <returns>The text form.</returns>
        public override string ToString() => "generic";
    }

    /// <summary>The role of <see cref="Paragraph"/>.</summary>
    public sealed record ParagraphRole : SemanticRole
    {
        /// <summary>Returns <c>P</c>.</summary>
        public override string? StandardStructureType => "P";

        /// <summary>Returns <c>paragraph</c>.</summary>
        /// <returns>The text form.</returns>
        public override string ToString() => "paragraph";
    }

    /// <summary>The role of <see cref="Table"/>.</summary>
    public sealed record TableRole : SemanticRole
    {
        /// <summary>Returns <c>Table</c>.</summary>
        public override string? StandardStructureType => "Table";

        /// <summary>Returns <c>table</c>.</summary>
        /// <returns>The text form.</returns>
        public override string ToString() => "table";
    }

    /// <summary>The role of <see cref="TableHead"/>.</summary>
    public sealed record TableHeadRole : SemanticRole
    {
        /// <summary>Returns <c>THead</c>.</summary>
        public override string? StandardStructureType => "THead";

        /// <summary>Returns <c>table-head</c>.</summary>
        /// <returns>The text form.</returns>
        public override string ToString() => "table-head";
    }

    /// <summary>The role of <see cref="TableBody"/>.</summary>
    public sealed record TableBodyRole : SemanticRole
    {
        /// <summary>Returns <c>TBody</c>.</summary>
        public override string? StandardStructureType => "TBody";

        /// <summary>Returns <c>table-body</c>.</summary>
        /// <returns>The text form.</returns>
        public override string ToString() => "table-body";
    }

    /// <summary>The role of <see cref="TableRow"/>.</summary>
    public sealed record TableRowRole : SemanticRole
    {
        /// <summary>Returns <c>TR</c>.</summary>
        public override string? StandardStructureType => "TR";

        /// <summary>Returns <c>table-row</c>.</summary>
        /// <returns>The text form.</returns>
        public override string ToString() => "table-row";
    }

    /// <summary>The role of <see cref="Caption"/>.</summary>
    public sealed record CaptionRole : SemanticRole
    {
        /// <summary>Returns <c>Caption</c>.</summary>
        public override string? StandardStructureType => "Caption";

        /// <summary>Returns <c>caption</c>.</summary>
        /// <returns>The text form.</returns>
        public override string ToString() => "caption";
    }

    /// <summary>The role of <see cref="TableHeaderCell"/> and <see cref="TableHeaderCellFor"/>.</summary>
    /// <param name="Scope">What the cell heads.</param>
    public sealed record TableHeaderCellRole(TableHeaderScope Scope) : SemanticRole
    {
        /// <summary>Returns <c>TH</c>.</summary>
        public override string? StandardStructureType => "TH";

        /// <summary>Returns <c>table-header-cell</c>, with its scope when it is not the column.</summary>
        /// <returns>The text form.</returns>
        public override string ToString() => Scope switch
        {
            TableHeaderScope.Column => "table-header-cell",
            TableHeaderScope.Row => "table-header-cell scope=row",
            TableHeaderScope.Both => "table-header-cell scope=both",
            _ => $"table-header-cell scope={(int)Scope}",
        };
    }

    /// <summary>The role of <see cref="TableCell"/>.</summary>
    public sealed record TableCellRole : SemanticRole
    {
        /// <summary>Returns <c>TD</c>.</summary>
        public override string? StandardStructureType => "TD";

        /// <summary>Returns <c>table-cell</c>.</summary>
        /// <returns>The text form.</returns>
        public override string ToString() => "table-cell";
    }

    /// <summary>The role of <see cref="Decorative"/>.</summary>
    public sealed record DecorativeRole : SemanticRole
    {
        /// <summary>Null: decoration is no structure.</summary>
        public override string? StandardStructureType => null;

        /// <summary>Returns <c>decorative</c>.</summary>
        /// <returns>The text form.</returns>
        public override string ToString() => "decorative";
    }

    /// <summary>A picture.</summary>
    /// <param name="AlternativeText">What it shows, in words, or null.</param>
    public sealed record FigureRole(string? AlternativeText) : SemanticRole
    {
        /// <summary>Returns <c>Figure</c>.</summary>
        public override string? StandardStructureType => "Figure";

        /// <summary>Returns <c>figure</c>, with the alternative text when there is one.</summary>
        /// <returns>The text form.</returns>
        public override string ToString() => AlternativeText is null ? "figure" : $"figure alt={QuotedText.Quote(AlternativeText)}";
    }

    /// <summary>A link.</summary>
    /// <param name="Target">Where it leads.</param>
    /// <param name="Description">The link in words.</param>
    public sealed record LinkRole(LinkTarget Target, string Description) : SemanticRole
    {
        /// <summary>Returns <c>Link</c>.</summary>
        public override string? StandardStructureType => "Link";

        /// <summary>Returns <c>link</c> with its target and description.</summary>
        /// <returns>The text form.</returns>
        public override string ToString() => $"link {Target} description={QuotedText.Quote(Description)}";
    }

    /// <summary>The role of <see cref="Span"/>.</summary>
    public sealed record SpanRole : SemanticRole
    {
        /// <summary>Returns <c>Span</c>.</summary>
        public override string? StandardStructureType => "Span";

        /// <summary>Returns <c>span</c>.</summary>
        /// <returns>The text form.</returns>
        public override string ToString() => "span";
    }

    /// <summary>A heading.</summary>
    /// <param name="Level">The level, 1 (most important) to 6.</param>
    public sealed record HeadingRole(int Level) : SemanticRole
    {
        /// <summary>Returns <c>H1</c>–<c>H6</c>; null for a level outside 1–6, which has no standard type.</summary>
        public override string? StandardStructureType => Level is >= 1 and <= 6 ? $"H{Level}" : null;

        /// <summary>Returns <c>heading-N</c>.</summary>
        /// <returns>The text form.</returns>
        public override string ToString() => $"heading-{Level}";
    }

    /// <summary>A list.</summary>
    /// <param name="Numbering">How its items are numbered or marked.</param>
    public sealed record ListRole(ListNumbering Numbering) : SemanticRole
    {
        /// <summary>Returns <c>L</c>.</summary>
        public override string? StandardStructureType => "L";

        /// <summary>Returns <c>list numbering=…</c>.</summary>
        /// <returns>The text form.</returns>
        public override string ToString() => $"list numbering={Numbering.ToString().ToLowerInvariant()}";
    }

    /// <summary>The role of <see cref="ListItem"/>.</summary>
    public sealed record ListItemRole : SemanticRole
    {
        /// <summary>Returns <c>LI</c>.</summary>
        public override string? StandardStructureType => "LI";

        /// <summary>Returns <c>list-item</c>.</summary>
        /// <returns>The text form.</returns>
        public override string ToString() => "list-item";
    }

    /// <summary>The label of a list item.</summary>
    /// <param name="ReplacementText">The text a drawn bullet stands for, or null.</param>
    public sealed record ListLabelRole(string? ReplacementText) : SemanticRole
    {
        /// <summary>Returns <c>Lbl</c>.</summary>
        public override string? StandardStructureType => "Lbl";

        /// <summary>Returns <c>list-label</c>, with the replacement text when there is one.</summary>
        /// <returns>The text form.</returns>
        public override string ToString() => ReplacementText is null ? "list-label" : $"list-label actual-text={QuotedText.Quote(ReplacementText)}";
    }

    /// <summary>The role of <see cref="ListBody"/>.</summary>
    public sealed record ListBodyRole : SemanticRole
    {
        /// <summary>Returns <c>LBody</c>.</summary>
        public override string? StandardStructureType => "LBody";

        /// <summary>Returns <c>list-body</c>.</summary>
        /// <returns>The text form.</returns>
        public override string ToString() => "list-body";
    }
}
