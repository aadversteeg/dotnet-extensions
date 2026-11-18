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
    public bool Equals(PreparedDeclaration? other) => ReferenceEquals(this, other);

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

/// <summary>One property set by one declaration: a longhand, or a custom property by name.</summary>
internal readonly record struct Entry(PropertyDefinition? Longhand, string? Custom, EntryValue Value, PreparedDeclaration Declaration);

/// <summary>A rule's declarations, prepared once: the entries, and the diagnostics reported once when the rule first
/// applies to an element (properties the engine ignores by rule, or does not know).</summary>
internal sealed record PreparedBlock(ValueList<Entry> Entries, ValueList<Error> WhenApplied)
{
    public static PreparedBlock Empty { get; } = new([], []);

    public bool Equals(PreparedBlock? other) => ReferenceEquals(this, other);

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

    public static PreparedBlock Prepare(IEnumerable<Declaration> declarations, string sourceName, CascadeOrigin origin, ref int order, List<Error> diagnostics)
    {
        var entries = new List<Entry>();
        var whenApplied = new List<Error>();
        foreach (var declaration in declarations)
        {
            Prepare(declaration, sourceName, origin, order++, entries, whenApplied, diagnostics);
        }

        return entries.Count == 0 && whenApplied.Count == 0 ? PreparedBlock.Empty : new PreparedBlock([.. entries], [.. whenApplied]);
    }

    private static void Prepare(Declaration declaration, string sourceName, CascadeOrigin origin, int order, List<Entry> entries, List<Error> whenApplied, List<Error> diagnostics)
    {
        var name = declaration.IsCustomProperty ? declaration.Name : Ascii.ToLower(declaration.Name);
        var prepared = new PreparedDeclaration(declaration, sourceName, name, declaration.Important && origin != CascadeOrigin.PresentationalHint, order);
        var wide = WideKeyword(declaration.Value);
        if (ContainsFunction(declaration.Value, "var") && !VarSubstitution.IsWellFormed(declaration.Value))
        {
            diagnostics.Add(Diagnostic(CssErrorCodes.Invalid.Value, $"A var() in '{declaration.Name}' does not match var( --name [, fallback]? ); the declaration is dropped.", prepared));
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
                whenApplied.Add(Diagnostic(CssErrorCodes.Ignored.Property, $"'{declaration.Name}' is ignored: it concerns {ignored.Reason}.", prepared));
            }
            else if (name.StartsWith("-moz-", StringComparison.Ordinal) || name.StartsWith("-ms-", StringComparison.Ordinal) || name.StartsWith("-o-", StringComparison.Ordinal))
            {
                diagnostics.Add(Diagnostic(CssErrorCodes.Invalid.Property, $"'{declaration.Name}' is a property of another browser engine; the declaration is dropped.", prepared));
            }
            else
            {
                whenApplied.Add(Diagnostic(CssErrorCodes.Unsupported.Property, $"The property '{declaration.Name}' is not supported.", prepared));
            }

            return;
        }

        IReadOnlyList<PropertyDefinition> targets = longhand is not null
            ? [longhand]
            : isAll
                ? [.. PropertyRegistry.Longhands.Where(property => property.Name is not ("direction" or "unicode-bidi"))]
                : [.. shorthand!.Longhands.Select(longhandName => PropertyRegistry.Find(longhandName)!)];

        void AddAll(EntryValue value)
        {
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
            diagnostics.Add(Diagnostic(CssErrorCodes.Invalid.Value, "'all' takes only a CSS-wide keyword; the declaration is dropped.", prepared));
            return;
        }

        if (UnsupportedSubstitutions.FirstOrDefault(function => ContainsFunction(declaration.Value, function)) is { } substitution)
        {
            AddAll(new RefusedEntry(CssErrorCodes.Unsupported.Value, $"{substitution}() is not supported."));
            return;
        }

        if (origin != CascadeOrigin.UserAgent && ContainsInternalKeyword(declaration.Value))
        {
            diagnostics.Add(Diagnostic(CssErrorCodes.Invalid.Value, $"A value starting with -internal- is for the user agent only; the declaration of '{declaration.Name}' is dropped.", prepared));
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
                diagnostics.Add(Diagnostic(CssErrorCodes.Invalid.Value, $"The value of '{declaration.Name}' is invalid ({parsed.Problem}); the declaration is dropped.", prepared));
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
            diagnostics.Add(Diagnostic(CssErrorCodes.Invalid.Value, $"The value of '{declaration.Name}' is invalid ({expanded.Problem}); the declaration is dropped.", prepared));
        }
    }

    /// <summary>A diagnostic about a declaration: at its place, with its property and value.</summary>
    public static Error Diagnostic(ErrorCode code, string message, PreparedDeclaration declaration) =>
        CssDiagnostics.At(code, message, declaration.SourceName, declaration.Source.Position)
            .With(ErrorMetadataKeys.Property, declaration.Name)
            .With(ErrorMetadataKeys.Value, CssSerializer.Serialize(Trim(declaration.Source.Value)));

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
                case CssToken { Kind: CssTokenKind.Ident } ident when keyword is null && WideKeywords.Contains(Ascii.ToLower(ident.Text)):
                    keyword = Ascii.ToLower(ident.Text);
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

    private static bool ContainsInternalKeyword(IEnumerable<ComponentValue> values)
    {
        foreach (var value in values)
        {
            switch (value)
            {
                case CssToken { Kind: CssTokenKind.Ident } ident when Ascii.StartsWithIgnoreCase(ident.Text, "-internal-"):
                    return true;
                case FunctionValue function when Ascii.StartsWithIgnoreCase(function.Name, "-internal-") || ContainsInternalKeyword(function.Values):
                    return true;
                case SimpleBlock block when ContainsInternalKeyword(block.Values):
                    return true;
            }
        }

        return false;
    }
}
