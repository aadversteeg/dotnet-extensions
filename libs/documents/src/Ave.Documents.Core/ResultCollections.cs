using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Core;

/// <summary>
/// The two helpers Ave.Extensions.Functional and Ave.Extensions.ErrorPaths do not have yet: run a
/// <see cref="Result{T,E}"/> function over a list, and collect several errors into one.
/// </summary>
/// <remarks>
/// TEMPORARY HOME — moves to Ave.Extensions.Functional (<c>TraverseAll</c>) and Ave.Extensions.ErrorPaths
/// (<c>Combine</c>) once Aad approves the API proposal in the stage-0 report. Keep it the only such helper in
/// this repository so the move is one change.
/// </remarks>
public static class ResultCollections
{
    /// <summary>
    /// Applies <paramref name="map"/> to every item. Succeeds with all values, in input order, when every call
    /// succeeds. Otherwise fails with ALL the errors (see <see cref="Combine"/>), not only the first, so that a
    /// caller validating a document hears about every problem at once.
    /// </summary>
    /// <typeparam name="TIn">The item type.</typeparam>
    /// <typeparam name="TOut">The value type of each successful result.</typeparam>
    /// <param name="items">The items.</param>
    /// <param name="map">The function to apply; must not have side effects, because every item is visited
    /// even after a failure.</param>
    /// <returns>The values, or the combined error.</returns>
    public static Result<ImmutableArray<TOut>, Error> TraverseAll<TIn, TOut>(
        this IEnumerable<TIn> items, Func<TIn, Result<TOut, Error>> map)
    {
        var results = items.Select(map).ToImmutableArray();
        var errors = results.Where(result => result.IsFailure).Select(result => result.Error).ToImmutableArray();

        return errors.IsEmpty
            ? Result<ImmutableArray<TOut>, Error>.Success(results.Select(result => result.Value).ToImmutableArray())
            : Result<ImmutableArray<TOut>, Error>.Failure(Combine(errors));
    }

    /// <summary>
    /// Collects one or more errors into one error. A single error is returned unchanged, so its own code stays
    /// visible to the caller. Several errors become one <see cref="DocumentErrorCodes.Multiple"/> error whose
    /// message lists every message and whose metadata <see cref="ErrorMetadataKeys.Errors"/> holds the errors.
    /// A nested Multiple error is flattened into its parts so the list is always one level deep.
    /// </summary>
    /// <param name="errors">The errors; at least one.</param>
    /// <returns>The combined error.</returns>
    /// <exception cref="ArgumentException">No errors were given; "combine nothing into an error" is a bug in
    /// the caller.</exception>
    public static Error Combine(IReadOnlyList<Error> errors)
    {
        var flat = errors.SelectMany(Parts).ToImmutableArray();
        if (flat.IsEmpty)
        {
            throw new ArgumentException("At least one error is needed.", nameof(errors));
        }

        if (flat.Length == 1)
        {
            return flat[0];
        }

        var message = $"{flat.Length} errors: " + string.Join("; ", flat.Select(error => error.Message));
        return new Error(DocumentErrorCodes.Multiple, message)
            .With(ErrorMetadataKeys.Errors, (IReadOnlyList<Error>)flat);
    }

    /// <summary>The parts of an error: the errors inside a Multiple error, or the error itself.</summary>
    /// <param name="error">The error.</param>
    /// <returns>Its parts.</returns>
    public static IReadOnlyList<Error> Parts(Error error) =>
        error.Code == DocumentErrorCodes.Multiple
        && error.Metadata is { } metadata
        && metadata.TryGetValue(ErrorMetadataKeys.Errors, out var inner)
        && inner is IReadOnlyList<Error> parts
            ? parts
            : [error];
}
