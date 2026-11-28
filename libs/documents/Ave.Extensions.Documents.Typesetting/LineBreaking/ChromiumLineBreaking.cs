using System;
using System.Collections.Immutable;
using System.Text;
using Ave.Extensions.Documents.Typesetting.Unicode;
using static Ave.Extensions.Documents.Typesetting.Unicode.LineBreakClass;

namespace Ave.Extensions.Documents.Typesetting.LineBreaking;

/// <summary>
/// The places where Chromium 159's print may end a line: the opportunities of <see cref="UnicodeLineBreaking"/> (UAX #14
/// revision 57, unchanged), then adjusted between two code points by the rules below, each measured against Chromium's
/// print. Where no rule applies, the standard's answer stands. A mandatory break is never changed.
/// </summary>
/// <remarks>
/// <para>The evidence is the measured matrix of the unit tests (<c>Reference/Chromium/line-breaks.txt</c> of
/// <c>UnitTests.Extensions.Documents.Typesetting</c>, measured by <c>tools/reference/chromium/line-breaks.py</c>): one
/// paragraph per row in a box 1 pt wide, so that every place where Chromium may break is a line end of its print. The
/// rows are every pair of printable ASCII characters between letters (<c>ab</c> X Y <c>cd</c>) and between digits
/// (<c>12</c> X Y <c>34</c>), every pair of 55 characters met in documents (dashes, quotes, brackets, currency signs, the
/// no-break and zero-width characters) in both contexts, 104 strings of ordinary documents, ASCII punctuation beside
/// letters that are not ASCII, the currency, percent and degree signs beside ASCII punctuation in a number and before
/// the inverted exclamation and question marks, the middle dots and the characters of class AI in the contexts of every
/// rule, and the hyphen-minus after punctuation before letters that are not ASCII. Each row is a box 1 pt wide, so each of
/// its lines ends at the first opportunity after the line's start; with the rules found anew from each line start (THE
/// LINE START, below) they reproduce every row. The rows named below are written as text where they are ASCII; the other
/// characters are named.</para>
/// <para>Two readings come before the rules. U+00B7 MIDDLE DOT and U+0387 GREEK ANO TELEIA (class IS in Unicode 18) are
/// read as a letter of class AL beyond ASCII, for the standard's opportunities as well as for the rules: rows <c>ab!</c>
/// MIDDLE DOT <c>cd</c> (a break after the exclamation mark), <c>ab</c> MIDDLE DOT <c>(cd</c> and <c>a</c> MIDDLE DOT
/// <c>$!d</c> (none), <c>ab/</c> GREEK ANO TELEIA <c>cd</c> (a break after the slash), and the same rows with a letter
/// in the place of the dot. And in the rules a character of class AI is of class AL, as UAX #14 LB1 resolves it for the
/// standard: rows <c>ab</c> SPACE EN DASH SECTION SIGN <c>cd</c> (no break after the dash), <c>ab</c> NO-BREAK SPACE
/// SOFT HYPHEN SECTION SIGN <c>cd</c> (a break after the soft hyphen).</para>
/// <para>The rules, in the order in which they are tried; the first that applies decides:</para>
/// <list type="number">
/// <item>THE SPACE RULE: after U+0020 SPACE a line may always break, unless a space or a mandatory break (a character of
/// class BK, CR, LF or NL) follows. This overrides UAX #14 LB7 (no break before ZW), LB11 (before WJ), LB13 (before CL,
/// CP, EX, SY), LB14 (after OP and spaces), LB15a, LB15d (before IS), LB16 and LB17 after a space. Rows <c>ab )cd</c>,
/// <c>ab /cd</c>, <c>ab( cd</c>, <c>ab</c> SPACE WORD JOINER <c>cd</c>, <c>ab</c> SPACE MIDDLE DOT <c>cd</c>.</item>
/// <item>THE MINUS-SIGN RULE: between U+002D HYPHEN-MINUS and an ASCII digit a line may break only when the character
/// before the hyphen-minus is an ASCII letter or digit, so a minus sign stays with its number. Rows <c>2026-11-28</c>
/// (a break after both hyphens), <c>REF-2026-0042</c>, <c>06-12345678</c>, <c>12--34</c> (no break after the second
/// hyphen), and none in <c>-5</c>, <c>12!-34</c>, <c>12 -34</c> and <c>a</c> E WITH ACUTE <c>-1cd</c>.</item>
/// <item>THE ASCII PAIR TABLE: between two printable ASCII characters other than the space (U+0021 to U+007E), Chromium
/// decides by a fixed table of 94 by 94 pairs, the same between letters and between digits, which
/// <see cref="AsciiPairs"/> holds as measured. It has four kinds of rows. After <c>$ ' ( / 0-9 &lt; @ A-Z [ ^ _ ` a-z {</c>
/// no break before any of them: rows <c>and/or</c>, <c>INV/2026/0042</c>, <c>/usr/local/share/doc</c>,
/// <c>C:\Users\jan\Documents\file.txt</c> (one piece each). After <c>! " # % &amp; ) * + , . : ; = &gt; \ ] | } ~</c>
/// a break only before <c>( &lt; [ {</c>: rows <c>a|b|c</c>, <c>C++</c>, <c>Hello!World</c> (one piece each), row
/// <c>ab;(cd</c> (a break). After <c>-</c> a break before everything except <c>! $ ) , . / : ; ? ] }</c> (row
/// <c>www.example.nl/over-ons</c>: after the hyphen only; row <c>ab -cd</c>: after the hyphen too, where LB20a keeps the
/// letter after a space and a hyphen). After <c>?</c> a break before everything except <c>! " ' ) , . / : ; ? ] }</c>
/// (row <c>https://example.com/a/b/c?d=e&amp;f=g#h</c>: after the question mark only). Where UAX #14 differs, its rules
/// give way to the table; a pair with one character outside the range follows the other rules.</item>
/// <item>THE LATIN-1 SIGN RULE: the six signs of class PR or PO in U+0080 to U+00FF (U+00A2 CENT SIGN, U+00A3 POUND SIGN,
/// U+00A4 CURRENCY SIGN, U+00A5 YEN SIGN, U+00B0 DEGREE SIGN, U+00B1 PLUS-MINUS SIGN) are not held to a number by LB25
/// across ASCII punctuation: a line may break between an ASCII character of class SY, IS, CL or CP (<c>/ : ; , . ) ] }</c>)
/// and such a sign, and between such a sign and an ASCII character of class OP (<c>( [ {</c>). Rows <c>12/</c> POUND SIGN
/// <c>34</c>, the same after <c>: ; , . ) ] }</c>, <c>12</c> POUND SIGN <c>(34</c>, the same before <c>[ {</c>, and all
/// of them with the other five signs, also with a character beyond U+00FF in the same paragraph. The signs of the same
/// classes beyond U+00FF keep LB25 (U+20AC EURO SIGN, U+20A4, U+20A7, U+20A9, U+20B9, U+20BA, U+20BD, U+20BF, U+2030,
/// U+2103, U+2116, U+2212, U+FFE1): row <c>12/</c> EURO SIGN <c>34</c> is one piece. Next to a digit the six signs stay:
/// row <c>12</c> POUND SIGN <c>34</c>. Beside an ASCII character of these classes the rule holds only for that ASCII
/// character: rows <c>12</c> RIGHT SQUARE BRACKET WITH QUILL POUND SIGN <c>34</c> and <c>12</c> GREEK QUESTION MARK
/// POUND SIGN <c>34</c> (class CL and IS beyond ASCII) are one piece. And a line may break between any sign of class PR
/// or PO up to U+00FF (also <c>$ % + \</c>) and U+00A1 INVERTED EXCLAMATION MARK or U+00BF INVERTED QUESTION MARK (class
/// OP), where LB25 keeps a sign with an opening punctuation before a digit: rows <c>12$</c> INVERTED EXCLAMATION MARK
/// <c>34</c>, <c>12</c> POUND SIGN INVERTED QUESTION MARK <c>34</c>; the signs beyond U+00FF and the characters of class
/// OP beyond U+00FF keep LB25 (rows <c>12</c> EURO SIGN INVERTED EXCLAMATION MARK <c>34</c>, <c>12$</c> SINGLE LOW-9
/// QUOTATION MARK <c>34</c>: one piece).</item>
/// <item>BA BEFORE GL: between a character of class BA and one of class GL a line may break, where LB12a forbids it.
/// Rows <c>ab</c> EN DASH NO-BREAK SPACE <c>cd</c>, <c>ab|</c> NO-BREAK SPACE <c>cd</c>, <c>ab</c> EN DASH NARROW
/// NO-BREAK SPACE <c>cd</c>, <c>ab</c> EN DASH NON-BREAKING HYPHEN <c>cd</c>, <c>pp.</c> NO-BREAK SPACE <c>10</c> EN
/// DASH <c>12</c>.</item>
/// <item>THE SOFT HYPHEN: after U+00AD SOFT HYPHEN a line may break before a character of class AL, HL or NU, also where
/// LB12 and LB20a keep it after a no-break or zero-width character, as if the soft hyphen were of class BA. Rows
/// <c>ab</c> NO-BREAK SPACE SOFT HYPHEN <c>cd</c>, <c>ab</c> ZERO WIDTH SPACE SOFT HYPHEN <c>cd</c>. Before a digit the
/// standard gives the same break (row <c>ab</c> NO-BREAK SPACE SOFT HYPHEN <c>1cd</c>).</item>
/// <item>THE DASH AFTER A SPACE: U+2012 FIGURE DASH and U+2013 EN DASH at the start of the text or after a character of
/// class SP, GL, ZW, BK, CR, LF, NL or CB keep a following letter (class AL or HL), as LB20a keeps one after a hyphen
/// there. Rows <c>ab</c> SPACE EN DASH <c>cd</c>, <c>ab</c> SPACE FIGURE DASH <c>cd</c>, <c>ab</c> NO-BREAK SPACE EN DASH
/// <c>cd</c>, EN DASH <c>cd</c> at the start of the text.</item>
/// </list>
/// <para>The rules look at the two code points beside a place and at the one before them, by their Line_Break class as
/// LineBreak.txt gives it, with class AI read as AL; the other classes LB1 resolves (SA, SG, XX, CJ) are read as they
/// are. Combining marks are not joined to their base for them (LB9), so a pair with a mark follows the standard. Not measured, and so the standard's answer: pairs of characters outside the
/// measured set in contexts other than the measured ones, text in scripts no repository font draws (CJK, Thai and the
/// other scripts of class SA, ID, H2, H3), and the dashes after a no-break space before letters that are not ASCII.</para>
/// <para>THE LINE START: Chromium finds the opportunities of each line anew from the line's start, so a pair right after a
/// line end is decided as at the start of a text. The matrix shows it, as each of its lines ends at the first opportunity:
/// row <c>a--</c> E WITH ACUTE <c>d</c> breaks after the first hyphen only, as the second hyphen then starts a line and
/// UAX #14 LB20a keeps the letter after a hyphen at the start of a text; in running text Chromium breaks after both
/// hyphens (case <c>lb-dash-before-sign</c>: <c>a--</c> / <c>élan</c>). Every row of the matrix is reproduced so, with
/// the rules found anew from each line start. LIMIT: this class finds the opportunities once, for the whole text. That
/// differs from Chromium only where a HYPHEN-MINUS starts a line after a hyphen-minus or a question mark (a line ends
/// between the two characters of <c>--</c> or <c>?-</c>) and a letter beyond ASCII, a character of class AI or a middle
/// dot follows it: this class has an opportunity after that hyphen, Chromium has none. It has two effects. In a box
/// narrower than the hyphen with the piece after it, the engine breaks again after the hyphen where Chromium lets the
/// piece overflow (case <c>lb-dash-before-sign</c>, 30 pt paragraph: Chromium <c>x-</c> / <c>-Ωmega</c>, the engine
/// <c>x-</c> / <c>-</c> / <c>Ωmega</c>; 476 rows of the matrix, <c>ChromiumLineBreakingTests</c>). And a
/// shrink-to-fit width (a table column in the automatic layout, an inline-block) is narrower by the hyphen's width while
/// every line is equal, as Chromium measures the smallest width with the hyphen and the piece after it (case
/// <c>lb-line-start-width</c>, a table 1 pt wide with automatic layout: 3.89 pt narrower). The running-text resource of
/// the tests shows the first effect only on a later line, never on the first line of a block.</para>
/// <para>BOUNDED WORK: one copy of the text where it holds a middle dot, one pass of <see cref="UnicodeLineBreaking.Find"/>,
/// then one pass over the code points, each decided by a table lookup and a look-behind of at most two code points.</para>
/// </remarks>
public static partial class ChromiumLineBreaking
{
    /// <summary>
    /// Finds the break opportunities of a text as Chromium's print has them: the standard's (<see cref="UnicodeLineBreaking.Find"/>)
    /// adjusted by the rules of this class. The start of the text is never one; the end always is, as a mandatory break,
    /// unless the text is empty.
    /// </summary>
    /// <param name="text">The text, for example a paragraph after white-space collapsing. An unpaired surrogate counts as
    /// U+FFFD.</param>
    /// <returns>The opportunities in text order.</returns>
    public static ImmutableArray<BreakOpportunity> Find(string text)
    {
        // The middle dots read as a letter; each is one UTF-16 code unit, as the letter is, so the offsets stay.
        if (text.AsSpan().IndexOfAny(MiddleDot, GreekAnoTeleia) >= 0)
        {
            text = text.Replace(MiddleDot, Letter).Replace(GreekAnoTeleia, Letter);
        }

        var standard = UnicodeLineBreaking.Find(text);
        if (standard.IsEmpty)
        {
            return standard;
        }

        // The standard's decision at every offset: null where it has no opportunity.
        var decided = new bool?[text.Length + 1];
        foreach (var opportunity in standard)
        {
            decided[opportunity.Offset] = opportunity.IsMandatory;
        }

        var (earlier, before) = (-1, -1);
        var offset = 0;
        while (offset < text.Length)
        {
            Rune.DecodeFromUtf16(text.AsSpan(offset), out var rune, out var consumed);
            if (before >= 0 && decided[offset] is not true && Tailored(earlier, before, rune.Value) is { } breaks)
            {
                decided[offset] = breaks ? false : null;
            }

            (earlier, before) = (before, rune.Value);
            offset += consumed;
        }

        var opportunities = ImmutableArray.CreateBuilder<BreakOpportunity>();
        for (var at = 1; at <= text.Length; at++)
        {
            if (decided[at] is { } mandatory)
            {
                opportunities.Add(new BreakOpportunity(at, mandatory));
            }
        }

        return opportunities.ToImmutable();
    }

