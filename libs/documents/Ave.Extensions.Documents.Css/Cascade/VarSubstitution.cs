using System;
using System.Collections.Generic;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Syntax;
using Ave.Extensions.Documents.Css.Values;

namespace Ave.Extensions.Documents.Css.Cascade;

/// <summary>
/// <c>var()</c> (CSS Variables 1 §3): <c>var( &lt;custom-property-name&gt; , &lt;declaration-value&gt;? )</c>, replaced by
/// the custom property's computed value, or by the fallback when the property has the guaranteed-invalid value. The
/// fallback is substituted too.
/// </summary>
/// <remarks>
/// Bounded, and without recursion over the value: the component-value tree is walked with an explicit stack. The
/// result is measured as Chromium 159 measures it, by the length of its text: the source text of the literal parts plus
/// the text of the substituted values. Chromium writes an empty comment where two tokens would run together, which
/// this count leaves out, so the count is a lower bound and, with up to four characters on each side of every
/// substitution, an upper bound. Within <see cref="CssInputLimits.MaxSubstitutionLength"/> by the upper bound the
/// value is used; past it by the lower bound it is invalid at computed-value time, as in Chromium; in between the
/// answer depends on comments the engine does not write, and the value is refused
/// (<see cref="Outcome.Uncertain"/>). Substitution stops as soon as the lower bound passes the limit.
/// </remarks>
internal static class VarSubstitution
{
    /// <summary>The outcome of one substitution.</summary>
    public enum Outcome
    {
        /// <summary>Substituted.</summary>
        Done,

        /// <summary>A var() named a property with no value and had no fallback: invalid at computed-value time.</summary>
        Invalid,

        /// <summary>Longer than Chromium's limit: invalid at computed-value time, as in Chromium.</summary>
        TooLong,

        /// <summary>So close to Chromium's limit that whether Chromium keeps it depends on comments it writes: refused.</summary>
        Uncertain,
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
    /// <param name="length">The length of the result's text (the lower bound).</param>
    /// <returns>Whether it worked.</returns>
    public static Outcome Substitute(IReadOnlyList<ComponentValue> values, Func<string, CssTokens?> lookup, out ValueList<ComponentValue> result, out long length)
    {
        var root = new List<ComponentValue>();
        var stack = new Stack<Frame>();
        stack.Push(new Frame(values, root, null, null));
        long lower = 0;
        long substitutions = 0;
        result = [];
        length = 0;
        while (stack.Count > 0)
        {
            var frame = stack.Peek();
            if (frame.Index >= frame.Source.Count)
            {
                stack.Pop();
                if (frame.Owner is FunctionValue function)
                {
                    frame.Into!.Add(function with { Values = [.. frame.Output] });
                }
                else if (frame.Owner is SimpleBlock block)
                {
                    frame.Into!.Add(block with { Values = [.. frame.Output] });
                }

                continue;
            }

            var value = frame.Source[frame.Index++];
            switch (value)
            {
                case FunctionValue function when function.IsNamed("var"):
                    if (Arguments(function) is not { } arguments)
                    {
                        return Outcome.Invalid;
                    }

                    if (lookup(arguments.Name) is { } substituted)
                    {
                        lower += substituted.Text.Length;
                        substitutions++;
                        frame.Output.AddRange(substituted.Tokens);
                        break;
                    }

                    if (arguments.Fallback is not { } fallback)
                    {
                        return Outcome.Invalid;
                    }

                    // The fallback's values go into the same output, in the place of the var().
                    stack.Push(new Frame(DeclarationPreparer.Trim(fallback), frame.Output, null, null));
                    break;
                case FunctionValue function:
                    lower += function.Name.Length + 2;
                    stack.Push(new Frame(function.Values, [], function, frame.Output));
                    break;
                case SimpleBlock block:
                    lower += 2;
                    stack.Push(new Frame(block.Values, [], block, frame.Output));
                    break;
                default:
                    lower += Math.Max(1, value.SourceEnd - value.SourceStart);
                    frame.Output.Add(value);
                    break;
            }

            if (lower > CssInputLimits.MaxSubstitutionLength)
            {
                length = lower;
                return Outcome.TooLong;
            }
        }

        length = lower;
        if (lower + (8 * substitutions) > CssInputLimits.MaxSubstitutionLength)
        {
            return Outcome.Uncertain;
        }

        result = [.. root];
        return Outcome.Done;
    }

    /// <summary>The names every var() in the values refers to, fallbacks included (the edges of the dependency graph of
    /// CSS Variables 1 §2.3), without recursion.</summary>
    public static List<string> References(IReadOnlyList<ComponentValue> values)
    {
        var names = new List<string>();
        var stack = new Stack<IReadOnlyList<ComponentValue>>();
        stack.Push(values);
        while (stack.Count > 0)
        {
            foreach (var value in stack.Pop())
            {
                switch (value)
                {
                    case FunctionValue function when function.IsNamed("var") && Arguments(function) is { } arguments:
                        names.Add(arguments.Name);
                        if (arguments.Fallback is { } fallback)
                        {
                            stack.Push(fallback);
                        }

                        break;
                    case FunctionValue function:
                        stack.Push(function.Values);
                        break;
                    case SimpleBlock block:
                        stack.Push(block.Values);
                        break;
                }
            }
        }

        return names;
    }

    /// <summary>One list being substituted: its source values, where it is, its output, and for a function or block
    /// the value to rebuild and the list it goes into.</summary>
    private sealed class Frame(IReadOnlyList<ComponentValue> source, List<ComponentValue> output, ComponentValue? owner, List<ComponentValue>? into)
    {
        /// <summary>The values of this list as written.</summary>
        public IReadOnlyList<ComponentValue> Source { get; } = source;

        /// <summary>The next value of <see cref="Source"/> to substitute.</summary>
        public int Index { get; set; }

        /// <summary>The values substituted so far.</summary>
        public List<ComponentValue> Output { get; } = output;

        /// <summary>The function or block this list is the contents of, rebuilt with <see cref="Output"/>; null for the
        /// top list.</summary>
        public ComponentValue? Owner { get; } = owner;

        /// <summary>The output of the enclosing list, where the rebuilt <see cref="Owner"/> goes.</summary>
        public List<ComponentValue>? Into { get; } = into;
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
