using System;
using System.Collections.Generic;
using System.Linq;
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
/// A substitution can also nest: <c>--b: (var(--a))</c> puts the value of <c>--a</c> one block deeper, so a chain of
/// such properties builds a value nested as deep as the chain is long, deeper than any parsed value. The depth of the
/// result is counted while it is made, and a value nested deeper than <see cref="CssInputLimits.MaxDepth"/> is
/// refused (<see cref="Outcome.TooDeep"/>): every reader of values (the serializer, math functions, the property
/// grammars) may then recurse once per level, as it does on parsed values.
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

        /// <summary>Its blocks and functions nest deeper than <see cref="CssInputLimits.MaxDepth"/>: refused.</summary>
        TooDeep,
    }

    /// <summary>The characters per substitution by which the engine's measure of a substituted value may differ from
    /// Chromium's (the comments Chromium writes between tokens): a value within that many characters per var() of
    /// <see cref="CssInputLimits.MaxSubstitutionLength"/> is <see cref="Outcome.Uncertain"/>.</summary>
    internal const int UncertainCharactersPerSubstitution = 8;

    /// <summary>Whether every var() in the values has the grammar of §3 (else the declaration is invalid when parsed,
    /// as Chromium reads it): a custom property name, and a fallback that is a &lt;declaration-value&gt;, so with no
    /// semicolon at its own top level and no unmatched ), ] or } (CSS Syntax 3 §8.2; Chromium 159 drops
    /// <c>color: var(--x, a;b)</c> and <c>color: var(--x, ])</c>, measured).</summary>
    public static bool IsWellFormed(IReadOnlyList<ComponentValue> values)
    {
        foreach (var value in values)
        {
            switch (value)
            {
                case FunctionValue function when function.IsNamed("var"):
                    if (Arguments(function) is not { } arguments
                        || (arguments.Fallback is { } fallback && (HasTopLevel(fallback, CssTokenKind.Semicolon) || HasUnmatchedClosing(fallback) || !IsWellFormed(fallback))))
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

    /// <summary>Whether the values hold a ), ] or } that closes nothing, at any depth (a block or function the parser
    /// built is matched; a closing token left as a token is not), without recursion.</summary>
    private static bool HasUnmatchedClosing(IReadOnlyList<ComponentValue> values)
    {
        var pending = new Stack<IReadOnlyList<ComponentValue>>();
        pending.Push(values);
        while (pending.Count > 0)
        {
            foreach (var value in pending.Pop())
            {
                switch (value)
                {
                    case CssToken { Kind: CssTokenKind.CloseParen or CssTokenKind.CloseSquare or CssTokenKind.CloseCurly }:
                        return true;
                    case FunctionValue function:
                        pending.Push(function.Values);
                        break;
                    case SimpleBlock block:
                        pending.Push(block.Values);
                        break;
                }
            }
        }

        return false;
    }

    /// <summary>Whether a var() in the values has a '!' at the top level of its fallback, which no
    /// &lt;declaration-value&gt; holds (CSS Syntax 3 §8.2).</summary>
    public static bool HasBangInFallback(IReadOnlyList<ComponentValue> values)
    {
        var pending = new Stack<IReadOnlyList<ComponentValue>>();
        pending.Push(values);
        while (pending.Count > 0)
        {
            foreach (var value in pending.Pop())
            {
                switch (value)
                {
                    case FunctionValue function when function.IsNamed("var") && Arguments(function) is { Fallback: { } fallback }:
                        if (fallback.Any(item => item is CssToken token && token.IsDelim('!')))
                        {
                            return true;
                        }

                        pending.Push(fallback);
                        break;
                    case FunctionValue function:
                        pending.Push(function.Values);
                        break;
                    case SimpleBlock block:
                        pending.Push(block.Values);
                        break;
                }
            }
        }

        return false;
    }

    private static bool HasTopLevel(IReadOnlyList<ComponentValue> values, CssTokenKind kind) =>
        values.Any(value => value is CssToken token && token.Kind == kind);

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
        stack.Push(new Frame(values, root, null, null, 0));
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
                        if (frame.Depth + Depth(substituted.Tokens) > CssInputLimits.MaxDepth)
                        {
                            length = lower;
                            return Outcome.TooDeep;
                        }

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
                    stack.Push(new Frame(Trimmed(fallback), frame.Output, null, null, frame.Depth));
                    break;
                case FunctionValue function:
                    lower += function.Name.Length + 2;
                    stack.Push(new Frame(function.Values, [], function, frame.Output, frame.Depth + 1));
                    break;
                case SimpleBlock block:
                    lower += 2;
                    stack.Push(new Frame(block.Values, [], block, frame.Output, frame.Depth + 1));
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
        if (lower + (UncertainCharactersPerSubstitution * substitutions) > CssInputLimits.MaxSubstitutionLength)
        {
            return Outcome.Uncertain;
        }

        result = [.. root];
        return Outcome.Done;
    }

    /// <summary>How deeply the blocks and functions of the values nest (a value without any: 0), without recursion. Its
    /// cost is the number of values, which is at most the length of their text, so a caller that charges that length
    /// has paid for it.</summary>
    public static int Depth(IReadOnlyList<ComponentValue> values)
    {
        var deepest = 0;
        var stack = new Stack<(IReadOnlyList<ComponentValue> Values, int Depth)>();
        stack.Push((values, 0));
        while (stack.Count > 0)
        {
            var (list, depth) = stack.Pop();
            foreach (var value in list)
            {
                IReadOnlyList<ComponentValue>? inner = value switch
                {
                    FunctionValue function => function.Values,
                    SimpleBlock block => block.Values,
                    _ => null,
                };
                if (inner is not null)
                {
                    deepest = Math.Max(deepest, depth + 1);
                    stack.Push((inner, depth + 1));
                }
            }
        }

        return deepest;
    }

    /// <summary>One list being substituted: its source values, where it is, its output, and for a function or block
    /// the value to rebuild and the list it goes into, and how deeply its output is nested in the result.</summary>
    private sealed class Frame(IReadOnlyList<ComponentValue> source, List<ComponentValue> output, ComponentValue? owner, List<ComponentValue>? into, int depth)
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

        /// <summary>The blocks and functions of the result around <see cref="Output"/>.</summary>
        public int Depth { get; } = depth;
    }

    /// <summary>The name and fallback of a well-formed var() (see <see cref="Arguments"/>), or null.</summary>
    public static (string Name, IReadOnlyList<ComponentValue>? Fallback)? ArgumentsOf(FunctionValue function) => Arguments(function);

    /// <summary>The arguments of var(): the name, and the fallback after the first comma (null when there is no comma;
    /// it may be empty). The fallback is a view of the function's values, not a copy: a fallback that is not used costs
    /// nothing however long it is.</summary>
    private static (string Name, IReadOnlyList<ComponentValue>? Fallback)? Arguments(FunctionValue function)
    {
        var index = 0;
        var values = function.Values;
        while (index < values.Count && values[index] is CssToken { Kind: CssTokenKind.Whitespace })
        {
            index++;
        }

        // A custom property name is a dashed identifier other than '--' alone, which CSS Variables 1 §2 reserves.
        if (index >= values.Count || values[index] is not CssToken { Kind: CssTokenKind.Ident } name || !name.Text.StartsWith("--", StringComparison.Ordinal) || name.Text.Length == 2)
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

        return (name.Text, new Slice(values, index + 1, values.Count));
    }

    /// <summary>A view without the white space at its start and end.</summary>
    private static IReadOnlyList<ComponentValue> Trimmed(IReadOnlyList<ComponentValue> values)
    {
        var (list, start, end) = values is Slice slice ? (slice.Values, slice.Start, slice.End) : (values, 0, values.Count);
        while (start < end && list[start] is CssToken { Kind: CssTokenKind.Whitespace })
        {
            start++;
        }

        while (end > start && list[end - 1] is CssToken { Kind: CssTokenKind.Whitespace })
        {
            end--;
        }

        return new Slice(list, start, end);
    }

    /// <summary>A part of a list of values, without a copy.</summary>
    private sealed class Slice(IReadOnlyList<ComponentValue> values, int start, int end) : IReadOnlyList<ComponentValue>
    {
        public IReadOnlyList<ComponentValue> Values { get; } = values;

        public int Start { get; } = start;

        public int End { get; } = end;

        public int Count => End - Start;

        public ComponentValue this[int index] => Values[Start + index];

        public IEnumerator<ComponentValue> GetEnumerator()
        {
            for (var i = Start; i < End; i++)
            {
                yield return Values[i];
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
