using System;
using Ave.Documents.Core;

namespace Ave.Documents.Fluent;

/// <summary>
/// Modifiers for an <see cref="Attachment"/> made by <see cref="Elements.Attachment"/>. Each returns a copy with one
/// property set; they are named <c>With…</c> because the attachment has properties of the same names.
/// </summary>
public static class AttachmentModifiers
{
    /// <summary>Says how the file relates to the document; PDF/A-3 requires it.</summary>
    /// <param name="attachment">The attachment.</param>
    /// <param name="relationship">The relationship, for example <see cref="AttachmentRelationship.Data"/> for the
    /// document's data in machine-readable form.</param>
    /// <returns>A copy with the relationship.</returns>
    public static Attachment WithRelationship(this Attachment attachment, AttachmentRelationship relationship) =>
        attachment with { Relationship = relationship };

    /// <summary>Describes the file for people.</summary>
    /// <param name="attachment">The attachment.</param>
    /// <param name="description">The description.</param>
    /// <returns>A copy with the description.</returns>
    public static Attachment WithDescription(this Attachment attachment, string description) =>
        attachment with { Description = description };

    /// <summary>Says when the file was last changed. An input like every date in the engine, so output stays
    /// deterministic.</summary>
    /// <param name="attachment">The attachment.</param>
    /// <param name="modified">The date and time.</param>
    /// <returns>A copy with the modification date.</returns>
    public static Attachment WithModificationDate(this Attachment attachment, DateTimeOffset modified) =>
        attachment with { ModificationDate = modified };
}
