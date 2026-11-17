using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Ave.Extensions.Documents.Typesetting.Unicode;
using static Ave.Extensions.Documents.Typesetting.Unicode.LineBreakClass;

namespace Ave.Extensions.Documents.Typesetting.LineBreaking;

/// <summary>
/// The Unicode Line Breaking Algorithm, UAX #14 revision 57 (Unicode 18.0.0), with the default rules and no
/// tailoring: finds every place in a text where a line may or must end. Checked against the official
/// LineBreakTest.txt of the same version.
/// </summary>
/// <remarks>
/// <para>How it works, following UAX #14 §6: each code point gets its Line_Break class (rule LB1 resolves the
/// classes that depend on context). Combining marks and zero-width joiners join the character before them
/// (LB9, LB10), so the rules from LB11 on see "units": a base character with its marks. Then every position
/// between two code points is decided by the first rule of LB4–LB31 that applies ("The rules are applied in
/// order", §6) — <see cref="Rules"/> lists them in that order, one small function per rule.</para>
/// <para>The function works on a whole paragraph; the line breaker then chooses among the opportunities
/// (<see cref="GreedyLineBreaker"/>), so a different chooser can be added without touching this algorithm.</para>
/// </remarks>
public static class UnicodeLineBreaking
{
    /// <summary>
    /// Finds the break opportunities of a text. The start of the text is never one (LB2); the end always is, as
    /// a mandatory break (LB3), unless the text is empty.
    /// </summary>
    /// <param name="text">The text, for example a paragraph after white-space collapsing. An unpaired surrogate
    /// counts as U+FFFD.</param>
    /// <returns>The opportunities in text order.</returns>
    public static ImmutableArray<BreakOpportunity> Find(string text)
    {
        var characters = Resolve(text);
        if (characters.IsEmpty)
        {
            return [];
        }

        var (units, unitOf, absorbed) = Units(characters);
        var opportunities = ImmutableArray.CreateBuilder<BreakOpportunity>();
        for (var index = 1; index < characters.Length; index++)
        {
            var position = new Position(characters, index, absorbed[index], units, unitOf[index]);
            var decision = Rules.Select(rule => rule.Decide(position)).First(found => found is not null);
            if (decision is Decision.Break or Decision.Mandatory)
            {
                opportunities.Add(new BreakOpportunity(characters[index].Offset, decision == Decision.Mandatory));
            }
        }

        opportunities.Add(new BreakOpportunity(text.Length, IsMandatory: true));
        return opportunities.ToImmutable();
    }

    /// <summary>What a rule decides about one position: ! (mandatory), × (no break) or ÷ (break allowed).</summary>
    internal enum Decision
    {
        /// <summary>× — no break allowed here.</summary>
        NoBreak,

        /// <summary>÷ — a break is allowed here.</summary>
        Break,

        /// <summary>! — a break must happen here.</summary>
        Mandatory,
    }

    /// <summary>A code point with its resolved class and traits, and where it starts in the text.</summary>
    internal readonly record struct Character(int Offset, int CodePoint, LineBreakClass Class, CharacterTraits Traits)
    {
        public bool Is(params ReadOnlySpan<LineBreakClass> classes) => classes.Contains(Class);

        public bool Has(CharacterTraits trait) => (Traits & trait) == trait;
    }

    /// <summary>A rule of §6 with its name, for example "LB13"; null from <see cref="Decide"/> means "does not apply".</summary>
    internal sealed record Rule(string Name, Func<Position, Decision?> Decide);

    /// <summary>
    /// LB1: "Assign a line breaking class to each code point", resolving the classes whose behaviour depends
    /// on criteria outside the algorithm with the defaults of the table in LB1: AI, SG and XX become AL; SA
    /// becomes CM for marks (General_Category Mn or Mc) and AL otherwise; CJ becomes NS.
    /// </summary>
    private static ImmutableArray<Character> Resolve(string text)
    {
        var characters = ImmutableArray.CreateBuilder<Character>();
        var offset = 0;
        while (offset < text.Length)
        {
            // An invalid sequence decodes to U+FFFD with one code unit consumed, which keeps the walk going.
            Rune.DecodeFromUtf16(text.AsSpan(offset), out var rune, out var consumed);
            var (original, traits, _) = UnicodeProperties.Of(rune.Value);
            var resolved = original switch
            {
                AI or SG or XX => AL,
                SA => (traits & CharacterTraits.Mark) != 0 ? CM : AL,
                CJ => NS,
                _ => original,
            };
            characters.Add(new Character(offset, rune.Value, resolved, traits));
            offset += consumed;
        }

        return characters.ToImmutable();
    }

