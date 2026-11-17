namespace Ave.Documents.Typesetting.Unicode;

/// <summary>
/// The values of the Unicode Script property (UAX #24) that shaping distinguishes in this version, from
/// Scripts.txt: the three scripts the shaper supports, the two "shared" values, and everything else.
/// </summary>
internal enum TextScript : byte
{
    /// <summary>Any script other than those below (shaped with the font's default script).</summary>
    Other,

    /// <summary>Script=Common (Zyyy): used by several scripts, such as digits, punctuation and spaces.</summary>
    Common,

    /// <summary>Script=Inherited (Zinh): takes the script of the character before it, such as combining marks.</summary>
    Inherited,

    /// <summary>Script=Latin (Latn).</summary>
    Latin,

    /// <summary>Script=Greek (Grek).</summary>
    Greek,

    /// <summary>Script=Cyrillic (Cyrl).</summary>
    Cyrillic,
}
