using Ave.Extensions.Documents.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using Ave.Extensions.Documents.Css.Syntax;

namespace Ave.Extensions.Documents.Css.Selectors;

/// <summary>The &lt;a-n-plus-b&gt; type in terms of CSS tokens (CSS Syntax 3 §6.2).</summary>
internal static class AnPlusBParser
{
    /// <summary>Parses component values as An+B; null when they are not one. White space around and between the parts
    /// is allowed, except between a '+' and an ident that starts with n.</summary>
    /// <param name="values">The values (without the <c>of S</c> part).</param>
    /// <returns>A and B, or null.</returns>
    public static AnPlusB? Parse(IReadOnlyList<ComponentValue> values)
    {
        var tokens = new List<CssToken>();
        var spaceBefore = new List<bool>();
        var sawSpace = false;
        foreach (var value in values)
        {
            if (value is not CssToken token)
            {
                return null;
            }

            if (token.Kind == CssTokenKind.Whitespace)
            {
                sawSpace = true;
                continue;
            }

            tokens.Add(token);
            spaceBefore.Add(sawSpace);
            sawSpace = false;
        }

        if (tokens.Count == 0)
        {
            return null;
        }

        var first = tokens[0];

        // odd | even | <integer>
        if (tokens.Count == 1)
        {
            if (first.IsIdent("odd"))
            {
                return new AnPlusB(2, 1);
            }

            if (first.IsIdent("even"))
            {
                return new AnPlusB(2, 0);
            }

            if (first is { Kind: CssTokenKind.Number, IsInteger: true })
            {
                return new AnPlusB(0, (long)first.Number);
            }
        }

        // The "n" part: a dimension with unit n…, an ident n… or -n…, or '+' and an ident n… with nothing between.
        long a;
        string rest;
        var index = 1;
        if (first is { Kind: CssTokenKind.Dimension, IsInteger: true })
        {
            a = (long)first.Number;
            rest = InfraAscii.ToLower(first.Text);
        }
        else if (first.Kind == CssTokenKind.Ident)
        {
            var lower = InfraAscii.ToLower(first.Text);
            if (lower.StartsWith("-n", StringComparison.Ordinal))
            {
                a = -1;
                rest = lower[1..];
            }
            else
            {
                a = 1;
                rest = lower;
            }
        }
        else if (first.IsDelim('+') && tokens.Count > 1 && !spaceBefore[1] && tokens[1].Kind == CssTokenKind.Ident && InfraAscii.ToLower(tokens[1].Text).StartsWith('n'))
        {
            a = 1;
            rest = InfraAscii.ToLower(tokens[1].Text);
            index = 2;
        }
        else
        {
            return null;
        }

        if (!rest.StartsWith('n'))
        {
            return null;
        }

        var after = rest[1..];
        var remaining = tokens.Count - index;
        if (after.Length == 0)
        {
            // n | n <signed-integer> | n ['+'|'-'] <signless-integer>
            if (remaining == 0)
            {
                return new AnPlusB(a, 0);
            }

            var next = tokens[index];
            if (remaining == 1 && next is { Kind: CssTokenKind.Number, IsInteger: true, Sign: not null })
            {
                return new AnPlusB(a, (long)next.Number);
            }

            if (remaining == 2 && (next.IsDelim('+') || next.IsDelim('-')) && tokens[index + 1] is { Kind: CssTokenKind.Number, IsInteger: true, Sign: null } number)
            {
                return new AnPlusB(a, next.IsDelim('-') ? -(long)number.Number : (long)number.Number);
            }

            return null;
        }

        if (after == "-")
        {
            // n- <signless-integer>
            return remaining == 1 && tokens[index] is { Kind: CssTokenKind.Number, IsInteger: true, Sign: null } number
                ? new AnPlusB(a, -(long)number.Number)
                : null;
        }

        // n-<digits>: B is negative.
        if (remaining == 0 && after.Length > 1 && after[0] == '-' && IsDigits(after.AsSpan(1)))
        {
            return long.TryParse(after.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var b) ? new AnPlusB(a, -b) : null;
        }

        return null;
    }

    private static bool IsDigits(ReadOnlySpan<char> text)
    {
        foreach (var c in text)
        {
            if (!InfraAscii.IsDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