    /// <summary>The decision of the first rule that applies between <paramref name="before"/> and <paramref name="after"/>:
    /// true for a break, false for none, null where no rule applies and the standard decides.</summary>
    /// <param name="earlier">The code point before <paramref name="before"/>, or -1 at the start of the text.</param>
    /// <param name="before">The code point before the place.</param>
    /// <param name="after">The code point after the place.</param>
    private static bool? Tailored(int earlier, int before, int after)
    {
        var afterClass = ClassOf(after);
        if (before == ' ')
        {
            // The space rule.
            return after == ' ' || afterClass is BK or CR or LF or NL ? null : true;
        }

        if (before == '-' && after is >= '0' and <= '9')
        {
            // The minus-sign rule.
            return earlier is >= '0' and <= '9' or >= 'A' and <= 'Z' or >= 'a' and <= 'z';
        }

        if (IsAsciiPrintable(before) && IsAsciiPrintable(after))
        {
            // The ASCII pair table.
            return AsciiPairs[before - FirstAscii][after - FirstAscii] == Break;
        }

        var beforeClass = ClassOf(before);
        if ((before < 0x80 && beforeClass is SY or IS or CL or CP && IsLatin1Sign(after))
            || (IsLatin1Sign(before) && after < 0x80 && afterClass == OP)
            || (before <= 0xFF && beforeClass is PR or PO && after is InvertedExclamationMark or InvertedQuestionMark))
        {
            // The Latin-1 sign rule.
            return true;
        }

        if (beforeClass == BA && afterClass == GL)
        {
            // BA before GL.
            return true;
        }

        if (before == SoftHyphen && afterClass is AL or HL or NU)
        {
            // The soft hyphen.
            return true;
        }

        if (before is FigureDash or EnDash && afterClass is AL or HL
            && (earlier < 0 || ClassOf(earlier) is SP or GL or ZW or BK or CR or LF or NL or CB))
        {
            // The dash after a space.
            return false;
        }

        return null;
    }

