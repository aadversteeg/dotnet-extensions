using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Ave.Documents.Core;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Pdf.Xmp;

/// <summary>
/// An XMP packet (ISO 16684-1 §7, RDF/XML serialization) holding the properties of one resource: the PDF document.
/// Written as the document's metadata stream (ISO 32000-1 §14.3.2). Pure: the same properties give the same bytes.
/// </summary>
/// <param name="Properties">The top-level properties, in output order. Properties of one namespace are written in one
/// <c>rdf:Description</c>, in the order their namespaces first appear.</param>
public sealed record XmpPacket(ValueList<XmpProperty> Properties)
{
    /// <summary>The packet wrapper's id (ISO 16684-1 §7.3.2: "the value of id shall be W5M0MpCehiHzreSzNTczkc9d").</summary>
    private const string PacketId = "W5M0MpCehiHzreSzNTczkc9d";

    /// <summary>
    /// Writes the packet as UTF-8 (veraPDF rule 6.6.2.1-5: "The XMP package must be encoded as UTF-8"): the
    /// <c>xpacket</c> header with the byte order mark as the begin attribute and without the bytes or encoding
    /// attributes (rules 6.6.2.1-2 and -3), the <c>x:xmpmeta</c> element, one <c>rdf:RDF</c>, one <c>rdf:Description
    /// rdf:about=""</c> per namespace, and the read-only trailer <c>end="w"</c>. No padding is added: the engine writes
    /// new files and never edits metadata in place.
    /// </summary>
    /// <returns>The packet bytes; <c>Documents.Pdf.Write.InvalidText</c> for every value with a character XML 1.0 does
    /// not allow (§2.2, production [2] Char), which no XML writer can write.</returns>
    public Result<ImmutableArray<byte>, Error> Write()
    {
        var problems = Properties.SelectMany(property => Problems(property, Prefixed(property))).ToImmutableArray();
        return problems.IsEmpty
            ? Result<ImmutableArray<byte>, Error>.Success(Serialize())
            : Result<ImmutableArray<byte>, Error>.Failure(Errors.Aggregate(problems));
    }

    /// <summary>The checked properties as RDF/XML.</summary>
    private ImmutableArray<byte> Serialize()
    {
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            OmitXmlDeclaration = true,
            Indent = true,
            IndentChars = " ",
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace,
        };
        using var output = new MemoryStream();
        using (var xml = XmlWriter.Create(output, settings))
        {
            xml.WriteProcessingInstruction("xpacket", $"begin=\"\uFEFF\" id=\"{PacketId}\"");
            xml.WriteStartElement(XmpNamespace.Meta.Prefix, "xmpmeta", XmpNamespace.Meta.Uri);
            xml.WriteStartElement(XmpNamespace.Rdf.Prefix, "RDF", XmpNamespace.Rdf.Uri);
            foreach (var schema in Properties.GroupBy(property => property.Namespace))
            {
                WriteDescription(xml, schema.Key, [.. schema]);
            }

            xml.WriteEndElement();
            xml.WriteEndElement();
            xml.WriteProcessingInstruction("xpacket", "end=\"w\"");
        }