    /// <summary>
    /// LB9 and LB10. LB9: "Treat X (CM | ZWJ)* as if it were X." "where X is any line break class except BK, CR,
    /// LF, NL, SP, or ZW." — a mark joins the unit of the character before it. LB10: "Treat any remaining CM or
    /// ZWJ as if it had the properties of U+0041 A", that is class AL and none of the traits.
    /// </summary>
    /// <returns>The units; for every code point the index of its unit; and whether it was absorbed into the unit
    /// before it.</returns>
    private static (ImmutableArray<Character> Units, int[] UnitOf, bool[] Absorbed) Units(ImmutableArray<Character> characters)
    {
        var units = ImmutableArray.CreateBuilder<Character>();
        var unitOf = new int[characters.Length];
        var absorbed = new bool[characters.Length];
        for (var index = 0; index < characters.Length; index++)
        {
            var character = characters[index];
            absorbed[index] = character.Is(CM, ZWJ)
                && index > 0
                && (absorbed[index - 1] || !characters[index - 1].Is(BK, CR, LF, NL, SP, ZW));
            if (!absorbed[index])
            {
                units.Add(character.Is(CM, ZWJ) ? character with { Class = AL, Traits = CharacterTraits.None } : character);
            }

            unitOf[index] = units.Count - 1;
        }

        return (units.ToImmutable(), unitOf, absorbed);
    }

    /// <summary>
    /// One position between two code points: <see cref="Index"/> is the code point after it. Rules LB4–LB8a look
    /// at the code points themselves; rules from LB11 on look at units, where <see cref="Before"/> is the unit
    /// that ends here and <see cref="After"/> the unit that starts here.
    /// </summary>
    internal readonly record struct Position(
        ImmutableArray<Character> Characters, int Index, bool IsAbsorbed, ImmutableArray<Character> Units, int UnitIndex)
    {
        public Character CharacterBefore => Characters[Index - 1];

        public Character CharacterAfter => Characters[Index];

        public Character Before => Units[UnitIndex - 1];

        public Character After => Units[UnitIndex];

        /// <summary>The unit <paramref name="steps"/> before <see cref="Before"/>, or null at the start of text (sot).</summary>
        public Character? Earlier(int steps) => UnitIndex - 1 - steps >= 0 ? Units[UnitIndex - 1 - steps] : null;

        /// <summary>The unit <paramref name="steps"/> after <see cref="After"/>, or null at the end of text (eot).</summary>
        public Character? Later(int steps) => UnitIndex + steps < Units.Length ? Units[UnitIndex + steps] : null;

        /// <summary>For "X SP* ×": the unit before the spaces that end here, or null when only spaces precede.</summary>
        public Character? BeforeSpaces()
        {
            var index = UnitIndex - 1;
            while (index >= 0 && Units[index].Class == SP)
            {
                index--;
            }

            return index >= 0 ? Units[index] : null;
        }

        /// <summary>For "NU ( SY | IS )* ×": true when the unit at <paramref name="index"/>, after skipping SY and IS
        /// units backwards, is NU.</summary>
        public bool NumberEndsAt(int index)
        {
            while (index >= 0 && Units[index].Is(SY, IS))
            {
                index--;
            }

            return index >= 0 && Units[index].Class == NU;
        }
    }

