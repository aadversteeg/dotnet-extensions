namespace Ave.Extensions.Documents.Typesetting.LineBreaking;

/// <summary>Which rules find the places where a line may end: the Unicode Line Breaking Algorithm as published, or the
/// same with the departures Chromium's print shows.</summary>
public enum LineBreakTailoring
{
    /// <summary>UAX #14 with its default rules and no tailoring (<see cref="UnicodeLineBreaking"/>).</summary>
    None,

    /// <summary>UAX #14 with the departures of Chromium's line breaker (<see cref="ChromiumLineBreaking"/>).</summary>
    Chromium,
}
