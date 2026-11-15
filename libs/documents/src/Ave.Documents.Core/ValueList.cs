using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Ave.Documents.Core;

/// <summary>
/// An immutable list that compares by its CONTENTS: two lists are equal when they have the same items in the
/// same order. The engine's trees (document model, fragments, page model, front-end elements) hold their
/// children in this type, so two trees built from the same input are equal (<c>==</c>), and a record that holds
/// one gets that structural equality for free.
/// </summary>
/// <remarks>
/// <para>Why not <see cref="ImmutableArray{T}"/> directly: it compares by reference to its storage, so two arrays
/// with equal items are not equal, and neither are two records that hold them.</para>
/// <para>Write one with a collection expression, <c>[a, b]</c> or <c>[.. items]</c>. The default value is the
/// empty list. An <see cref="ImmutableArray{T}"/> converts implicitly, without copying.</para>
/// <para>Equality costs a walk over the items (and, for trees, over the whole tree); nothing in layout compares
/// trees in a hot path, so the clearer semantics win (rule: readability over performance).</para>
/// </remarks>
/// <typeparam name="T">The item type; its own <see cref="object.Equals(object)"/> decides item equality.</typeparam>
[CollectionBuilder(typeof(ValueList), nameof(ValueList.Create))]
public readonly struct ValueList<T> : IReadOnlyList<T>, IEquatable<ValueList<T>>
{
    private readonly ImmutableArray<T> _items;

    /// <summary>Wraps an immutable array; the array is not copied.</summary>
    /// <param name="items">The items.</param>
    public ValueList(ImmutableArray<T> items) => _items = items;

    /// <summary>The empty list.</summary>
    public static ValueList<T> Empty => default;

    /// <summary>The items as an immutable array (empty for the default value).</summary>
    public ImmutableArray<T> Items => _items.IsDefault ? [] : _items;

    /// <inheritdoc/>
    public int Count => Items.Length;

    /// <summary>True when the list has no items.</summary>
    public bool IsEmpty => Items.IsEmpty;

    /// <inheritdoc/>
    public T this[int index] => Items[index];

    /// <summary>Wraps an immutable array; the array is not copied.</summary>
    /// <param name="items">The items.</param>
    public static implicit operator ValueList<T>(ImmutableArray<T> items) => new(items);

    /// <summary>True when both lists have equal items in the same order.</summary>
    /// <param name="left">The first list.</param>
    /// <param name="right">The second list.</param>
    /// <returns>Whether they are equal.</returns>
    public static bool operator ==(ValueList<T> left, ValueList<T> right) => left.Equals(right);

    /// <summary>True when the lists differ in length or in any item.</summary>
    /// <param name="left">The first list.</param>
    /// <param name="right">The second list.</param>
    /// <returns>Whether they differ.</returns>
    public static bool operator !=(ValueList<T> left, ValueList<T> right) => !left.Equals(right);

    /// <summary>Returns an enumerator over the items, without allocating.</summary>
    /// <returns>The enumerator.</returns>
    public ImmutableArray<T>.Enumerator GetEnumerator() => Items.GetEnumerator();

    /// <inheritdoc/>
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)Items).GetEnumerator();

    /// <summary>True when both lists have equal items in the same order.</summary>
    /// <param name="other">The other list.</param>
    /// <returns>Whether they are equal.</returns>
    public bool Equals(ValueList<T> other) => Items.SequenceEqual(other.Items, EqualityComparer<T>.Default);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ValueList<T> other && Equals(other);

    /// <summary>A hash of the items, consistent with <see cref="Equals(ValueList{T})"/>.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in Items)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }

    /// <summary>Formats as <c>[a, b, c]</c>.</summary>
    /// <returns>The text form.</returns>
    public override string ToString() => $"[{string.Join(", ", Items)}]";
}

/// <summary>Creates <see cref="ValueList{T}"/> values; also the builder behind collection expressions.</summary>
public static class ValueList
{
    /// <summary>A list with a copy of the given items. Called by the compiler for <c>[a, b]</c>.</summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="items">The items.</param>
    /// <returns>The list.</returns>
    public static ValueList<T> Create<T>(ReadOnlySpan<T> items) => new([.. items]);

    /// <summary>A list with the items of a sequence, in order.</summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="items">The items.</param>
    /// <returns>The list.</returns>
    public static ValueList<T> ToValueList<T>(this IEnumerable<T> items) => new([.. items]);
}
