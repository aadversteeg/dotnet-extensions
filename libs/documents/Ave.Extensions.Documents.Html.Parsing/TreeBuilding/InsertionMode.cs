namespace Ave.Extensions.Documents.Html.Parsing.TreeBuilding;

/// <summary>The insertion modes of tree construction (HTML Standard §13.2.4.1, §13.2.6.4.1–§13.2.6.4.21).</summary>
internal enum InsertionMode
{
    /// <summary>§13.2.6.4.1.</summary>
    Initial,

    /// <summary>§13.2.6.4.2.</summary>
    BeforeHtml,

    /// <summary>§13.2.6.4.3.</summary>
    BeforeHead,

    /// <summary>§13.2.6.4.4.</summary>
    InHead,

    /// <summary>§13.2.6.4.5.</summary>
    InHeadNoscript,

    /// <summary>§13.2.6.4.6.</summary>
    AfterHead,

    /// <summary>§13.2.6.4.7.</summary>
    InBody,

    /// <summary>§13.2.6.4.8.</summary>
    Text,

    /// <summary>§13.2.6.4.9.</summary>
    InTable,

    /// <summary>§13.2.6.4.10.</summary>
    InTableText,

    /// <summary>§13.2.6.4.11.</summary>
    InCaption,

    /// <summary>§13.2.6.4.12.</summary>
    InColumnGroup,

    /// <summary>§13.2.6.4.13.</summary>
    InTableBody,

    /// <summary>§13.2.6.4.14.</summary>
    InRow,

    /// <summary>§13.2.6.4.15.</summary>
    InCell,

    /// <summary>§13.2.6.4.16.</summary>
    InTemplate,

    /// <summary>§13.2.6.4.17.</summary>
    AfterBody,

    /// <summary>§13.2.6.4.18.</summary>
    InFrameset,

    /// <summary>§13.2.6.4.19.</summary>
    AfterFrameset,

    /// <summary>§13.2.6.4.20.</summary>
    AfterAfterBody,

    /// <summary>§13.2.6.4.21.</summary>
    AfterAfterFrameset,
}
