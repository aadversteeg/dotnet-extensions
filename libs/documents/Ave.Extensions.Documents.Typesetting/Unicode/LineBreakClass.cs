namespace Ave.Documents.Typesetting.Unicode;

/// <summary>
/// The values of the Unicode Line_Break property (UAX #14 §5.1, "Description of Line Breaking Properties"),
/// with their short names as used in LineBreak.txt and in the rules. The comment on each value is the class's
/// name in UAX #14.
/// </summary>
internal enum LineBreakClass : byte
{
    /// <summary>Ambiguous (Alphabetic or Ideograph); resolved to AL by LB1.</summary>
    AI,
    /// <summary>Aksara.</summary>
    AK,
    /// <summary>Ordinary Alphabetic and Symbol Characters.</summary>
    AL,
    /// <summary>Aksara Pre-Base.</summary>
    AP,
    /// <summary>Aksara Start.</summary>
    AS,
    /// <summary>Break Opportunity Before and After (em dash).</summary>
    B2,
    /// <summary>Break After.</summary>
    BA,
    /// <summary>Break Before.</summary>
    BB,
    /// <summary>Mandatory Break.</summary>
    BK,
    /// <summary>Contingent Break Opportunity.</summary>
    CB,
    /// <summary>Conditional Japanese Starter; resolved to NS by LB1.</summary>
    CJ,
    /// <summary>Close Punctuation.</summary>
    CL,
    /// <summary>Combining Mark.</summary>
    CM,
    /// <summary>Close Parenthesis.</summary>
    CP,
    /// <summary>Carriage Return.</summary>
    CR,
    /// <summary>Emoji Base.</summary>
    EB,
    /// <summary>Emoji Modifier.</summary>
    EM,
    /// <summary>Exclamation/Interrogation.</summary>
    EX,
    /// <summary>Non-breaking ("Glue").</summary>
    GL,
    /// <summary>Hangul LV Syllable.</summary>
    H2,
    /// <summary>Hangul LVT Syllable.</summary>
    H3,
    /// <summary>Unambiguous Hyphen.</summary>
    HH,
    /// <summary>Hebrew Letter.</summary>
    HL,
    /// <summary>Hyphen (hyphen-minus).</summary>
    HY,
    /// <summary>Ideographic.</summary>
    ID,
    /// <summary>Inseparable Characters.</summary>
    IN,
    /// <summary>Infix Numeric Separator.</summary>
    IS,
    /// <summary>Hangul L Jamo.</summary>
    JL,
    /// <summary>Hangul T Jamo.</summary>
    JT,
    /// <summary>Hangul V Jamo.</summary>
    JV,
    /// <summary>Line Feed.</summary>
    LF,
    /// <summary>Next Line.</summary>
    NL,
    /// <summary>Nonstarter.</summary>
    NS,
    /// <summary>Numeric.</summary>
    NU,
    /// <summary>Open Punctuation.</summary>
    OP,
    /// <summary>Postfix Numeric.</summary>
    PO,
    /// <summary>Prefix Numeric.</summary>
    PR,
    /// <summary>Quotation.</summary>
    QU,
    /// <summary>Regional Indicator.</summary>
    RI,
    /// <summary>Complex Context Dependent (South East Asian); resolved by LB1.</summary>
    SA,
    /// <summary>Surrogate; resolved to AL by LB1.</summary>
    SG,
    /// <summary>Space.</summary>
    SP,
    /// <summary>Symbols Allowing Break After (solidus).</summary>
    SY,
    /// <summary>Virama Final.</summary>
    VF,
    /// <summary>Virama.</summary>
    VI,
    /// <summary>Word Joiner.</summary>
    WJ,
    /// <summary>Unknown; resolved to AL by LB1.</summary>
    XX,
    /// <summary>Zero Width Space.</summary>
    ZW,
    /// <summary>Zero Width Joiner.</summary>
    ZWJ,
}