        return [.. output.ToArray()];
    }

    /// <summary>The forbidden characters in a property's text, per text value: arrays and structures are searched
    /// through. <paramref name="name"/> is the prefixed name of the top-level property, for the message.</summary>
    private static IEnumerable<Error> Problems(XmpProperty property, string name) => Texts(property.Value)
        .Select(text => (Text: text, Offset: XmlCharacters.IndexOfForbidden(text)))
        .Where(found => found.Offset is not null)
        .Select(found => new Error(DocumentErrorCodes.Pdf.Write.InvalidText, $"The XMP property {name} holds a character XML does not allow, at position {found.Offset}.")
            .With(ErrorMetadataKeys.Property, name)
            .With(ErrorMetadataKeys.Offset, found.Offset!.Value));

    private static IEnumerable<string> Texts(XmpValue value) => value switch
    {
        XmpText text => [text.Text],
        XmpLanguageAlternative alternative => [alternative.Text],
        XmpOrderedArray array => array.Items.SelectMany(Texts),
        XmpUnorderedArray array => array.Items.SelectMany(Texts),
        XmpStructure structure => structure.Fields.SelectMany(field => Texts(field.Value)),
        _ => throw new InvalidOperationException($"Unknown XMP value {value.GetType().Name}."),
    };

    private static string Prefixed(XmpProperty property) => $"{property.Namespace.Prefix}:{property.Name}";

    /// <summary>One <c>rdf:Description</c>; every namespace its properties use is declared on it, so the output does
    /// not depend on where the XML writer would choose to declare them.</summary>
    private static void WriteDescription(XmlWriter xml, XmpNamespace schema, ImmutableArray<XmpProperty> properties)
    {
        xml.WriteStartElement(XmpNamespace.Rdf.Prefix, "Description", XmpNamespace.Rdf.Uri);
        xml.WriteAttributeString(XmpNamespace.Rdf.Prefix, "about", XmpNamespace.Rdf.Uri, string.Empty);
        foreach (var used in properties.SelectMany(NamespacesOf).Prepend(schema).Distinct())
        {
            xml.WriteAttributeString("xmlns", used.Prefix, null, used.Uri);
        }

        foreach (var property in properties)
        {
            WriteProperty(xml, property);
        }

        xml.WriteEndElement();
    }

    private static IEnumerable<XmpNamespace> NamespacesOf(XmpProperty property) =>
        [property.Namespace, .. NamespacesOf(property.Value)];

    private static IEnumerable<XmpNamespace> NamespacesOf(XmpValue value) => value switch
    {
        XmpOrderedArray array => array.Items.SelectMany(NamespacesOf),
        XmpUnorderedArray array => array.Items.SelectMany(NamespacesOf),
        XmpStructure structure => structure.Fields.SelectMany(NamespacesOf),
        _ => [],
    };

    /// <summary>
    /// A property element (ISO 16684-1 §7.9.2): a simple value as element text; an array as a <c>rdf:Seq</c>,
    /// <c>rdf:Bag</c> or <c>rdf:Alt</c> of <c>rdf:li</c> items; a structure as the fields inside an element with
    /// <c>rdf:parseType="Resource"</c> (§7.9.2.4).
    /// </summary>
    private static void WriteProperty(XmlWriter xml, XmpProperty property)
    {
        xml.WriteStartElement(property.Namespace.Prefix, property.Name, property.Namespace.Uri);
        WriteValue(xml, property.Value);
        xml.WriteEndElement();
    }

    private static void WriteValue(XmlWriter xml, XmpValue value)
    {
        switch (value)
        {
            case XmpText text:
                xml.WriteString(text.Text);
                break;
            case XmpLanguageAlternative alternative:
                WriteArray(xml, "Alt", [alternative], item =>
                {
                    xml.WriteAttributeString("xml", "lang", null, "x-default");
                    xml.WriteString(((XmpLanguageAlternative)item).Text);
                });
                break;
            case XmpOrderedArray array:
                WriteArray(xml, "Seq", array.Items, item => WriteValue(xml, item));
                break;
            case XmpUnorderedArray array:
                WriteArray(xml, "Bag", array.Items, item => WriteValue(xml, item));
                break;
            case XmpStructure structure:
                xml.WriteAttributeString(XmpNamespace.Rdf.Prefix, "parseType", XmpNamespace.Rdf.Uri, "Resource");
                foreach (var field in structure.Fields)
                {
                    WriteProperty(xml, field);
                }

                break;
            default:
                throw new InvalidOperationException($"Unknown XMP value {value.GetType().Name}.");
        }
    }

    /// <summary>An RDF container; each item is an <c>rdf:li</c> element whose content <paramref name="writeItem"/>
    /// writes (a structure item puts its <c>rdf:parseType</c> on the <c>rdf:li</c> itself).</summary>
    private static void WriteArray(XmlWriter xml, string container, IEnumerable<XmpValue> items, Action<XmpValue> writeItem)
    {
        xml.WriteStartElement(XmpNamespace.Rdf.Prefix, container, XmpNamespace.Rdf.Uri);
        foreach (var item in items)
        {
            xml.WriteStartElement(XmpNamespace.Rdf.Prefix, "li", XmpNamespace.Rdf.Uri);
            writeItem(item);
            xml.WriteEndElement();
        }

        xml.WriteEndElement();
    }
}
