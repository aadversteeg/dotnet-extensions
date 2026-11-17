using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Pdf.Xmp;

/// <summary>
/// An XMP packet (ISO 16684-1 §7, RDF/XML serialization) holding the properties of one resource: the PDF document.
/// Written as the document's metadata stream (ISO 32000-1 §14.3.2). Pure: the same properties give the same bytes.
/// </summary>
/// <param name="Properties">The top-level properties, in output order. Properties of one namespace are written in one
/// <c>rdf:Description</c>, in the order their namespaces first appear.</param>
public sealed record XmpPacket(ValueList<XmpProperty> Properties)
{
    /// <summary>The packet wrapper's id (ISO 16684-1 §7.3.2: "The header PI shall be an XML processing instruction
    /// of exactly the form shown in Figure 6", whose id is W5M0MpCehiHzreSzNTczkc9d).</summary>
    private const string PacketId = "W5M0MpCehiHzreSzNTczkc9d";

    /// <summary>
    /// The deepest a property's value may nest arrays and structures, the value itself being level 1: a limit of the
    /// writer's own, as <see cref="PdfSerializer.MaxNestingDepth"/> is for PDF objects. Every walk over a value (the
    /// checks and the writing) recurses once per level, so the limit keeps them far from the stack limit whatever value a
    /// caller builds. The engine's own metadata nests at most four levels (the PDF/A extension schema: a bag of
    /// structures holding a sequence of structures), so 64 never refuses it.
    /// </summary>
    public const int MaxNestingDepth = 64;

