using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Pdf;

/// <summary>Writes content-stream instructions as the bytes of a content stream (ISO 32000-1 §7.8.2).</summary>
public static class ContentStream
{
    /// <summary>
    /// Writes each operation on its own line: operands separated by spaces, then the operator. Fails when an
    /// operand holds a number that is not finite.
    /// </summary>
    /// <param name="operations">The operations, in drawing order.</param>
    /// <returns>The stream bytes (ASCII), or a <c>Documents.Pdf.Write.InvalidNumber</c> error.</returns>
    public static Result<ImmutableArray<byte>, Error> Serialize(IEnumerable<ContentOperation> operations)
    {
        var all = operations.ToImmutableArray();
        var invalid = all.SelectMany(operation => operation.Operands)
            .Select(PdfSerializer.FindNonFinite)
            .FirstOrDefault(found => found is not null);
        if (invalid is { } value)
        {
            return Result<ImmutableArray<byte>, Error>.Failure(PdfSerializer.InvalidNumber(value));
        }

        var text = string.Concat(all.Select(WriteLine));
        return Result<ImmutableArray<byte>, Error>.Success([.. Encoding.ASCII.GetBytes(text)]);
    }

    private static string WriteLine(ContentOperation operation) =>
        string.Concat(operation.Operands.Select(operand => PdfSerializer.Write(operand) + " ")) + operation.Operator + "\n";
}
