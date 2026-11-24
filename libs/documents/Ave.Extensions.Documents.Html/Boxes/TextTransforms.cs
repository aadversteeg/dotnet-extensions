using System;
using System.Globalization;
using System.Text;

namespace Ave.Extensions.Documents.Html.Boxes;

/// <summary>What <see cref="TextTransforms.Apply"/> gives: the transformed text, or the first character it does not map
/// and why.</summary>
/// <param name="Text">The text, transformed; the text as written when <paramref name="Reason"/> is set.</param>
/// <param name="Offset">The offset of the character that is not mapped, in the text as written; -1 when none.</param>
/// <param name="Reason">Why that character is not mapped; null when the whole text is.</param>
internal readonly record struct TransformedText(string Text, int Offset, string? Reason);

/// <summary>
/// <c>text-transform</c> (CSS Text 3 §2.1) on the text of one text node, as Chromium 159 prints it. The mapping is
/// Unicode's simple case mapping of each character on its own (UnicodeData.txt, through .NET's invariant casing, which
/// gives the same with and without culture data, so the output does not depend on the machine), with the rules of
/// Unicode's SpecialCasing.txt for i, ı and İ in Turkish and Azerbaijani written out (<see cref="UpperOf"/>,
/// <see cref="LowerOf"/>). It keeps the length of the text; a character whose case mapping in Chromium is longer
/// (SpecialCasing.txt: ß to SS, the ligature ﬁ to FI, İ to i and a combining dot), or follows rules of a language that
/// are not written out here (Greek under el, Lithuanian, Dutch ij), is refused, so the text is never silently
/// different. Measured: cases <c>tt-values</c>, <c>tt-special</c>, <c>tt-capitalize</c>, <c>tt-inline</c> of
/// <c>tools/reference/chromium</c>; what Chromium printed for each refused letter is in the cases or in the comments
/// of the refusals.
/// </summary>
internal static class TextTransforms
{
    /// <summary>Applies a transform to a text.</summary>
    /// <param name="text">The text of one text node, as written.</param>
    /// <param name="transform">The computed value: <c>none</c>, <c>uppercase</c>, <c>lowercase</c> or <c>capitalize</c>.</param>
    /// <param name="language">The language of the text (HTML Standard §3.2.6.2), or null.</param>
    /// <param name="previous">The last character of the text before it in the same inline content, a space when there
    /// is none (capitalize looks at it to tell whether the text starts inside a word).</param>
    /// <returns>The transformed text, or the first character that is not mapped.</returns>
    public static TransformedText Apply(string text, string transform, string? language, char previous) => transform switch
    {
        "uppercase" => Upper(text, IsTurkic(Primary(language)), Primary(language)),
        "lowercase" => Lower(text, IsTurkic(Primary(language)), Primary(language)),
        "capitalize" => Capitalize(text, IsTurkic(Primary(language)), Primary(language), previous),
        _ => new(text, -1, null),
    };

    /// <summary>The capital of a character: Unicode's simple uppercase mapping (<see cref="char.ToUpperInvariant"/>,
    /// the same with and without culture data), but i to İ in Turkish and Azerbaijani and ı to I in every language
    /// (SpecialCasing.txt; .NET's invariant casing leaves ı as it is).</summary>
    private static char UpperOf(char c, bool turkic) => c switch
    {
        'i' when turkic => '\u0130',
        '\u0131' => 'I',
        _ => char.ToUpperInvariant(c),
    };

    /// <summary>The small letter of a character: Unicode's simple lowercase mapping, but I to ı and İ to i in Turkish
    /// and Azerbaijani (SpecialCasing.txt).</summary>
    private static char LowerOf(char c, bool turkic) => c switch
    {
        'I' when turkic => '\u0131',
        '\u0130' when turkic => 'i',
        _ => char.ToLowerInvariant(c),
    };

    private static string Primary(string? language)
    {
        if (string.IsNullOrEmpty(language))
        {
            return string.Empty;
        }

        var dash = language.IndexOf('-', StringComparison.Ordinal);
        return (dash < 0 ? language : language[..dash]).ToLowerInvariant();
    }

    private static bool IsTurkic(string primary) => primary is "tr" or "az";

    private static TransformedText Refused(string text, int offset, string reason) => new(text, offset, reason);

    /// <summary>Uppercase: each character to its capital. Refused: a lowercase or titlecase letter without a capital of one
    /// character (ß, ŉ, ǰ, the ligatures ﬀ–ﬆ, Greek letters with an iota subscript), and text in Greek under the language
    /// el or in Lithuanian, whose capitals Chromium spells by rules of that language.</summary>
    private static TransformedText Upper(string text, bool turkic, string primary)
    {
        var result = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsSurrogate(c))
            {
                return Refused(text, i, "a character outside the Basic Multilingual Plane is not case-mapped in this version");
            }

            var category = char.GetUnicodeCategory(c);
            if ((category is UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter && UpperOf(c, turkic) == c) || c is >= '\u1F80' and <= '\u1FFF')
            {
                return Refused(text, i, $"'{c}' has no capital of one letter (Unicode SpecialCasing.txt): write the capitals in the text");
            }

