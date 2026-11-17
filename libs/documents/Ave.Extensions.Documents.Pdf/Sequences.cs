using System.Collections.Generic;
using System.Collections.Immutable;

namespace Ave.Extensions.Documents.Pdf;

/// <summary>How the public API reads a sequence it is given.</summary>
internal static class Sequences
{
    /// <summary>
    /// The sequence, or an empty one for null and for an unset <see cref="ImmutableArray{T}"/>: boxed, an unset array is
    /// not null, but reading it throws. The API reads both as "nothing given".
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="source">The sequence, or null.</param>
    /// <returns>A sequence that can be read.</returns>
    public static IEnumerable<T> OrEmpty<T>(this IEnumerable<T>? source) =>
        source is null or ImmutableArray<T> { IsDefault: true } ? [] : source;
}
