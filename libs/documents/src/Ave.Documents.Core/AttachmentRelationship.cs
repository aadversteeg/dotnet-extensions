namespace Ave.Documents.Core;

/// <summary>
/// How an embedded file relates to the document: the values of the file specification's <c>/AFRelationship</c> entry
/// (ISO 32000-2 §7.11.3, Table 43, for associated files, §14.13; PDF/A-3 takes the entry over from the then-draft
/// PDF 2.0 in ISO 19005-3 Annex E).
/// </summary>
public enum AttachmentRelationship
{
    /// <summary>The original content the document was made from (<c>/Source</c>), for example a spreadsheet.</summary>
    Source,

    /// <summary>Data the document shows, in machine-readable form (<c>/Data</c>), for example an invoice's XML.</summary>
    Data,

    /// <summary>Another representation of the same content (<c>/Alternative</c>), for example an audio version.</summary>
    Alternative,

    /// <summary>A supplement for processing that is not a representation of the content (<c>/Supplement</c>).</summary>
    Supplement,

    /// <summary>A relationship that is none of the above or not known (<c>/Unspecified</c>).</summary>
    Unspecified,
}