    /// <summary>The first character of <see cref="AsciiPairs"/>: U+0021 EXCLAMATION MARK; the last is U+007E TILDE.</summary>
    internal const int FirstAscii = 0x21;

    /// <summary>The mark of a pair in <see cref="AsciiPairs"/> between which a line may break.</summary>
    internal const char Break = '|';

    /// <summary>The mark of a pair in <see cref="AsciiPairs"/> between which a line does not break.</summary>
    internal const char NoBreak = '.';

    private const int SoftHyphen = 0x00AD;
    private const int FigureDash = 0x2012;
    private const int EnDash = 0x2013;
    private const int InvertedExclamationMark = 0x00A1;
    private const int InvertedQuestionMark = 0x00BF;
    private const char MiddleDot = '\u00B7';
    private const char GreekAnoTeleia = '\u0387';

    /// <summary>The letter the middle dots are read as: U+00E9 LATIN SMALL LETTER E WITH ACUTE, of class AL and beyond
    /// ASCII, as both dots are.</summary>
    private const char Letter = '\u00E9';

    private static bool IsAsciiPrintable(int codePoint) => codePoint is >= FirstAscii and <= 0x7E;

    /// <summary>A sign of class PR or PO in U+0080 to U+00FF: one of the six of the Latin-1 sign rule.</summary>
    private static bool IsLatin1Sign(int codePoint) => codePoint is >= 0x80 and <= 0xFF && ClassOf(codePoint) is PR or PO;

    /// <summary>The Line_Break class of a code point as the rules read it: as LineBreak.txt gives it, with class AI read
    /// as AL (UAX #14 LB1).</summary>
    private static LineBreakClass ClassOf(int codePoint) => UnicodeProperties.Of(codePoint).Class switch
    {
        AI => AL,
        var found => found,
    };
}