    /// <summary>
    /// Writes the packet as UTF-8 (veraPDF rule 6.6.2.1-5: "The XMP package must be encoded as UTF-8"): the
    /// <c>xpacket</c> header with the byte order mark as the begin attribute and without the bytes or encoding
    /// attributes (rules 6.6.2.1-2 and -3), the <c>x:xmpmeta</c> element, one <c>rdf:RDF</c>, one <c>rdf:Description
    /// rdf:about=""</c> per namespace, and the read-only trailer <c>end="w"</c>. No padding is added: the engine writes
    /// new files and never edits metadata in place.
    /// </summary>
    /// <returns>The packet bytes; <c>Documents.Pdf.Write.NestedTooDeep</c> for the first property whose value nests
    /// deeper than <see cref="MaxNestingDepth"/>, checked before anything else; <c>Documents.Pdf.Write.InvalidPropertyName</c> for every property or field whose name,
    /// namespace or value is missing or whose name or prefix is not an XML name (an NCName, which an element name must
    /// be), and for namespaces no XML writer can declare (<see cref="NamespaceProblems"/>);
    /// <c>Documents.Pdf.Write.InvalidText</c> for every value with a character XML 1.0 does not allow (§2.2,
    /// production [2] Char). No XML writer can write either.</returns>
    public Result<ImmutableArray<byte>, Error> Write()
    {
        var properties = Properties.ToImmutableArray();
        if (properties.Select(property => property is null ? null : TooDeep(property, property.Value, level: 1)).FirstOrDefault(problem => problem is not null) is { } tooDeep)
        {
            return Result<ImmutableArray<byte>, Error>.Failure(tooDeep);
        }

        var nameProblems = properties.SelectMany(NameProblems).ToImmutableArray();
        nameProblems = nameProblems.IsEmpty ? NamespaceProblems(properties).ToImmutableArray() : nameProblems;
        var problems = nameProblems.IsEmpty
            ? Properties.SelectMany(property => Problems(property, Prefixed(property))).ToImmutableArray()
            : nameProblems;
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

    /// <summary>
    /// The nesting error for a top-level <paramref name="property"/> whose <paramref name="value"/> (at
    /// <paramref name="level"/>) nests arrays and structures deeper than <see cref="MaxNestingDepth"/>, or null. It stops
    /// at the first level past the limit, so it never recurses deeper than that itself. Missing parts are left to the
    /// other checks.
    /// </summary>
    private static Error? TooDeep(XmpProperty property, XmpValue? value, int level) => value switch
    {
        XmpOrderedArray or XmpUnorderedArray or XmpStructure when level > MaxNestingDepth => NestedTooDeep(property),
        XmpOrderedArray array => FirstTooDeep(property, array.Items, level + 1),
        XmpUnorderedArray array => FirstTooDeep(property, array.Items, level + 1),
        XmpStructure structure => FirstTooDeep(property, structure.Fields.Select(field => field?.Value), level + 1),
        _ => null,
    };

    private static Error? FirstTooDeep(XmpProperty property, IEnumerable<XmpValue?> values, int level) =>
        values.Select(value => TooDeep(property, value, level)).FirstOrDefault(problem => problem is not null);

    private static Error NestedTooDeep(XmpProperty property)
    {
        var name = $"{property.Namespace?.Prefix}:{property.Name}";
        return new Error(PdfErrorCodes.Write.NestedTooDeep, $"The XMP property {name} nests arrays and structures more than {MaxNestingDepth} levels deep.")
            .With(ErrorMetadataKeys.Value, MaxNestingDepth.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .With(ErrorMetadataKeys.Property, name);
    }

    /// <summary>A property and, through arrays and structures, every field it holds: a namespace with an NCName prefix,
    /// an NCName name, and a value of a known kind.</summary>
    private static IEnumerable<Error> NameProblems(XmpProperty? property) =>
        property is null
            ? [InvalidName("a missing property")]
            : property.Namespace is null || !IsNcName(property.Namespace.Prefix) || string.IsNullOrEmpty(property.Namespace.Uri)
                ? [InvalidName($"the property {property.Name} without a namespace that has an XML prefix and a URI")]
                : !IsNcName(property.Name)
                    ? [InvalidName($"the property name '{property.Namespace.Prefix}:{property.Name}', which is not an XML name")]
                    : ValueNameProblems(property.Value, Prefixed(property));

    private static IEnumerable<Error> ValueNameProblems(XmpValue? value, string name) => value switch
    {
        XmpText { Text: null } or XmpLanguageAlternative { Text: null } => [InvalidName($"the property {name} without a text")],
        XmpText or XmpLanguageAlternative => [],
        XmpOrderedArray array => array.Items.SelectMany(item => ValueNameProblems(item, name)),
        XmpUnorderedArray array => array.Items.SelectMany(item => ValueNameProblems(item, name)),
        XmpStructure structure => structure.Fields.SelectMany(NameProblems),
        null => [InvalidName($"the property {name} without a value")],
        _ => [InvalidName($"the property {name}, whose value is a {value.GetType().Name}, which XMP does not know")],
    };

    /// <summary>The XML namespace's own URI, which the prefix <c>xml</c> is bound to by definition.</summary>
    private const string XmlNamespaceUri = "http://www.w3.org/XML/1998/namespace";

    /// <summary>The namespace name the prefix xmlns is bound to by definition (XML Namespaces 1.0 §3).</summary>
    private const string XmlnsNamespaceUri = "http://www.w3.org/2000/xmlns/";

    /// <summary>
    /// The namespaces of the packet as an XML writer must declare them (Namespaces in XML 1.0, §3): "The prefix xml is
    /// by definition bound to the namespace name http://www.w3.org/XML/1998/namespace" and "The prefix xmlns is used only
    /// to declare namespace bindings and is by definition bound to the namespace name http://www.w3.org/2000/xmlns/. It
    /// MUST NOT be declared." So neither may name a property's namespace; the packet itself uses rdf and x, which keep
    /// their URIs; and every namespace is declared on one element (<see cref="WriteDescription"/>), where one prefix can
    /// name one URI only, so a prefix names the same URI wherever it is used. A URI is an attribute value: only characters
    /// XML allows.
    /// </summary>
    private static IEnumerable<Error> NamespaceProblems(ImmutableArray<XmpProperty> properties)
    {
        var used = properties.SelectMany(NamespacesOf).Distinct().ToList();
        var reserved = used.Where(space => space.Prefix is "xml" or "xmlns")
            .Select(space => InvalidName($"the namespace prefix {space.Prefix}, which XML binds by definition (xml to {XmlNamespaceUri})"));
        var reservedUris = used.Where(space => space.Prefix is not ("xml" or "xmlns") && space.Uri is XmlNamespaceUri or XmlnsNamespaceUri)
            .Select(space => InvalidName($"the namespace URI {space.Uri} for the prefix {space.Prefix}: XML binds it to {(space.Uri == XmlNamespaceUri ? "xml" : "xmlns")} only"));
        var badUris = used.Where(space => XmlCharacters.IndexOfForbidden(space.Uri) is not null)
            .Select(space => InvalidName($"the namespace URI of {space.Prefix}, which holds a character XML does not allow"));
        var bound = used.Concat([XmpNamespace.Rdf, XmpNamespace.Meta])
            .Where(space => space.Prefix is not ("xml" or "xmlns"))
            .GroupBy(space => space.Prefix)
            .Where(group => group.Select(space => space.Uri).Distinct().Count() > 1)
            .Select(group => InvalidName($"the namespace prefix {group.Key} for more than one URI ({string.Join(", ", group.Select(space => space.Uri).Distinct())})"));
        return reserved.Concat(reservedUris).Concat(badUris).Concat(bound);
    }

    private static bool IsNcName(string? name) =>
        !string.IsNullOrEmpty(name) && XmlConvert.IsStartNCNameChar(name[0]) && name.All(XmlConvert.IsNCNameChar);

    private static Error InvalidName(string what) =>
        new Error(PdfErrorCodes.Write.InvalidPropertyName, $"The XMP packet cannot hold {what}.").With(ErrorMetadataKeys.Property, what);

    /// <summary>The forbidden characters in a property's text, per text value: arrays and structures are searched
    /// through. <paramref name="name"/> is the prefixed name of the top-level property, for the message.</summary>
    private static IEnumerable<Error> Problems(XmpProperty property, string name) => Texts(property.Value)
        .Select(text => (Text: text, Offset: XmlCharacters.IndexOfForbidden(text)))
        .Where(found => found.Offset is not null)
        .Select(found => new Error(PdfErrorCodes.Write.InvalidText, $"The XMP property {name} holds a character XML does not allow, at position {found.Offset}.")
            .With(ErrorMetadataKeys.Property, name)
            .With(ErrorMetadataKeys.Offset, found.Offset!.Value));

    private static IEnumerable<string> Texts(XmpValue value) => value switch
    {
        XmpText text => [text.Text],
        XmpLanguageAlternative alternative => [alternative.Text],
        XmpOrderedArray array => array.Items.SelectMany(Texts),
        XmpUnorderedArray array => array.Items.SelectMany(Texts),
        XmpStructure structure => structure.Fields.SelectMany(field => Texts(field.Value)),
        _ => [],
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
    /// A property element: a simple value as element text (ISO 16684-1 §7.5); an array as "exactly one nested element"
    /// <c>rdf:Seq</c>, <c>rdf:Bag</c> or <c>rdf:Alt</c> of <c>rdf:li</c> items (§7.7); a structure as the fields inside
    /// an element with <c>rdf:parseType="Resource"</c> (§7.9.2, allowed equivalent RDF).
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
                // Unreachable: Write refuses a value of any other kind before it serializes (NameProblems).
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
