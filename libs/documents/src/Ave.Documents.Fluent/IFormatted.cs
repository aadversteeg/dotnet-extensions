namespace Ave.Documents.Fluent;

/// <summary>
/// Something the text modifiers (<see cref="TextModifiers"/>) apply to: it carries its own <see cref="Style"/>
/// and returns a copy with another one. Implemented by every element and by <see cref="Style"/> itself; authors
/// never call these members, they call the modifiers.
/// </summary>
/// <typeparam name="TSelf">The implementing type, so a modifier returns the same type it was called on.</typeparam>
public interface IFormatted<TSelf>
    where TSelf : IFormatted<TSelf>
{
    /// <summary>The values set on this element by modifiers.</summary>
    Style Format { get; }

    /// <summary>A copy with other modifier values.</summary>
    /// <param name="format">The new values.</param>
    /// <returns>The copy.</returns>
    TSelf WithFormat(Style format);
}

/// <summary>
/// Something the box modifiers (<see cref="BoxModifiers"/>) apply to as well: a block, a paragraph, or a style.
/// A text span is not one — a span has no box of its own in this front-end.
/// </summary>
/// <typeparam name="TSelf">The implementing type.</typeparam>
public interface IBoxFormatted<TSelf> : IFormatted<TSelf>
    where TSelf : IBoxFormatted<TSelf>;
