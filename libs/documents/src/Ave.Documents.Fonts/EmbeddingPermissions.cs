namespace Ave.Documents.Fonts;

/// <summary>
/// What the font's licence allows when embedding it in a document: the <c>fsType</c> bits of the 'OS/2' table
/// (OpenType spec, "OS/2" → "fsType").
/// </summary>
/// <param name="FsType">The raw fsType value.</param>
public sealed record EmbeddingPermissions(ushort FsType)
{
    // fsType bits: 0x0002 restricted licence embedding, 0x0004 preview & print, 0x0008 editable,
    // 0x0200 bitmap embedding only. Installable embedding is fsType == 0 (no bits set).
    private const ushort Restricted = 0x0002;
    private const ushort PreviewAndPrint = 0x0004;
    private const ushort Editable = 0x0008;
    private const ushort BitmapOnly = 0x0200;

    /// <summary>
    /// True when the outlines may be embedded. Not allowed when only "Restricted License embedding" is set
    /// ("must not be modified, embedded or exchanged in any manner without first obtaining permission"), or when
    /// "Bitmap embedding only" is set, because a PDF embeds outlines. The spec says that when several of the
    /// usage bits are set, the least restrictive one applies, so Restricted together with Preview &amp; Print or
    /// Editable does allow embedding.
    /// </summary>
    public bool AllowsOutlineEmbedding =>
        (FsType & BitmapOnly) == 0
        && ((FsType & Restricted) == 0 || (FsType & (PreviewAndPrint | Editable)) != 0);
}