    /// <summary>The rules LB4–LB31 of §6.1 and §6.2, in the order in which they are applied.</summary>
    internal static readonly ImmutableArray<Rule> Rules =
    [
        // Mandatory breaks. LB4: "BK !". LB5: "CR × LF", "CR !", "LF !", "NL !". LB6: "× ( BK | CR | LF | NL )".
        new("LB4", p => p.CharacterBefore.Class == BK ? Decision.Mandatory : null),
        new("LB5", p => p.CharacterBefore.Class == CR && p.CharacterAfter.Class == LF ? Decision.NoBreak
            : p.CharacterBefore.Is(CR, LF, NL) ? Decision.Mandatory : null),
        new("LB6", p => p.CharacterAfter.Is(BK, CR, LF, NL) ? Decision.NoBreak : null),

        // Explicit breaks and non-breaks. LB7: "× SP", "× ZW". LB8: "ZW SP* ÷". LB8a: "ZWJ ×".
        new("LB7", p => p.CharacterAfter.Is(SP, ZW) ? Decision.NoBreak : null),
        new("LB8", p => ZeroWidthSpaceBefore(p) ? Decision.Break : null),
        new("LB8a", p => p.CharacterBefore.Class == ZWJ ? Decision.NoBreak : null),

        // LB9: no break inside "X (CM | ZWJ)*"; from here on the rules see units (see Units()).
        new("LB9", p => p.IsAbsorbed ? Decision.NoBreak : null),

        // LB11: "× WJ", "WJ ×". LB12: "GL ×". LB12a: "[^SP HY HH] × GL".
        new("LB11", p => p.After.Class == WJ || p.Before.Class == WJ ? Decision.NoBreak : null),
        new("LB12", p => p.Before.Class == GL ? Decision.NoBreak : null),
        new("LB12a", p => p.After.Class == GL && !p.Before.Is(SP, HY, HH) ? Decision.NoBreak : null),

        // Opening and closing. LB13: "× CL", "× CP", "× EX", "× SY". LB14: "OP SP* ×".
        new("LB13", p => p.After.Is(CL, CP, EX, SY) ? Decision.NoBreak : null),
        new("LB14", p => p.BeforeSpaces()?.Class == OP ? Decision.NoBreak : null),

        // LB15a: "(sot | BK | CR | LF | NL | OP | QU | GL | SP | ZW) [\p{Pi}&QU] SP* ×".
        new("LB15a", p => InitialQuoteBefore(p) ? Decision.NoBreak : null),

        // LB15b: "× [\p{Pf}&QU] ( SP | GL | WJ | CL | QU | CP | EX | IS | SY | BK | CR | LF | NL | ZW | eot)".
        new("LB15b", p => p.After.Class == QU && p.After.Has(CharacterTraits.FinalPunctuation)
            && (p.Later(1) is not { } next || next.Is(SP, GL, WJ, CL, QU, CP, EX, IS, SY, BK, CR, LF, NL, ZW)) ? Decision.NoBreak : null),

        // LB15c: "SP ÷ IS NU". LB15d: "× IS".
        new("LB15c", p => p.Before.Class == SP && p.After.Class == IS && p.Later(1)?.Class == NU ? Decision.Break : null),
        new("LB15d", p => p.After.Class == IS ? Decision.NoBreak : null),

        // LB16: "(CL | CP) SP* × NS". LB17: "B2 SP* × B2".
        new("LB16", p => p.After.Class == NS && p.BeforeSpaces() is { } before && before.Is(CL, CP) ? Decision.NoBreak : null),
        new("LB17", p => p.After.Class == B2 && p.BeforeSpaces()?.Class == B2 ? Decision.NoBreak : null),

        // Spaces. LB18: "SP ÷".
        new("LB18", p => p.Before.Class == SP ? Decision.Break : null),

        // LB19: "× [ QU - \p{Pi} ]", "[ QU - \p{Pf} ] ×".
        new("LB19", p => (p.After.Class == QU && !p.After.Has(CharacterTraits.InitialPunctuation))
            || (p.Before.Class == QU && !p.Before.Has(CharacterTraits.FinalPunctuation)) ? Decision.NoBreak : null),

        // LB19a: "[^$EastAsian] × QU", "× QU ( [^$EastAsian] | eot )", "QU × [^$EastAsian]",
        // "( sot | [^$EastAsian] ) QU ×".
        new("LB19a", p => QuotationNotAmongEastAsian(p) ? Decision.NoBreak : null),

        // LB20: "÷ CB", "CB ÷".
        new("LB20", p => p.After.Class == CB || p.Before.Class == CB ? Decision.Break : null),

        // LB20a: "( sot | BK | CR | LF | NL | SP | ZW | CB | GL ) ( HY | HH ) × ( AL | HL )".
        new("LB20a", p => p.Before.Is(HY, HH) && p.After.Is(AL, HL)
            && (p.Earlier(1) is not { } earlier || earlier.Is(BK, CR, LF, NL, SP, ZW, CB, GL)) ? Decision.NoBreak : null),

        // LB21: "× BA", "× HH", "× HY", "× NS", "BB ×". LB21a: "HL (HY | HH) × [^HL]". LB21b: "SY × HL".
        new("LB21", p => p.After.Is(BA, HH, HY, NS) || p.Before.Class == BB ? Decision.NoBreak : null),
        new("LB21a", p => p.Before.Is(HY, HH) && p.Earlier(1)?.Class == HL && p.After.Class != HL ? Decision.NoBreak : null),
        new("LB21b", p => p.Before.Class == SY && p.After.Class == HL ? Decision.NoBreak : null),

        // LB22: "× IN".
        new("LB22", p => p.After.Class == IN ? Decision.NoBreak : null),

        // Numbers. LB23: "(AL | HL) × NU", "NU × (AL | HL)". LB23a: "PR × (ID | EB | EM)", "(ID | EB | EM) × PO".
        // LB24: "(PR | PO) × (AL | HL)", "(AL | HL) × (PR | PO)".
        new("LB23", p => (p.Before.Is(AL, HL) && p.After.Class == NU) || (p.Before.Class == NU && p.After.Is(AL, HL)) ? Decision.NoBreak : null),
        new("LB23a", p => (p.Before.Class == PR && p.After.Is(ID, EB, EM)) || (p.Before.Is(ID, EB, EM) && p.After.Class == PO) ? Decision.NoBreak : null),
        new("LB24", p => (p.Before.Is(PR, PO) && p.After.Is(AL, HL)) || (p.Before.Is(AL, HL) && p.After.Is(PR, PO)) ? Decision.NoBreak : null),

        // LB25, "Do not break numbers" (see NumberContinues).
        new("LB25", p => NumberContinues(p) ? Decision.NoBreak : null),

        // Korean syllable blocks. LB26: "JL × (JL | JV | H2 | H3)", "(JV | H2) × (JV | JT)", "(JT | H3) × JT".
        // LB27: "(JL | JV | JT | H2 | H3) × PO", "PR × (JL | JV | JT | H2 | H3)".
        new("LB26", p => (p.Before.Class == JL && p.After.Is(JL, JV, H2, H3))
            || (p.Before.Is(JV, H2) && p.After.Is(JV, JT))
            || (p.Before.Is(JT, H3) && p.After.Class == JT) ? Decision.NoBreak : null),
        new("LB27", p => (p.Before.Is(JL, JV, JT, H2, H3) && p.After.Class == PO)
            || (p.Before.Class == PR && p.After.Is(JL, JV, JT, H2, H3)) ? Decision.NoBreak : null),

        // LB28: "(AL | HL) × (AL | HL)". LB28a: Brahmic orthographic syllables (see InsideAksaraSyllable).
        new("LB28", p => p.Before.Is(AL, HL) && p.After.Is(AL, HL) ? Decision.NoBreak : null),
        new("LB28a", p => InsideAksaraSyllable(p) ? Decision.NoBreak : null),

        // LB29: "IS × (AL | HL)". LB30: "(AL | HL | NU) × [OP-$EastAsian]", "[CP-$EastAsian] × (AL | HL | NU)".
        new("LB29", p => p.Before.Class == IS && p.After.Is(AL, HL) ? Decision.NoBreak : null),
        new("LB30", p => (p.Before.Is(AL, HL, NU) && p.After.Class == OP && !p.After.Has(CharacterTraits.EastAsian))
            || (p.Before.Class == CP && !p.Before.Has(CharacterTraits.EastAsian) && p.After.Is(AL, HL, NU)) ? Decision.NoBreak : null),

        // LB30a: "sot (RI RI)* RI × RI", "[^RI] (RI RI)* RI × RI": pair regional indicators.
        new("LB30a", p => p.Before.Class == RI && p.After.Class == RI && RegionalIndicatorsBefore(p) % 2 == 1 ? Decision.NoBreak : null),

        // LB30b: "EB × EM", "[\p{Extended_Pictographic}&\p{Cn}] × EM".
        new("LB30b", p => p.After.Class == EM
            && (p.Before.Class == EB || p.Before.Has(CharacterTraits.ExtendedPictographic | CharacterTraits.Unassigned)) ? Decision.NoBreak : null),

        // LB31: "ALL ÷", "÷ ALL".
        new("LB31", _ => Decision.Break),
    ];

