using System;
using System.Collections.Generic;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Properties;
using Ave.Extensions.Documents.Css.Syntax;
using Ave.Extensions.Documents.Css.Values;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents.Css.Cascade;

/// <summary>One declaration as the cascade keeps it: where it came from and its place in the order of appearance. Its
/// identity is what diagnostics are reported once for.</summary>
/// <param name="Source">The declaration as written.</param>
/// <param name="SourceName">The style sheet's name.</param>
/// <param name="Name">The property name: lowercase, or as written for a custom property.</param>
/// <param name="Important">Whether it is important.</param>
/// <param name="Order">Its place in the order of appearance (CSS Cascade 5 §6.1, the last criterion).</param>
internal sealed record PreparedDeclaration(Declaration Source, string SourceName, string Name, bool Important, int Order)
{
    /// <summary>Two declarations are equal only when they are the same one, so one written twice is reported twice.
    /// </summary>
    public bool Equals(PreparedDeclaration? other) => ReferenceEquals(this, other);

    /// <inheritdoc />
    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
}

/// <summary>What a declaration says about one property, before cascading.</summary>
internal abstract record EntryValue;

/// <summary>A specified value that parsed and is supported.</summary>
internal sealed record SpecifiedEntry(CssValue Value) : EntryValue;

/// <summary>A CSS-wide keyword (CSS Cascade 5 §7.3): initial, inherit, unset, revert or revert-layer.</summary>
internal sealed record WideKeywordEntry(string Keyword) : EntryValue;

/// <summary>A value with <c>var()</c> in it (CSS Variables 1 §3): parsed only after substitution, as the longhand, as
/// <paramref name="Shorthand"/> (a pending-substitution value, §3.2), or as <c>all</c>.</summary>
internal sealed record PendingEntry(ShorthandDefinition? Shorthand, bool IsAll) : EntryValue;

/// <summary>A valid value the engine refuses; reported when it would win, and then skipped.</summary>
internal sealed record RefusedEntry(ErrorCode Code, string Problem) : EntryValue;

/// <summary>A custom property's value (CSS Variables 1 §2): component values with their text.</summary>
internal sealed record CustomEntry(ValueList<ComponentValue> Values, string Text, bool HasVar) : EntryValue;

/// <summary>One property set by one declaration: a longhand, a custom property by name, or (neither) <c>all</c>.</summary>
internal readonly record struct Entry(PropertyDefinition? Longhand, string? Custom, EntryValue Value, PreparedDeclaration Declaration);

/// <summary>A diagnostic reported when its declaration first applies to an element, once for that declaration.</summary>
/// <param name="Declaration">The declaration it is about: what it is reported once for.</param>
/// <param name="Diagnostic">The diagnostic.</param>
internal readonly record struct Notice(PreparedDeclaration Declaration, Error Diagnostic);

/// <summary>A rule's declarations, prepared once: the entries, and the diagnostics reported once per declaration when the
/// rule first applies to an element (properties the engine ignores by rule, or does not know).</summary>
internal sealed record PreparedBlock(ValueList<Entry> Entries, ValueList<Notice> WhenApplied)
{
    /// <summary>A block with no declarations.</summary>
    public static PreparedBlock Empty { get; } = new([], []);

    /// <summary>Two blocks are equal only when they are the same one: a block is reported once per rule, not once per
    /// text.</summary>
    public bool Equals(PreparedBlock? other) => ReferenceEquals(this, other);

    /// <inheritdoc />
    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
}

/// <summary>
/// Turns declarations into cascade entries (CSS Cascade 5 §6, CSS Variables 1 §2–§3): custom properties keep their
/// values; a CSS-wide keyword sets every longhand it names; a value with <c>var()</c> waits for substitution; any other
/// value is parsed now by the property registry, a shorthand into all its longhands. Invalid declarations are dropped
/// and reported here, as a browser drops them while parsing; what is refused or ignored is reported when it applies.
/// </summary>
internal static class DeclarationPreparer
{
    private static readonly string[] WideKeywords = ["initial", "inherit", "unset", "revert", "revert-layer"];

