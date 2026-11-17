using Ave.Documents.Core;

namespace Ave.Documents.Pdf.Xmp;

/// <summary>
/// The value of an XMP property (ISO 16684-1 §6.3, value forms: simple, structure, array): a simple text value, an array, a language alternative,
/// or a structure of fields. A closed set of immutable values; <see cref="XmpPacket"/> writes them as RDF/XML.
/// </summary>
public abstract record XmpValue
{
    // Only the cases in this file exist, so the writer handles every one.
    private protected XmpValue()
    {
    }
}

/// <summary>A simple value: text, or a number or date already formatted as XMP text.</summary>
/// <param name="Text">The text.</param>
public sealed record XmpText(string Text) : XmpValue;

/// <summary>
/// A language alternative with one entry, for the default language (<c>rdf:Alt</c> with one <c>rdf:li
/// xml:lang="x-default"</c>; ISO 16684-1 §8.2.2.4: "If used, the x-default item shall be first in the
/// array"), the form PDF/A and PDF/UA expect for <c>dc:title</c>.
/// </summary>
/// <param name="Text">The text.</param>
public sealed record XmpLanguageAlternative(string Text) : XmpValue;

/// <summary>An ordered array (<c>rdf:Seq</c>), for example the authors in <c>dc:creator</c>.</summary>
/// <param name="Items">The items, in order.</param>
public sealed record XmpOrderedArray(ValueList<XmpValue> Items) : XmpValue;

/// <summary>An unordered array (<c>rdf:Bag</c>), for example the languages in <c>dc:language</c>.</summary>
/// <param name="Items">The items.</param>
public sealed record XmpUnorderedArray(ValueList<XmpValue> Items) : XmpValue;

/// <summary>A structure: named fields, each in its own namespace (written with <c>rdf:parseType="Resource"</c>).</summary>
/// <param name="Fields">The fields, in output order.</param>
public sealed record XmpStructure(ValueList<XmpProperty> Fields) : XmpValue;

/// <summary>One XMP property: a name in a namespace, and its value.</summary>
/// <param name="Namespace">The property's namespace.</param>
/// <param name="Name">The local name, for example <c>title</c>.</param>
/// <param name="Value">The value.</param>
public sealed record XmpProperty(XmpNamespace Namespace, string Name, XmpValue Value);