    /// <summary>LB8 "ZW SP* ÷": a zero-width space, possibly followed by spaces, ends right here.</summary>
    private static bool ZeroWidthSpaceBefore(Position position)
    {
        var index = position.Index - 1;
        while (index >= 0 && position.Characters[index].Class == SP)
        {
            index--;
        }

        return index >= 0 && position.Characters[index].Class == ZW;
    }

    /// <summary>LB15a: an initial quotation mark (QU with General_Category Pi), possibly followed by spaces, ends
    /// here, and before it is the start of the text or one of BK, CR, LF, NL, OP, QU, GL, SP, ZW.</summary>
    private static bool InitialQuoteBefore(Position position)
    {
        var index = position.UnitIndex - 1;
        while (index >= 0 && position.Units[index].Class == SP)
        {
            index--;
        }

        return index >= 0
            && position.Units[index] is { Class: QU } quote && quote.Has(CharacterTraits.InitialPunctuation)
            && (index == 0 || position.Units[index - 1].Is(BK, CR, LF, NL, OP, QU, GL, SP, ZW));
    }

    /// <summary>LB19a: a quotation mark next to a character that is not East Asian (or next to sot/eot).</summary>
    private static bool QuotationNotAmongEastAsian(Position position)
    {
        static bool IsEastAsian(Character? unit) => unit is { } found && found.Has(CharacterTraits.EastAsian);

        return (position.After.Class == QU && (!IsEastAsian(position.Before) || !IsEastAsian(position.Later(1))))
            || (position.Before.Class == QU && (!IsEastAsian(position.After) || !IsEastAsian(position.Earlier(1))));
    }

