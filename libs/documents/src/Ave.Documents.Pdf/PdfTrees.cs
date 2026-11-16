using System;
using System.Collections.Generic;
using Ave.Documents.Core;
using System.Linq;

namespace Ave.Documents.Pdf;

/// <summary>
/// Name trees and number trees (ISO 32000-1 §7.9.6 and §7.9.7) written as a single root node that holds all entries.
/// The sections allow it ("The tree shall always have exactly one root node, which shall contain a single entry:
/// either Kids or Names but not both", resp. Nums), and a document has few
/// entries; a balanced tree of intermediate nodes only pays off for thousands.
/// </summary>
public static class PdfTrees
{
    /// <summary>
    /// A name tree: <c>&lt;&lt; /Names [key1 value1 key2 value2 …] &gt;&gt;</c>. §7.9.6: "the keys shall be sorted in
    /// lexical order", which for byte strings is the order of their bytes.
    /// </summary>
    /// <param name="entries">The keys and values; keys unique. Null and an unset array are read as no entries; a missing key or value is
    /// kept, and refused when the tree is written (<see cref="PdfSerializer.Problem"/>).</param>
    /// <returns>The root node.</returns>
    public static PdfDictionary NameTree(IEnumerable<(PdfString Key, PdfObject Value)>? entries) =>
        PdfDictionary.Of(("Names", new PdfArray([.. entries.OrEmpty()
            .OrderBy(entry => entry.Key?.Bytes ?? default, ByteOrder.Instance)
            .SelectMany(entry => new[] { entry.Key, entry.Value })])));

    /// <summary>A number tree: <c>&lt;&lt; /Nums [0 value0 1 value1 …] &gt;&gt;</c>, keys in ascending order (§7.9.7).</summary>
    /// <param name="entries">The keys and values; keys unique. Null and an unset array are read as no entries.</param>
    /// <returns>The root node.</returns>
    public static PdfDictionary NumberTree(IEnumerable<(int Key, PdfObject Value)>? entries) =>
        PdfDictionary.Of(("Nums", new PdfArray([.. entries.OrEmpty()
            .OrderBy(entry => entry.Key)
            .SelectMany(entry => new[] { new PdfInteger(entry.Key), entry.Value })])));

    /// <summary>Byte strings in the order of their bytes, the shorter first when one is a prefix of the other.</summary>
    private sealed class ByteOrder : IComparer<ValueList<byte>>
    {
        public static ByteOrder Instance { get; } = new();

        public int Compare(ValueList<byte> left, ValueList<byte> right) =>
            left.ToArray().AsSpan().SequenceCompareTo(right.ToArray());
    }
}
