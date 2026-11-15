using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Ave.Documents.Fonts.OpenType;

/// <summary>
/// Reads big-endian values from one OpenType layout table, checking every read against the table's end.
/// </summary>
/// <remarks>
/// <para>The layout tables are graphs of 16-bit offsets ("OpenType Layout Common Table Formats"): checking each
/// offset before following it would bury the parsers in bounds checks. Instead a read past the end returns 0 and
/// remembers the first offending offset in <see cref="FirstOutOfRange"/>; the parser of the whole table turns that
/// into a <c>Documents.Font.Parse.Truncated</c> error once it is done. A value the specification does not allow
/// (an unknown format number, unordered ranges) is recorded the same way, in <see cref="FirstInvalid"/>. So a
/// malformed font gives an error value, never an exception, and the parsers read like the specification's record
/// layouts.</para>
/// <para>Two rules keep malformed data from making the parse slow. Once a problem is recorded the table is known to be
/// broken, so <see cref="Count"/> returns 0 and <see cref="Fits"/> false from then on, and no further array is walked.
/// And the parse has a work budget proportional to the table's size — the approach of HarfBuzz's sanitizer, which
/// allows eight operations per byte: offsets that stay inside the table can still point at each other in ways
/// that multiply the work, which no well-formed table needs. Work is charged per value read, so a range record is
/// one record however many glyphs it covers, and a shared coverage or class table is read once
/// (<see cref="Shared{T}"/>).</para>
/// <para>Offsets are added in 64-bit arithmetic wherever a 32-bit value takes part, so no sum can wrap around, and
/// the parsed form of a table is never larger than the records the table holds: a record that takes no bytes (a
/// value record with valueFormat 0) is not stored.</para>
/// <para>The reader holds the state of ONE table parse; it is created for it and never shared.</para>
/// </remarks>
internal sealed class TableReader(ReadOnlyMemory<byte> table)
{
    private long _budget = Math.Max(16_384, 8L * table.Length);

    private readonly Dictionary<(Type Kind, int Offset), object> _shared = [];

    /// <summary>The first offset that was read beyond the end of the table, or null when every read was in range.</summary>
    public int? FirstOutOfRange { get; private set; }

    /// <summary>The first value the specification does not allow, described, or null.</summary>
    public string? FirstInvalid { get; private set; }

    /// <summary>The table's length in bytes.</summary>
    public int Length => table.Length;

    private bool HasProblem => FirstOutOfRange is not null || FirstInvalid is not null;

    /// <summary>Records a value the specification does not allow; the first one is kept.</summary>
    /// <param name="description">What is wrong, for example "coverage format 3".</param>
    public void Invalid(string description) => FirstInvalid ??= description;

    /// <summary>
    /// Takes <paramref name="work"/> units from the budget (one read is one unit). Returns false, and records the table
    /// as malformed, when the budget is used up.
    /// </summary>
    /// <param name="work">The units of work about to be done.</param>
    /// <returns>Whether the work may be done.</returns>
    public bool Spend(long work)
    {
        _budget -= work;
        if (_budget >= 0)
        {
            return !HasProblem;
        }

        Invalid("the table needs far more work than its size allows, so its offsets or counts are malformed");
        return false;
    }

    /// <summary>
    /// A sub-table that several subtables may point at — a coverage table or a class definition: compiled fonts share
    /// identical ones widely — read once per table parse and then reused, so sharing costs no work from the budget.
    /// </summary>
    /// <typeparam name="T">The sub-table type.</typeparam>
    /// <param name="offset">Where it starts in the table.</param>
    /// <param name="read">Reads it.</param>
    /// <returns>The sub-table.</returns>
    public T Shared<T>(int offset, Func<T> read)
        where T : notnull
    {
        if (_shared.TryGetValue((typeof(T), offset), out var known))
        {
            return (T)known;
        }

        var value = read();
        _shared[(typeof(T), offset)] = value;
        return value;
    }

    /// <summary>Reads a uint16 at <paramref name="offset"/>.</summary>
    public ushort UInt16(int offset) =>
        InRange(offset, 2) ? BinaryPrimitives.ReadUInt16BigEndian(table.Span[offset..]) : (ushort)0;

    /// <summary>Reads an int16 (also FWORD) at <paramref name="offset"/>.</summary>
    public short Int16(int offset) =>
        InRange(offset, 2) ? BinaryPrimitives.ReadInt16BigEndian(table.Span[offset..]) : (short)0;

    /// <summary>Reads a uint32 (also Offset32) at <paramref name="offset"/>.</summary>
    public uint UInt32(int offset) =>
        InRange(offset, 4) ? BinaryPrimitives.ReadUInt32BigEndian(table.Span[offset..]) : 0u;

    /// <summary>Reads a Tag (four ASCII characters) at <paramref name="offset"/>.</summary>
    public string Tag(int offset) =>
        InRange(offset, 4) ? System.Text.Encoding.ASCII.GetString(table.Span.Slice(offset, 4)) : "    ";

    /// <summary>
    /// Reads the uint16 count of an array of records and checks that the whole array lies inside the table. A
    /// count whose records would run past the end is recorded as out of range and read as 0, so a malformed count
    /// can never make a parser walk records that are not there. After any problem the count is 0.
    /// </summary>
    /// <param name="countOffset">Where the count is.</param>
    /// <param name="arrayOffset">Where the first record starts.</param>
    /// <param name="recordLength">The length of one record in bytes.</param>
    /// <returns>The count, or 0 when the array does not fit or the table is already known to be malformed.</returns>
    public int Count(int countOffset, int arrayOffset, int recordLength)
    {
        var count = UInt16(countOffset);
        return Fits(arrayOffset, (long)count * recordLength) ? count : 0;
    }

    /// <summary>True when <paramref name="length"/> bytes from <paramref name="offset"/> lie inside the table (and no
    /// problem was found yet); otherwise the overrun is recorded as out of range.</summary>
    /// <param name="offset">The start.</param>
    /// <param name="length">The number of bytes.</param>
    /// <returns>Whether they fit.</returns>
    public bool Fits(int offset, long length)
    {
        if (HasProblem)
        {
            return false;
        }

        if (offset >= 0 && length >= 0 && (long)offset + length <= table.Length)
        {
            return true;
        }

        FirstOutOfRange ??= (int)Math.Clamp((long)offset + length, 0, int.MaxValue);
        return false;
    }

    /// <summary>A read of <paramref name="size"/> bytes at <paramref name="offset"/>; the sum is taken in 64 bits, so an
    /// offset near the end of the 32-bit range cannot wrap around into the table.</summary>
    private bool InRange(int offset, int size)
    {
        if (offset >= 0 && (long)offset + size <= table.Length && Spend(1))
        {
            return true;
        }

        if (!HasProblem)
        {
            FirstOutOfRange = offset;
        }

        return false;
    }
}
