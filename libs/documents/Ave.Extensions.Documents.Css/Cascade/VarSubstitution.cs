using System;
using System.Collections.Generic;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Syntax;

namespace Ave.Extensions.Documents.Css.Cascade;

/// <summary>
/// <c>var()</c> (CSS Variables 1 §3): <c>var( &lt;custom-property-name&gt; , &lt;declaration-value&gt;? )</c>, replaced by
/// the custom property's computed value, or by the fallback when the property has the guaranteed-invalid value. The
/// fallback is substituted too. Bounded: the result has at most <see cref="CssInputLimits.MaxSubstitutionValues"/>
/// component values.
/// </summary>
internal static class VarSubstitution
{
    /// <summary>The outcome of one substitution.</summary>
    public enum Outcome
    {
        /// <summary>Substituted.</summary>
        Done,

        /// <summary>A var() named a property with no value and had no fallback: invalid at computed-value time.</summary>
        Invalid,

        /// <summary>Past the limit.</summary>
        TooLarge,
    }

    /// <summary>Whether every var() in the values has the grammar of §3 (else the declaration is invalid when parsed,
    /// as Chromium reads it).</summary>
    public static bool IsWellFormed(IReadOnlyList<ComponentValue> values)
    {
        foreach (var value in values)
        {
            switch (value)
            {
                case FunctionValue function when function.IsNamed("var"):
                    if (Arguments(function) is not { } arguments || (arguments.Fallback is { } fallback && !IsWellFormed(fallback)))
                    {
                        return false;
                    }

                    break;
                case FunctionValue function when !IsWellFormed(function.Values):
                    return false;
                case SimpleBlock block when !IsWellFormed(block.Values):
                    return false;
            }
        }

        return true;
    }

    /// <summary>Substitutes every var() in the values.</summary>
    /// <param name="values">The values.</param>
    /// <param name="lookup">A custom property's computed value by name, or null for the guaranteed-invalid value.</param>
    /// <param name="result">The values after substitution.</param>
    /// <returns>Whether it worked.</returns>
    public static Outcome Substitute(IReadOnlyList<ComponentValue> values, Func<string, ValueList<ComponentValue>?> lookup, out ValueList<ComponentValue> result)
    {
        var budget = CssInputLimits.MaxSubstitutionValues;
        var output = new List<ComponentValue>();
        var outcome = Into(values, lookup, output, ref budget);
        result = outcome == Outcome.Done ? [.. output] : [];
        return outcome;
    }

    private static Outcome Into(IReadOnlyList<ComponentValue> values, Func<string, ValueList<ComponentValue>?> lookup, List<ComponentValue> output, ref int budget)
    {
        foreach (var value in values)
        {
            Outcome outcome;
            switch (value)
            {
                case FunctionValue function when function.IsNamed("var"):
                    if (Arguments(function) is not { } arguments)
                    {
                        return Outcome.Invalid;
                    }

                    if (lookup(arguments.Name) is { } substituted)
                    {
                        budget -= substituted.Count;
                        if (budget < 0)
                        {
                            return Outcome.TooLarge;
                        }

                        output.AddRange(substituted);
                        continue;
                    }

                    if (arguments.Fallback is not { } fallback)
                    {
                        return Outcome.Invalid;
                    }

                    outcome = Into(DeclarationPreparer.Trim(fallback), lookup, output, ref budget);
                    break;
                case FunctionValue function:
                    var inside = new List<ComponentValue>();
                    outcome = Into(function.Values, lookup, inside, ref budget);
                    output.Add(function with { Values = [.. inside] });
                    budget--;
                    break;
                case SimpleBlock block:
                    var contents = new List<ComponentValue>();
                    outcome = Into(block.Values, lookup, contents, ref budget);
                    output.Add(block with { Values = [.. contents] });
                    budget--;
                    break;
                default:
                    output.Add(value);
                    budget--;
                    outcome = Outcome.Done;
                    break;
            }

            if (outcome != Outcome.Done)
            {
                return outcome;
            }

            if (budget < 0)
            {
                return Outcome.TooLarge;
            }
        }

        return Outcome.Done;
    }

    /// <summary>The arguments of var(): the name, and the fallback after the first comma (null when there is no comma;
    /// it may be empty).</summary>
    private static (string Name, List<ComponentValue>? Fallback)? Arguments(FunctionValue function)
    {
        var index = 0;
        var values = function.Values;
        while (index < values.Count && values[index] is CssToken { Kind: CssTokenKind.Whitespace })
        {
            index++;
        }

        if (index >= values.Count || values[index] is not CssToken { Kind: CssTokenKind.Ident } name || !name.Text.StartsWith("--", StringComparison.Ordinal))
        {
            return null;
        }

        index++;
        while (index < values.Count && values[index] is CssToken { Kind: CssTokenKind.Whitespace })
        {
            index++;
        }

        if (index == values.Count)
        {
            return (name.Text, null);
        }

        if (values[index] is not CssToken { Kind: CssTokenKind.Comma })
        {
            return null;
        }

        var fallback = new List<ComponentValue>();
        for (index++; index < values.Count; index++)
        {
            fallback.Add(values[index]);
        }

        return (name.Text, fallback);
    }
}
