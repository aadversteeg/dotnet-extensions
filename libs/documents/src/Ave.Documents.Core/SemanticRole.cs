namespace Ave.Documents.Core;

/// <summary>
/// What a node MEANS, independent of how it is laid out. A box laid out as a table is not necessarily a table
/// for a screen reader; roles are therefore separate from layout. Roles follow the PDF standard structure types
/// (ISO 32000-1 §14.8.4) so that tagged PDF (a later stage) can be derived from them directly.
/// </summary>
/// <remarks>
/// A closed set: the private constructor means only the nested cases below exist, and a <c>switch</c> over a
/// role can be checked for completeness.
/// </remarks>
public abstract record SemanticRole
{
    private SemanticRole()
    {
    }

    /// <summary>A grouping with no meaning of its own (PDF structure type <c>Div</c>).</summary>
    public static SemanticRole Generic { get; } = new GenericRole();

    /// <summary>A paragraph (PDF structure type <c>P</c>).</summary>
    public static SemanticRole Paragraph { get; } = new ParagraphRole();

    /// <summary>A table (PDF structure type <c>Table</c>, ISO 32000-1 §14.8.4.3.4).</summary>
    public static SemanticRole Table { get; } = new NamedRole("table");

    /// <summary>The group of header rows of a table (PDF structure type <c>THead</c>).</summary>
    public static SemanticRole TableHead { get; } = new NamedRole("table-head");

    /// <summary>A group of body rows of a table (PDF structure type <c>TBody</c>).</summary>
    public static SemanticRole TableBody { get; } = new NamedRole("table-body");

    /// <summary>A row of a table (PDF structure type <c>TR</c>).</summary>
    public static SemanticRole TableRow { get; } = new NamedRole("table-row");

    /// <summary>A header cell of a table (PDF structure type <c>TH</c>).</summary>
    public static SemanticRole TableHeaderCell { get; } = new NamedRole("table-header-cell");

    /// <summary>A data cell of a table (PDF structure type <c>TD</c>).</summary>
    public static SemanticRole TableCell { get; } = new NamedRole("table-cell");

    /// <summary>A picture such as a logo or chart (PDF structure type <c>Figure</c>, §14.8.4.5).</summary>
    public static SemanticRole Figure { get; } = new NamedRole("figure");

    /// <summary>A heading of the given level (PDF structure types <c>H1</c>–<c>H6</c>).</summary>
    /// <param name="level">The level, 1 (most important) to 6. Front-ends validate the range.</param>
    /// <returns>The role.</returns>
    public static SemanticRole Heading(int level) => new HeadingRole(level);

    /// <summary>A grouping with no meaning of its own.</summary>
    public sealed record GenericRole : SemanticRole
    {
        /// <summary>Returns <c>generic</c>.</summary>
        /// <returns>The text form.</returns>
        public override string ToString() => "generic";
    }

    /// <summary>A paragraph.</summary>
    public sealed record ParagraphRole : SemanticRole
    {
        /// <summary>Returns <c>paragraph</c>.</summary>
        /// <returns>The text form.</returns>
        public override string ToString() => "paragraph";
    }

    /// <summary>
    /// A role without parameters other than the generic and paragraph roles (table parts, figure), identified by
    /// its name; the static properties above are the only instances, so roles still form a closed set.
    /// </summary>
    public sealed record NamedRole : SemanticRole
    {
        internal NamedRole(string name) => Name = name;

        /// <summary>The role's name, for example <c>table-row</c>.</summary>
        public string Name { get; }

        /// <summary>Returns the name.</summary>
        /// <returns>The text form.</returns>
        public override string ToString() => Name;
    }

    /// <summary>A heading.</summary>
    /// <param name="Level">The level, 1 (most important) to 6.</param>
    public sealed record HeadingRole(int Level) : SemanticRole
    {
        /// <summary>Returns <c>heading-N</c>.</summary>
        /// <returns>The text form.</returns>
        public override string ToString() => $"heading-{Level}";
    }
}