    /// <summary>The functions that substitute like var() (CSS Environment Variables 1, CSS Values 5) and that Chromium 159
    /// supports: not supported here, so refused.</summary>
    private static readonly string[] UnsupportedSubstitutions = ["env", "attr", "if"];

    /// <summary>Prepares a rule's declarations in order, each with the next place in the order of appearance (CSS
    /// Cascade 5 §6.1). Invalid declarations are dropped and reported to <paramref name="diagnostics"/>; refused and
    /// ignored ones are kept, to be reported when they apply.</summary>
    public static PreparedBlock Prepare(IEnumerable<Declaration> declarations, string sourceName, CascadeOrigin origin, ref int order, DiagnosticCollector diagnostics)
    {
        var entries = new List<Entry>();
        var whenApplied = new List<Notice>();
        foreach (var declaration in declarations)
        {
            Prepare(declaration, sourceName, origin, order++, entries, whenApplied, diagnostics);
        }

        return entries.Count == 0 && whenApplied.Count == 0 ? PreparedBlock.Empty : new PreparedBlock(Unshadowed(entries), [.. whenApplied]);
    }

    /// <summary>The entries of one block that can win: of the entries for one property (or for <c>all</c>) with one
    /// importance, the last that is not refused and the refused ones after it. The earlier ones lose to it in every
    /// cascade (same origin, layer, specificity and element, later order), so a block that repeats a declaration costs
    /// one entry, not one per repetition. (A refused entry is skipped when it would win, so the one before it stays.)</summary>
    private static ValueList<Entry> Unshadowed(List<Entry> entries)
    {
        if (entries.Count < 2)
        {
            return [.. entries];
        }

        var settled = new HashSet<(int Longhand, string? Custom, bool Important)>();
        var kept = new List<Entry>(entries.Count);
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            var entry = entries[i];
            var key = (entry.Longhand?.Index ?? -1, entry.Custom, entry.Declaration.Important);
            if (settled.Contains(key))
            {
                continue;
            }

            kept.Add(entry);
            if (entry.Value is not RefusedEntry)
            {
                settled.Add(key);
            }
        }