    /// <summary>
    /// LB25, "Do not break numbers", all its expressions: "NU ( SY | IS )* CL × PO", "… CP × PO", "… CL × PR",
    /// "… CP × PR", "NU ( SY | IS )* × PO", "… × PR", "PO × OP NU", "PO × OP IS NU", "PO × NU", the same three
    /// for PR, "HY × NU", "IS × NU", and "NU ( SY | IS )* × NU".
    /// </summary>
    private static bool NumberContinues(Position position)
    {
        var (before, after) = (position.Before, position.After);
        var last = position.UnitIndex - 1;
        return (after.Is(PO, PR) && before.Is(CL, CP) && position.NumberEndsAt(last - 1))
            || (after.Is(PO, PR) && position.NumberEndsAt(last))
            || (before.Is(PO, PR) && (after.Class == NU
                || (after.Class == OP && position.Later(1)?.Class == NU)
                || (after.Class == OP && position.Later(1)?.Class == IS && position.Later(2)?.Class == NU)))
            || (before.Is(HY, IS) && after.Class == NU)
            || (after.Class == NU && position.NumberEndsAt(last));
    }

    /// <summary>
    /// LB28a, "Do not break inside the orthographic syllables of Brahmic scripts": "AP × (AK | [◌] | AS)",
    /// "(AK | [◌] | AS) × (VF | VI)", "(AK | [◌] | AS) VI × (AK | [◌])", "(AK | [◌] | AS) × (AK | [◌] | AS) VF",
    /// where [◌] is U+25CC DOTTED CIRCLE.
    /// </summary>
    private static bool InsideAksaraSyllable(Position position)
    {
        static bool IsBaseOrCircle(Character? unit) => unit is { } found && (found.Class == AK || found.CodePoint == 0x25CC);
        static bool IsAksara(Character? unit) => IsBaseOrCircle(unit) || unit?.Class == AS;

        var (before, after) = (position.Before, position.After);
        return (before.Class == AP && IsAksara(after))
            || (IsAksara(before) && after.Is(VF, VI))
            || (before.Class == VI && IsAksara(position.Earlier(1)) && IsBaseOrCircle(after))
            || (IsAksara(before) && IsAksara(after) && position.Later(1)?.Class == VF);
    }

    /// <summary>LB30a: how many regional indicators end here, counted back to the first unit that is not one.</summary>
    private static int RegionalIndicatorsBefore(Position position)
    {
        var count = 0;
        for (var index = position.UnitIndex - 1; index >= 0 && position.Units[index].Class == RI; index--)
        {
            count++;
        }

        return count;
    }
}