            if (primary == "el" && IsGreek(c))
            {
                return Refused(text, i, "Greek capitals under the language el drop their accents in Chromium, which this version does not do: write the capitals in the text");
            }

            if (primary == "lt" && c is 'i' or 'j' or '\u012F' or '\u0307')
            {
                return Refused(text, i, "Lithuanian capitals follow rules of the language that this version does not apply: write the capitals in the text");
            }

            result.Append(UpperOf(c, turkic));
        }

        return new(result.ToString(), -1, null);
    }

    /// <summary>Lowercase: each character to its small letter, a capital sigma to final sigma ς where it ends a word
    /// (Unicode §3.13, Final_Sigma, within the text node: <c>ΟΔΟΣ.</c> prints <c>οδος.</c>, a lone <c>Σ</c> prints
    /// <c>σ</c>, case <c>tt-special</c>). Refused: İ outside Turkish and Azerbaijani (its small letter is two characters, i
    /// and a combining dot, as Chromium printed it), a combining dot above in those languages, and the letters Lithuanian
    /// lowercases with a dot.</summary>
    private static TransformedText Lower(string text, bool turkic, string primary)
    {
        var result = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsSurrogate(c))
            {
                return Refused(text, i, "a character outside the Basic Multilingual Plane is not case-mapped in this version");
            }

            if (c == '\u0130' && !turkic)
            {
                return Refused(text, i, "'İ' has no small letter of one character outside Turkish (Unicode SpecialCasing.txt): write the small letters in the text");
            }

            if (c == '\u0307' && turkic)
            {
                return Refused(text, i, "a combining dot above is dropped after I in Turkish, which this version does not do: write the small letters in the text");
            }

            if (primary == "lt" && c is 'I' or 'J' or '\u012E' or '\u00CC' or '\u00CD' or '\u0128')
            {
                return Refused(text, i, "Lithuanian small letters follow rules of the language that this version does not apply: write the small letters in the text");
            }

            if (c == '\u03A3')
            {
                if (FinalSigma(text, i) is not { } final)
                {
                    return Refused(text, i, "a capital sigma next to a modifier letter, a combining mark or a format character is not lowercased in this version: write the small letters in the text");
                }

                result.Append(final ? '\u03C2' : '\u03C3');
                continue;
            }

            result.Append(LowerOf(c, turkic));
        }

        return new(result.ToString(), -1, null);
    }

    /// <summary>
    /// Final_Sigma (The Unicode Standard §3.13, Table 3-17): the capital sigma at <paramref name="at"/> follows a cased
    /// letter and no cased letter follows it, case-ignorable characters skipped on both sides; only within the text, as
    /// Chromium maps each text node on its own. Cased is read as a letter of category Lu, Ll or Lt, case-ignorable as the
    /// apostrophes, full stop, colon and middle dots of Unicode's definition (Word_Break MidLetter, MidNumLet and
    /// Single_Quote); null where a neighbour is a character for which that reading of the two properties could be wrong
    /// (a modifier letter or symbol, a mark, a format character, a surrogate).
    /// </summary>
    private static bool? FinalSigma(string text, int at)
    {
        bool? Cased(int from, int step)
        {
            for (var i = from; i >= 0 && i < text.Length; i += step)
            {
                var c = text[i];
                switch (char.GetUnicodeCategory(c))
                {
                    case UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter:
                        return true;
                    case UnicodeCategory.ModifierLetter or UnicodeCategory.ModifierSymbol or UnicodeCategory.NonSpacingMark
                        or UnicodeCategory.EnclosingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.Format or UnicodeCategory.Surrogate:
                        return null;
                    default:
                        if (c is '\'' or '.' or ':' or '\u00B7' or '\u0387' or '\u2018' or '\u2019' or '\u2024' or '\u2027')
                        {
                            continue;
                        }

                        return false;
                }
            }

            return false;
        }

        var before = Cased(at - 1, -1);
        var after = Cased(at + 1, 1);
        return before is null || after is null ? null : before.Value && !after.Value;
    }

    private static bool IsGreek(char c) => c is >= '\u0370' and <= '\u03FF' or >= '\u1F00' and <= '\u1FFF';

    /// <summary>
    /// Capitalize: the first letter of each word to its titlecase form, the rest as written (<c>mIxed</c> prints
    /// <c>MIxed</c>). The titlecase form is the capital in the text's language (Turkish i to İ), but ǅ, ǈ, ǋ and ǲ for the
    /// four digraphs (UnicodeData.txt's titlecase mapping). Refused: a first letter whose titlecase form is more than one
    /// character in Chromium (ß prints Ss, ﬁ Fi, ŉ ʼN: the letters without a capital of their own), a word that starts with
    /// ij in Dutch (Chromium prints IJ), and a character whose effect on where a word starts is not measured
    /// (<see cref="ClassOf"/>).
    /// </summary>
    private static TransformedText Capitalize(string text, bool turkic, string primary, char previous)
    {
        if (text.Length > 0 && ClassOf(text[0]) == WordClass.Letter && ClassOf(previous) is null)
        {
            return Refused(text, 0, $"'text-transform: capitalize' does not tell whether a word starts here after '{previous}' in this version: write the capitals in the text");
        }

        var result = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (ClassOf(c) is not { } kind)
            {
                return Refused(text, i, $"'text-transform: capitalize' does not tell where a word starts at '{c}' in this version: write the capitals in the text");
            }

            if (kind != WordClass.Letter || !StartsWord(text, i, previous))
            {
                result.Append(c);
                continue;
            }

            var title = Title(c, turkic);
            if (title == c && char.IsLower(c))
            {
                return Refused(text, i, $"'{c}' has no capital of one letter (Unicode SpecialCasing.txt): write the capitals in the text");
            }

            if (primary == "nl" && c is 'i' or 'I' && i + 1 < text.Length && text[i + 1] is 'j' or 'J')
            {
                return Refused(text, i, "a Dutch word that starts with ij is capitalized as IJ in Chromium, which this version does not do: write the capitals in the text");
            }

            result.Append(title);
        }

        return new(result.ToString(), -1, null);
    }

    private static char Title(char c, bool turkic) => c switch
    {
        '\u01C4' or '\u01C5' or '\u01C6' => '\u01C5',
        '\u01C7' or '\u01C8' or '\u01C9' => '\u01C8',
        '\u01CA' or '\u01CB' or '\u01CC' => '\u01CB',
        '\u01F1' or '\u01F2' or '\u01F3' => '\u01F2',
        _ => UpperOf(c, turkic),
    };

    /// <summary>What a character is to the start of a word, as measured (case <c>tt-capitalize</c>).</summary>
    private enum WordClass
    {
        /// <summary>A letter of Latin, Greek or Cyrillic: titlecased where it starts a word.</summary>
        Letter,

        /// <summary>A letter, a decimal digit or connector punctuation before a letter: the letter goes on the word
        /// (<c>3rd</c>, <c>a1b</c>, <c>a_b</c> print <c>3rd</c>, <c>A1b</c>, <c>A_b</c>).</summary>
        Joining,

        /// <summary>The apostrophe U+0027 and the right single quotation mark U+2019: inside a word between two letters
        /// (<c>don't</c>, <c>rock’n’roll</c> print <c>Don't</c>, <c>Rock’n’roll</c>), else a break (<c>'quoted'</c> prints
        /// <c>'Quoted'</c>).</summary>
        Apostrophe,

        /// <summary>Any other character: white space, a no-break space, punctuation (the full stop, colon and comma
        /// too: <c>e.g.</c>, <c>a:b</c>, <c>a,b</c> print <c>E.G.</c>, <c>A:B</c>, <c>A,B</c>), symbols. A letter after it
        /// starts a word.</summary>
        Break,
    }

    /// <summary>
    /// The class of a character (<see cref="WordClass"/>); null for the characters whose effect is not measured: letters of
    /// other scripts, modifier letters and symbols, marks and format characters, the other apostrophes and middle dots
    /// (Unicode's word boundaries, UAX #29, give them their own classes MidLetter and MidNumLet), and characters outside
    /// the Basic Multilingual Plane.
    /// </summary>
    private static WordClass? ClassOf(char c)
    {
        if (c is '\'' or '\u2019')
        {
            return WordClass.Apostrophe;
        }

        if (c is '\u00B7' or '\u0387' or '\u2018' or '\u2024' or '\u2027' or '\uFE13' or '\uFE52' or '\uFE55' or '\uFF07' or '\uFF0E' or '\uFF1A')
        {
            return null;
        }

        return char.GetUnicodeCategory(c) switch
        {
            UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter or UnicodeCategory.OtherLetter
                => c is <= '\u052F' or >= '\u1E00' and <= '\u1FFF' or >= '\uFB00' and <= '\uFB06' ? WordClass.Letter : null,
            UnicodeCategory.DecimalDigitNumber or UnicodeCategory.ConnectorPunctuation => WordClass.Joining,
            UnicodeCategory.ModifierLetter or UnicodeCategory.ModifierSymbol or UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark
                or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.Format or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse
                or UnicodeCategory.OtherNotAssigned => null,
            _ => WordClass.Break,
        };
    }

    /// <summary>
    /// Whether the letter at <paramref name="at"/> starts a word: it does unless the character before it is a letter, a
    /// digit or connector punctuation, or an apostrophe that follows a letter. Before the text stands
    /// <paramref name="previous"/>, one character, as Chromium looks back: <c>wo&lt;span&gt;rd&lt;/span&gt;</c> prints
    /// <c>Wo</c><c>rd</c> (case <c>tt-inline</c>).
    /// </summary>
    private static bool StartsWord(string text, int at, char previous)
    {
        WordClass? At(int index) => index >= 0 ? ClassOf(text[index]) : index == -1 ? ClassOf(previous) : WordClass.Break;

        return At(at - 1) switch
        {
            WordClass.Letter or WordClass.Joining => false,
            WordClass.Apostrophe => At(at - 2) != WordClass.Letter,
            _ => true,
        };
    }
}
