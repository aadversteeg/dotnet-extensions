using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.Fluent;

/// <summary>
/// A list: items one below the other, each with a bullet or a number outside its content, at the left of the list's
/// padding. Made by <see cref="Elements.BulletedList"/> and <see cref="Elements.NumberedList"/>. A list is indented by a
/// left padding of 30 pt (40 CSS pixels, as HTML's user-agent style sheet indents <c>ul</c> and <c>ol</c>) unless a
/// padding is set; a list inside an item nests one level deeper.
/// </summary>
/// <param name="Items">The items, top to bottom; at least one.</param>
/// <param name="IsNumbered">True for a numbered list, false for a bulleted one.</param>
/// <param name="Format">What the modifiers set on the list.</param>
/// <param name="Style">The shared style applied with <see cref="With"/>.</param>
/// <param name="Source">Where the element was created.</param>
public sealed record ListElement(ValueList<ListItemElement> Items, bool IsNumbered, Style Format, Style Style, SourceLocation Source)
    : Element(Source), IBoxFormatted<ListElement>
{
    /// <summary>The bullet of a bulleted list, or null for the bullet of its level: a disc, inside one list a circle, deeper
    /// a square (as HTML's user-agent style sheet gives <c>ul</c>).</summary>
    public ListBullet? BulletShape { get; init; }

    /// <summary>The number of a numbered list's first item, or null for 1.</summary>
    public int? StartNumber { get; init; }

    /// <summary>Sets the bullet of a bulleted list; <see cref="ListBullet.None"/> gives its items no label.</summary>
    /// <param name="bullet">The bullet.</param>
    /// <returns>A copy with the bullet.</returns>
    public ListElement Bullet(ListBullet bullet) => this with { BulletShape = bullet };

    /// <summary>Sets the number of a numbered list's first item; the items after it count up by one.</summary>
    /// <param name="number">The first number; zero and negative numbers are allowed.</param>
    /// <returns>A copy with the start number.</returns>
    public ListElement Start(int number) => this with { StartNumber = number };

    /// <summary>Applies a shared style. The list's own modifier values still win over it.</summary>
    /// <param name="style">The style; replaces a style applied before.</param>
    /// <returns>A copy with the style.</returns>
    public ListElement With(Style style) => this with { Style = style };

    /// <inheritdoc/>
    ListElement IFormatted<ListElement>.WithFormat(Style format) => this with { Format = format };
}

/// <summary>
/// One item of a list: its content, with the list's bullet or the item's number beside its first line. Made by
/// <see cref="Elements.ListItem(ValueList{Element}, string, int)"/>; an item is only ever an item of a list. Its text
/// formatting is inherited by its content and gives its label's font, size and colour.
/// </summary>
/// <param name="Content">The elements of the item, top to bottom.</param>
/// <param name="Format">What the modifiers set on the item.</param>
/// <param name="Style">The shared style applied with <see cref="With"/>.</param>
/// <param name="Source">Where the item was created.</param>
public sealed record ListItemElement(ValueList<Element> Content, Style Format, Style Style, SourceLocation Source)
    : IBoxFormatted<ListItemElement>
{
    /// <summary>Applies a shared style. The item's own modifier values still win over it.</summary>
    /// <param name="style">The style; replaces a style applied before.</param>
    /// <returns>A copy with the style.</returns>
    public ListItemElement With(Style style) => this with { Style = style };

    /// <inheritdoc/>
    ListItemElement IFormatted<ListItemElement>.WithFormat(Style format) => this with { Format = format };
}

/// <summary>The bullets of a bulleted list (<see cref="ListElement.Bullet"/>), drawn as shapes sized from the item's font.</summary>
public enum ListBullet
{
    /// <summary>A filled circle.</summary>
    Disc,

    /// <summary>An open circle.</summary>
    Circle,

    /// <summary>A filled square.</summary>
    Square,

    /// <summary>No bullet: the items have no label.</summary>
    None,
}
