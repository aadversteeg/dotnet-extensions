using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Ave.Extensions.Documents.Fonts.Tables;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Fonts.OpenType;

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
/// And the parse has a work budget proportional to the table's size (<see cref="WorkPerByte"/>,
/// <see cref="MinimumWork"/>), as HarfBuzz's sanitizer has: offsets that stay inside the table can still point at each
/// other in ways that multiply the work, which no well-formed table needs. Work is charged per value read AND per record
/// created (<see cref="Records"/>, which <see cref="Count"/> calls): some records read nothing — a value record whose
/// fields are all device offsets, which are skipped — and subtables that share one array let a few bytes stand for many
/// such records, so only charging the records bounds the parsed form by the budget. A range record is one record
/// however many glyphs it covers, and a shared coverage or class table is read once (<see cref="Shared{T}"/>); a parser
/// that walks parsed data again charges that walk with <see cref="Spend"/>.</para>
/// <para>Offsets are added in 64-bit arithmetic wherever a 32-bit value takes part, so no sum can wrap around, and
/// the parsed form of a table is never larger than the records the table holds: a record that takes no bytes (a
/// value record with valueFormat 0) is not stored.</para>
/// <para>The reader holds the state of ONE table parse; it is created for it and never shared.</para>
/// </remarks>
internal sealed class TableReader(ReadOnlyMemory<byte> table)
{
    /// <summary>Units of work the parse may spend per byte of the table. A well-formed table reads each value about
    /// once; eight per byte leaves room for shared sub-tables and still bounds the parse by the table's size.</summary>
    public const int WorkPerByte = 8;

    /// <summary>The smallest budget, so that a tiny table is not refused for a constant amount of work.</summary>
    public const int MinimumWork = 16_384;

    private long _budget = Math.Max(MinimumWork, (long)WorkPerByte * table.Length);

    private readonly Dictionary<(Type Kind, int Offset), object> _shared = [];

    /// <summary>The first offset that was read beyond the end of the table, or null when every read was in range.</summary>
    public int? FirstOutOfRange { get; private set; }

    /// <summary>The first value the specification does not allow, described, or null.</summary>
    public string? FirstInvalid { get; private set; }

    /// <summary>The table's length in bytes.</summary>
    public int Length => table.Length;

    /// <summary>True once a read went past the end or a value was invalid: the table is known to be malformed.</summary>
    public bool HasProblem => FirstOutOfRange is not null || FirstInvalid is not null;

    /// <summary>
    /// The outcome of the whole parse: <paramref name="value"/> when every read was in range and every value valid;
    /// otherwise the first problem, <c>Documents.Font.Parse.Truncated</c> before <c>InvalidValue</c>. Every parser
    /// that uses a reader ends with this call, so no recorded problem can be ignored.
    /// </summary>
    /// <typeparam name="T">The parsed value.</typeparam>
    /// <param name="tag">The table tag, for the error.</param>
    /// <param name="value">What the parser built.</param>
    /// <returns>The value or the error.</returns>
    public Result<T, Error> Finish<T>(string tag, T value) =>
        FirstOutOfRange is { } offset ? FontParseErrors.Truncated<T>(tag, offset, Length)
        : FirstInvalid is { } problem ? FontParseErrors.InvalidValue<T>(tag, problem, problem)
        : Result<T, Error>.Success(value);

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
    /// Charges for <paramref name="count"/> records the parser is about to create, one unit each, on top of the values
    /// they read (see the remarks of this class). Returns false, and the parser creates none, when the budget is used up
    /// or a problem is known.
    /// </summary>
    /// <param name="count">The number of records.</param>
    /// <returns>Whether they may be created.</returns>
    public bool Records(long count) => count == 0 ? !HasProblem : Spend(count);

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
    /// Reads the uint16 count of an array of records, checks that the whole array lies inside the table, and charges
    /// the records the caller is about to create (<see cref="Records"/>). A count whose records would run past the end
    /// is recorded as out of range and read as 0, so a malformed count can never make a parser walk records that are not
    /// there. After any problem the count is 0.
    /// </summary>
    /// <param name="countOffset">Where the count is.</param>
    /// <param name="arrayOffset">Where the first record starts.</param>
    /// <param name="recordLength">The length of one record in bytes.</param>
    /// <returns>The count, or 0 when the array does not fit, the budget is used up or the table is already known to be
    /// malformed.</returns>
    public int Count(int countOffset, int arrayOffset, int recordLength)
    {
        var count = UInt16(countOffset);
        return Fits(arrayOffset, (long)count * recordLength) && Records(count) ? count : 0;
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
