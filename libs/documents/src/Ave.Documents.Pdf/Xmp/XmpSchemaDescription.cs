using System.Collections.Generic;
using System.Linq;
using Ave.Documents.Core;

namespace Ave.Documents.Pdf.Xmp;

/// <summary>
/// The description of an XMP schema that PDF/A does not predefine. PDF/A accepts a property only from the predefined
/// schemas or from a schema described in the file itself (ISO 19005-3 §6.6.2.3.1, veraPDF rule 6.6.2.3.1-1), with the
/// extension schema container of §6.6.2.3.3. This is how a new schema (PDF/UA identification now, Factur-X later) is
/// added: describe it here, write its properties, and <see cref="ContainerProperty"/> lists the description.
/// </summary>
/// <param name="Name">What the schema is, for people (field <c>pdfaSchema:schema</c>).</param>
/// <param name="Namespace">Its namespace and prefix (<c>pdfaSchema:namespaceURI</c>, <c>pdfaSchema:prefix</c>).</param>
/// <param name="Properties">The properties it defines (<c>pdfaSchema:property</c>).</param>
public sealed record XmpSchemaDescription(string Name, XmpNamespace Namespace, ValueList<XmpPropertyDescription> Properties)
{
    /// <summary>
    /// The PDF/UA identification schema (ISO 14289-1 §5), which PDF/A-3 does not predefine: a file that claims both
    /// standards must describe it. Its one required property is <c>part</c>, an Integer.
    /// </summary>
    public static XmpSchemaDescription PdfUaIdentification { get; } = new(
        "PDF/UA Universal Accessibility Schema",
        XmpNamespace.PdfUaIdentification,
        [new XmpPropertyDescription("part", "Integer", "Indicates, which part of ISO 14289 standard is followed", IsInternal: true)]);

    /// <summary>
    /// The <c>pdfaExtension:schemas</c> property (ISO 19005-3 §6.6.2.3.3, veraPDF rules 6.6.2.3.3-1 to -10): a bag of
    /// schema descriptions, each with its name, namespace URI, prefix and a sequence of property descriptions.
    /// </summary>
    /// <param name="schemas">The schemas to describe; null is read as none. A missing schema, or a missing part of one,
    /// becomes a missing value, which <see cref="XmpPacket.Write"/> refuses.</param>
    /// <returns>The property.</returns>
    public static XmpProperty ContainerProperty(IEnumerable<XmpSchemaDescription?>? schemas) =>
        new(XmpNamespace.PdfAExtension, "schemas", new XmpUnorderedArray([.. (schemas ?? []).Select(schema => schema?.Describe() ?? new XmpStructure([null!]))]));

    private XmpValue Describe() => new XmpStructure(
    [
        new XmpProperty(XmpNamespace.PdfASchema, "schema", new XmpText(Name)),
        new XmpProperty(XmpNamespace.PdfASchema, "namespaceURI", new XmpText(Namespace?.Uri!)),
        new XmpProperty(XmpNamespace.PdfASchema, "prefix", new XmpText(Namespace?.Prefix!)),
        new XmpProperty(XmpNamespace.PdfASchema, "property", new XmpOrderedArray([.. Properties.Select(property => property?.Describe() ?? new XmpStructure([null!]))])),
    ]);
}

/// <summary>One property of an <see cref="XmpSchemaDescription"/> (ISO 19005-3 §6.6.2.3.3, the PDF/A Property value type).</summary>
/// <param name="Name">The property's local name.</param>
/// <param name="ValueType">Its XMP value type, for example <c>Text</c> or <c>Integer</c>.</param>
/// <param name="Description">What it means, for people.</param>
/// <param name="IsInternal">True when the value is computed by software from the file (category <c>internal</c>); false
/// when a person or another system provides it (<c>external</c>).</param>
public sealed record XmpPropertyDescription(string Name, string ValueType, string Description, bool IsInternal)
{
    internal XmpValue Describe() => new XmpStructure(
    [
        new XmpProperty(XmpNamespace.PdfAProperty, "name", new XmpText(Name)),
        new XmpProperty(XmpNamespace.PdfAProperty, "valueType", new XmpText(ValueType)),
        new XmpProperty(XmpNamespace.PdfAProperty, "category", new XmpText(IsInternal ? "internal" : "external")),
        new XmpProperty(XmpNamespace.PdfAProperty, "description", new XmpText(Description)),
    ]);
}
