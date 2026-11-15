using System;

namespace Ave.Documents.Typesetting.Unicode;

/// <summary>
/// The other Unicode properties the line breaking and shaping rules look at, besides Line_Break. Each comes from its own
/// Unicode data file (see <see cref="UnicodeProperties"/>).
/// </summary>
[Flags]
internal enum CharacterTraits : byte
{
    /// <summary>None of the traits below.</summary>
    None = 0,

    /// <summary>East_Asian_Width is F, W or H: the set <c>$EastAsian</c> of UAX #14 §6 (EastAsianWidth.txt).</summary>
    EastAsian = 1 << 0,

    /// <summary>Extended_Pictographic=Yes (emoji-data.txt), used by LB30b.</summary>
    ExtendedPictographic = 1 << 1,

    /// <summary>General_Category Mn or Mc, a combining mark (DerivedGeneralCategory.txt), used by LB1 for SA.</summary>
    Mark = 1 << 2,

    /// <summary>General_Category Pi, initial punctuation, used by LB15a and LB19.</summary>
    InitialPunctuation = 1 << 3,

    /// <summary>General_Category Pf, final punctuation, used by LB15b and LB19.</summary>
    FinalPunctuation = 1 << 4,

    /// <summary>General_Category Cn, unassigned, used by LB30b.</summary>
    Unassigned = 1 << 5,

    /// <summary>General_Category Mn, a nonspacing mark. Shaping treats its glyph as a mark when the font has no
    /// glyph classes of its own (no GDEF table).</summary>
    NonspacingMark = 1 << 6,

    /// <summary>Default_Ignorable_Code_Point=Yes (DerivedCoreProperties.txt): a character that is rendered invisible
    /// when it has no visible effect, such as the soft hyphen, the joiners and the zero width space (the Unicode
    /// Standard, §5.21).</summary>
    DefaultIgnorable = 1 << 7,
}
