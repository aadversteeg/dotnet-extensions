using System.Collections.Generic;

namespace Ave.Extensions.Documents.Html.Parsing.Tokenizing;

/// <summary>The kinds of token the tokenizer emits (HTML Standard §13.2.5): DOCTYPE, start tag, end tag, comment,
/// processing instruction, character and end-of-file.</summary>
internal enum TokenKind
{
    /// <summary>A DOCTYPE.</summary>
    Doctype,

    /// <summary>A start tag.</summary>
    StartTag,

    /// <summary>An end tag.</summary>
    EndTag,

    /// <summary>A comment.</summary>
    Comment,

    /// <summary>A processing instruction (§13.2.5.72–§13.2.5.76).</summary>
    ProcessingInstruction,

    /// <summary>A run of character tokens (each character of <see cref="Token.Data"/> is one character token).</summary>
    Characters,

    /// <summary>The end of the input.</summary>
    EndOfFile,
}

/// <summary>
/// A token. One mutable class for all kinds, filled in by the tokenizer while it reads and handed to tree construction
/// once complete; tree construction only reads it, except for the attribute adjustments of foreign content and the
/// acknowledgement of the self-closing flag.
/// </summary>
internal sealed class Token
{
    /// <summary>The kind.</summary>
    public TokenKind Kind { get; init; }

    /// <summary>The index in the source text where the token starts (its <c>&lt;</c>, or its first character).</summary>
    public int Start { get; init; }

    /// <summary>For tags and DOCTYPE: the name (null for a DOCTYPE without one). For a processing instruction: its target.</summary>
    public string? Name { get; set; }

    /// <summary>For tags: the attributes, duplicates removed. Add with <see cref="AddAttribute"/>, so the index by name
    /// stays right.</summary>
    public List<TokenAttribute> Attributes { get; } = [];

    /// <summary>The attributes by name, once there are more than <see cref="IndexFrom"/>: a lookup is then constant work,
    /// so a tag with n attributes costs n, not n² (the duplicate check of the tokenizer, the "Noah's Ark" comparison, the
    /// attribute tests of tree construction).</summary>
    private Dictionary<string, int>? _index;

    /// <summary>The number of attributes from which they are indexed by name.</summary>
    private const int IndexFrom = 8;

    /// <summary>For tags: the self-closing flag.</summary>
    public bool SelfClosing { get; set; }

    /// <summary>For start tags: whether tree construction acknowledged the self-closing flag (§13.2.5, "When a start
    /// tag token is emitted with its self-closing flag set").</summary>
    public bool SelfClosingAcknowledged { get; set; }

    /// <summary>For DOCTYPE: the public identifier, null when missing.</summary>
    public string? PublicId { get; set; }

    /// <summary>For DOCTYPE: the system identifier, null when missing.</summary>
    public string? SystemId { get; set; }

    /// <summary>For DOCTYPE: the force-quirks flag.</summary>
    public bool ForceQuirks { get; set; }

    /// <summary>For comments and processing instructions: the data. For characters: the characters.</summary>
    public string Data { get; set; } = string.Empty;

    /// <summary>For characters: whether every character came from the source one for one, so character <c>k</c> was
    /// written at <see cref="Start"/> + k. False for the characters of a character reference, which were all written
    /// at its ampersand.</summary>
    public bool Contiguous { get; init; } = true;

    /// <summary>The index in the source of the character at <paramref name="offset"/> in <see cref="Data"/>.</summary>
    /// <param name="offset">An offset in <see cref="Data"/>.</param>
    /// <returns>Its index in the source text.</returns>
    public int SourceIndexOf(int offset) => Contiguous ? Start + offset : Start;

    /// <summary>The value of the attribute with this name, or null.</summary>
    /// <param name="name">The attribute name.</param>
    /// <returns>The value, or null.</returns>
    public string? Attribute(string name)
    {
        if (Attributes.Count > IndexFrom)
        {
            return Index().TryGetValue(name, out var at) ? Attributes[at].Value : null;
        }

        foreach (var attribute in Attributes)
        {
            if (attribute.Name == name)
            {
                return attribute.Value;
            }
        }

        return null;
    }

    /// <summary>Whether the token has an attribute with this name.</summary>
    /// <param name="name">The attribute name.</param>
    /// <returns>Whether it has one.</returns>
    public bool HasAttribute(string name) => Attribute(name) is not null;

    /// <summary>Adds an attribute (the tokenizer has checked it is not a duplicate).</summary>
    /// <param name="attribute">The attribute.</param>
    public void AddAttribute(TokenAttribute attribute)
    {
        Attributes.Add(attribute);
        _index?.TryAdd(attribute.Name, Attributes.Count - 1);
    }

    /// <summary>Forgets the index after attribute names were changed in place (the SVG and MathML adjustments of
    /// foreign content, §13.2.6.1).</summary>
    public void AttributesRenamed() => _index = null;

    private Dictionary<string, int> Index()
    {
        if (_index is null)
        {
            _index = new Dictionary<string, int>(Attributes.Count, System.StringComparer.Ordinal);
            for (var i = 0; i < Attributes.Count; i++)
            {
                _index.TryAdd(Attributes[i].Name, i);
            }
        }

        return _index;
    }
}

/// <summary>An attribute of a tag token.</summary>
internal sealed class TokenAttribute
{
    /// <summary>The name, lowercase as the tokenizer made it.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The value.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Where the name starts in the source.</summary>
    public int Start { get; init; }

    /// <summary>Where the value's characters were written: from each anchor's offset in <see cref="Value"/> on, at its
    /// source index and one by one after it (contiguous), or all at that index (the characters of a character
    /// reference, at its ampersand). An anchor starts the value, follows each reference and each line feed. Empty for
    /// an empty value.</summary>
    public (int Offset, int Source, bool Contiguous)[] Anchors { get; init; } = [];
}