        kept.Reverse();
        return [.. kept];
    }

    private static void Prepare(Declaration declaration, string sourceName, CascadeOrigin origin, int order, List<Entry> entries, List<Notice> whenApplied, DiagnosticCollector diagnostics)
    {
        var name = declaration.IsCustomProperty ? declaration.Name : InfraAscii.ToLower(declaration.Name);
        var prepared = new PreparedDeclaration(declaration, sourceName, name, declaration.Important && origin != CascadeOrigin.PresentationalHint, order);
        var wide = WideKeyword(declaration.Value);
        if (declaration.Value.Any(value => value is CssToken token && token.IsDelim('!')))
        {
            // A top-level '!' other than !important is not a <declaration-value> (CSS Syntax 3 §8.2): no property's
            // grammar takes it, a custom property's included, so Chromium drops the declaration.
            diagnostics.Add(Diagnostic(CssErrorCodes.Invalid.Value, $"A '!' that is not !important makes the declaration of '{CssSerializer.Shortened(declaration.Name)}' invalid; it is dropped (CSS Syntax 3 §8.2).", prepared));
            return;
        }

        if (declaration.IsCustomProperty && name == "--")
        {
            // '--' alone is reserved (CSS Variables 1 §2): no custom property has that name; Chromium 159 drops it.
            diagnostics.Add(Diagnostic(CssErrorCodes.Invalid.Property, "'--' alone is not a custom property name (CSS Variables 1 §2); the declaration is dropped.", prepared));
            return;
        }

        if (ContainsFunction(declaration.Value, "var") && !VarSubstitution.IsWellFormed(declaration.Value))
        {
            // CSS Variables 1 §3: a var() that does not match its grammar makes the declaration invalid when it is
            // parsed; Chromium 159 drops it (measured: 'var(--, yf)').
            diagnostics.Add(Diagnostic(CssErrorCodes.Invalid.Value, $"A var() in '{CssSerializer.Shortened(declaration.Name)}' does not match var( --name [, fallback]? ); the declaration is invalid and dropped (CSS Variables 1 §3).", prepared));
            return;
        }

        if (ContainsFunction(declaration.Value, "var") && ContainsNestedBang(declaration.Value))
        {
            // A '!' inside a var() (in its fallback): Chromium 159 drops some such declarations and makes others invalid at
            // computed-value time (measured: 'var(--x, a!b)' is dropped, 'var(--x, 5px !important)' unsets the property),
            // by a rule the engine does not follow, so it is refused.
            diagnostics.Add(Diagnostic(CssErrorCodes.Unsupported.Value, NotReadable(prepared, "a '!' inside a function") + "; the declaration is skipped.", prepared));
            return;
        }

        if (declaration.IsCustomProperty)
        {
            var values = Trim(declaration.Value);
            entries.Add(new Entry(null, name, wide is not null ? new WideKeywordEntry(wide) : new CustomEntry(values, declaration.OriginalText?.Trim() ?? CssSerializer.Serialize(values), ContainsFunction(values, "var")), prepared));
            return;
        }

        var longhand = PropertyRegistry.Find(name);
        var shorthand = longhand is null ? PropertyRegistry.FindShorthand(name) : null;
        var isAll = name == "all";
        if (longhand is null && shorthand is null && !isAll)
        {
            if (PropertyRegistry.FindIgnored(name) is { } ignored)
            {
                whenApplied.Add(new Notice(prepared, ignored.Refuses?.Invoke(declaration.Value) == true
                    ? Diagnostic(CssErrorCodes.Unsupported.Value, $"'{CssSerializer.Shortened(declaration.Name)}: {CssSerializer.Shortened(declaration.Value)}' is not supported: {ignored.RefusedValues}.", prepared)
                    : Diagnostic(CssErrorCodes.Ignored.Property, $"'{CssSerializer.Shortened(declaration.Name)}' is ignored: it concerns {ignored.Reason}.", prepared)));
            }
            else if (name.StartsWith("-moz-", StringComparison.Ordinal) || name.StartsWith("-ms-", StringComparison.Ordinal) || name.StartsWith("-o-", StringComparison.Ordinal))
            {
                diagnostics.Add(Diagnostic(CssErrorCodes.Invalid.Property, $"'{CssSerializer.Shortened(declaration.Name)}' is a property of another browser engine; the declaration is dropped.", prepared));
            }
            else
            {
                whenApplied.Add(new Notice(prepared, Diagnostic(CssErrorCodes.Unsupported.Property, $"The property '{CssSerializer.Shortened(declaration.Name)}' is not supported.", prepared)));
            }

            return;
        }

        IReadOnlyList<PropertyDefinition> targets = longhand is not null
            ? [longhand]
            : isAll
                ? []
                : [.. shorthand!.Longhands.Select(longhandName => PropertyRegistry.Find(longhandName)!)];

        void AddAll(EntryValue value)
        {
            if (isAll)
            {
                // One entry for every longhand but direction and unicode-bidi (CSS Cascade 5 §3.2): the cascade takes it
                // with each longhand's own candidates, so a declaration of all costs one entry, not one per longhand.
                entries.Add(new Entry(null, null, value, prepared));
                return;
            }

            foreach (var target in targets)
            {
                entries.Add(new Entry(target, null, value, prepared));
            }
        }

        if (wide is not null)
        {
            AddAll(new WideKeywordEntry(wide));
            return;
        }

        if (ContainsFunction(declaration.Value, "var"))
        {
            AddAll(new PendingEntry(shorthand, isAll));
            return;
        }

        if (isAll)
        {
            diagnostics.Add(Diagnostic(CssErrorCodes.Unsupported.Value, NotReadable(prepared, "'all' takes only a CSS-wide keyword") + "; the declaration is skipped.", prepared));
            return;
        }

        if (UnsupportedSubstitutions.FirstOrDefault(function => ContainsFunction(declaration.Value, function)) is { } substitution)
        {
            AddAll(new RefusedEntry(CssErrorCodes.Unsupported.Value, $"{substitution}() is not supported."));
            return;
        }

        if (UnsupportedMath(declaration.Value) is { } math)
        {
            AddAll(new RefusedEntry(CssErrorCodes.Unsupported.Calc, math));
            return;
        }

        // Chromium keeps its -internal- keywords for its own style sheet: an author's text-align: -internal-center is
        // dropped. Where a property takes a name (font-family, counter names, list-style-type, page), Chromium 159 reads
        // -internal-… as an ordinary name (measured), so that is left to the property's grammar.
        if (origin != CascadeOrigin.UserAgent && name == "text-align" && ContainsInternalKeyword(declaration.Value))
        {
            diagnostics.Add(Diagnostic(CssErrorCodes.Invalid.Value, $"A value starting with -internal- is for the user agent only; the declaration of '{CssSerializer.Shortened(declaration.Name)}' is dropped.", prepared));
            return;
        }

        if (longhand is not null)
        {
            var parsed = longhand.Parse(new ValueReader(declaration.Value));
            if (parsed.IsValid)
            {
                entries.Add(new Entry(longhand, null, new SpecifiedEntry(parsed.Value!), prepared));
            }
            else if (parsed.Unsupported)
            {
                entries.Add(new Entry(longhand, null, new RefusedEntry(parsed.Code ?? CssErrorCodes.Unsupported.Value, parsed.Problem!), prepared));
            }
            else
            {
                entries.Add(new Entry(longhand, null, new RefusedEntry(CssErrorCodes.Unsupported.Value, NotReadable(prepared, parsed.Problem)), prepared));
            }

            return;
        }

        var expanded = shorthand!.Parse(new ValueReader(declaration.Value));
        if (expanded.Values is { } longhandValues)
        {
            foreach (var target in targets)
            {
                var value = longhandValues.TryGetValue(target.Name, out var given) ? given : new CssKeyword("initial");
                entries.Add(new Entry(target, null, value is CssKeyword { Name: "initial" } ? new WideKeywordEntry("initial") : new SpecifiedEntry(value), prepared));
            }
        }
        else if (expanded.Unsupported)
        {
            AddAll(new RefusedEntry(expanded.Code ?? CssErrorCodes.Unsupported.Value, expanded.Problem!));
        }
        else
        {
            AddAll(new RefusedEntry(CssErrorCodes.Unsupported.Value, NotReadable(prepared, expanded.Problem)));
        }
    }

    /// <summary>The one answer for a value the engine's grammar does not read (above CSS Syntax 3): the engine cannot
    /// tell CSS a browser drops from valid CSS beyond this profile (docs/html-css-profile.md), so it refuses both, in the
    /// author's words.</summary>
    /// <param name="declaration">The declaration.</param>
    /// <param name="problem">What the grammar found, or null.</param>
    /// <returns>The message, without the consequence.</returns>
    public static string NotReadable(PreparedDeclaration declaration, string? problem) =>
        $"'{CssSerializer.Shortened(declaration.Source.Name)}: {CssSerializer.Shortened(Trim(declaration.Source.Value))}' is not valid or not supported in this profile"
        + (problem is null ? string.Empty : $" ({problem})");

    /// <summary>A diagnostic about a declaration: at its place, with its property and value.</summary>
    public static Error Diagnostic(ErrorCode code, string message, PreparedDeclaration declaration) =>
        CssDiagnostics.At(code, message, declaration.SourceName, declaration.Source.Position)
            .With(ErrorMetadataKeys.Property, CssSerializer.Shortened(declaration.Name))
            .With(ErrorMetadataKeys.Value, CssSerializer.Shortened(Trim(declaration.Source.Value)));

    /// <summary>The CSS-wide keyword the whole value is, or null (CSS Cascade 5 §7.3; white space around it does not
    /// count).</summary>
    public static string? WideKeyword(IReadOnlyList<ComponentValue> values)
    {
        string? keyword = null;
        foreach (var value in values)
        {
            switch (value)
            {
                case CssToken { Kind: CssTokenKind.Whitespace }:
                    continue;
                case CssToken { Kind: CssTokenKind.Ident } ident when keyword is null && WideKeywords.Contains(InfraAscii.ToLower(ident.Text)):
                    keyword = InfraAscii.ToLower(ident.Text);
                    continue;
                default:
                    return null;
            }
        }

        return keyword;
    }

    /// <summary>The values without white space at the start and the end.</summary>
    public static ValueList<ComponentValue> Trim(IReadOnlyList<ComponentValue> values)
    {
        var start = 0;
        var end = values.Count;
        while (start < end && values[start] is CssToken { Kind: CssTokenKind.Whitespace })
        {
            start++;
        }

        while (end > start && values[end - 1] is CssToken { Kind: CssTokenKind.Whitespace })
        {
            end--;
        }

        var trimmed = new List<ComponentValue>(end - start);
        for (var i = start; i < end; i++)
        {
            trimmed.Add(values[i]);
        }

        return [.. trimmed];
    }

    /// <summary>Whether a function of this name is anywhere in the values, inside blocks and functions too.</summary>
    public static bool ContainsFunction(IEnumerable<ComponentValue> values, string name)
    {
        foreach (var value in values)
        {
            switch (value)
            {
                case FunctionValue function when function.IsNamed(name):
                    return true;
                case FunctionValue function when ContainsFunction(function.Values, name):
                    return true;
                case SimpleBlock block when ContainsFunction(block.Values, name):
                    return true;
            }
        }

        return false;
    }

    /// <summary>Why a math function in the values is refused (one the engine does not evaluate, typed arithmetic), or
    /// null: checked for every property at once, so a value Chromium computes is never dropped as invalid.</summary>
    private static string? UnsupportedMath(IEnumerable<ComponentValue> values)
    {
        foreach (var value in values)
        {
            switch (value)
            {
                case FunctionValue function when Calc.IsMathFunction(function):
                    var node = Calc.Parse(function, out var problem);
                    if (node is null && problem is not null && problem.StartsWith("unsupported:", StringComparison.Ordinal))
                    {
                        return problem["unsupported:".Length..];
                    }

                    if (node is not null && Calc.TypeOf(node) == CalcType.TypedArithmetic)
                    {
                        return $"{function.Name}() multiplies or divides dimensions (typed arithmetic), which is not supported";
                    }

                    break;
                case FunctionValue function when UnsupportedMath(function.Values) is { } inner:
                    return inner;
                case SimpleBlock block when UnsupportedMath(block.Values) is { } inner:
                    return inner;
            }
        }

        return null;
    }

    /// <summary>Whether a '!' delimiter is inside a function or block of the values (a top-level one is checked apart).</summary>
    internal static bool ContainsNestedBang(IReadOnlyList<ComponentValue> values)
    {
        var pending = new Stack<IReadOnlyList<ComponentValue>>();
        foreach (var value in values)
        {
            switch (value)
            {
                case FunctionValue function:
                    pending.Push(function.Values);
                    break;
                case SimpleBlock block:
                    pending.Push(block.Values);
                    break;
            }
        }

        while (pending.Count > 0)
        {
            foreach (var value in pending.Pop())
            {
                switch (value)
                {
                    case CssToken token when token.IsDelim('!'):
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

    /// <summary>Whether an identifier or function name starting with -internal- is anywhere in the values.</summary>
    internal static bool ContainsInternalKeyword(IEnumerable<ComponentValue> values)
    {
        foreach (var value in values)
        {
            switch (value)
            {
                case CssToken { Kind: CssTokenKind.Ident } ident when InfraAscii.StartsWithIgnoreCase(ident.Text, "-internal-"):
                    return true;
                case FunctionValue function when InfraAscii.StartsWithIgnoreCase(function.Name, "-internal-") || ContainsInternalKeyword(function.Values):
                    return true;
                case SimpleBlock block when ContainsInternalKeyword(block.Values):
                    return true;
            }
        }

        return false;
    }
}
