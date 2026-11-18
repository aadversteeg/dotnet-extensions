using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.Css.Syntax;

/// <summary>
/// A declaration (CSS Syntax 3 §5.2) as written: a name, a value of component values, and the important flag. The value
/// stays raw until the cascade, because <c>var()</c> is substituted at computed-value time (CSS Variables 1 §3).
/// </summary>
/// <param name="Name">The property name as written (custom properties keep their case; others are compared ASCII
/// case-insensitively).</param>
/// <param name="Value">The value, without the <c>!important</c> and without white space at the end.</param>
/// <param name="Important">Whether it ended in <c>!important</c>.</param>
/// <param name="Position">Where its name starts.</param>
public sealed record Declaration(string Name, ValueList<ComponentValue> Value, bool Important, TextPosition Position)
{
    /// <summary>For a custom property (a name starting with <c>--</c>): the value's text as written (CSS Syntax 3 §5.5.6,
    /// "original text"); null for every other property.</summary>
    public string? OriginalText { get; init; }

    /// <summary>Whether this declares a custom property (CSS Variables 1 §2).</summary>
    public bool IsCustomProperty => Name.StartsWith("--", System.StringComparison.Ordinal);
}
