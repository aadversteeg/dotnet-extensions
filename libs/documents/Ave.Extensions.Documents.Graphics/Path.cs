using System.Collections.Immutable;
using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.Graphics;

/// <summary>
/// A path: subpaths of straight lines and quadratic and cubic Bézier curves, as in PDF (ISO 32000-1 §8.5.2). Each
/// subpath starts with <see cref="MoveTo"/>; filling closes every subpath that is still open (§8.5.3.1: "any open subpaths
/// are implicitly closed before being filled"). Build one with <see cref="PathBuilder"/> (or <see cref="Rectangle"/>), so a
/// path is always well formed.
/// </summary>
public sealed record Path
{
    internal Path(ValueList<PathCommand> commands) => Commands = commands;

    /// <summary>The commands in order: no missing command, the first is a <see cref="MoveTo"/> unless the path is empty,
    /// and every other command follows a current point (the builders' types make sure).</summary>
    public ValueList<PathCommand> Commands { get; }

    /// <summary>The path without subpaths.</summary>
    public static Path Empty { get; } = new(ValueList<PathCommand>.Empty);

    /// <summary>A rectangle as one closed subpath: top-left, top-right, bottom-right, bottom-left.</summary>
    /// <param name="x">The left edge.</param>
    /// <param name="y">The top edge.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns>The path.</returns>
    public static Path Rectangle(double x, double y, double width, double height) =>
        new PathBuilder().MoveTo(x, y).LineTo(x + width, y).LineTo(x + width, y + height).LineTo(x, y + height).Close().ToPath();
}

/// <summary>One command of a <see cref="Path"/>. Only the cases in this assembly exist.</summary>
public abstract record PathCommand
{
    private protected PathCommand()
    {
    }
}

/// <summary>Starts a new subpath at a point.</summary>
/// <param name="X">The x coordinate.</param>
/// <param name="Y">The y coordinate.</param>
public sealed record MoveTo(double X, double Y) : PathCommand;

/// <summary>A straight line from the current point.</summary>
/// <param name="X">The end's x coordinate.</param>
/// <param name="Y">The end's y coordinate.</param>
public sealed record LineTo(double X, double Y) : PathCommand;

/// <summary>A quadratic Bézier curve from the current point, as TrueType outlines are made of.</summary>
/// <param name="ControlX">The control point's x coordinate.</param>
/// <param name="ControlY">The control point's y coordinate.</param>
/// <param name="X">The end's x coordinate.</param>
/// <param name="Y">The end's y coordinate.</param>
public sealed record QuadraticTo(double ControlX, double ControlY, double X, double Y) : PathCommand;

/// <summary>A cubic Bézier curve from the current point (ISO 32000-1 §8.5.2.2).</summary>
/// <param name="Control1X">The first control point's x coordinate.</param>
/// <param name="Control1Y">The first control point's y coordinate.</param>
/// <param name="Control2X">The second control point's x coordinate.</param>
/// <param name="Control2Y">The second control point's y coordinate.</param>
/// <param name="X">The end's x coordinate.</param>
/// <param name="Y">The end's y coordinate.</param>
public sealed record CubicTo(double Control1X, double Control1Y, double Control2X, double Control2Y, double X, double Y) : PathCommand;

/// <summary>Closes the current subpath with a straight line back to its start.</summary>
public sealed record ClosePath : PathCommand
{
    /// <summary>The one close command.</summary>
    public static ClosePath Instance { get; } = new();
}

/// <summary>
/// Builds a <see cref="Path"/> one command at a time. A path starts with <see cref="MoveTo"/>, which gives the current
/// point every other command needs (ISO 32000-1 §8.5.2.1), so this builder offers only that and hands back a
/// <see cref="SubpathBuilder"/> for the rest: a line or curve before the first <see cref="MoveTo"/> cannot be written,
/// and no call to either builder can fail.
/// </summary>
public sealed class PathBuilder
{
    private readonly ImmutableArray<PathCommand>.Builder _commands = ImmutableArray.CreateBuilder<PathCommand>();
    private readonly SubpathBuilder _subpath;

    /// <summary>Creates an empty builder.</summary>
    public PathBuilder() => _subpath = new SubpathBuilder(_commands);

    /// <summary>Starts the first subpath (or another one).</summary>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    /// <returns>The builder of the same path, now with a current point.</returns>
    public SubpathBuilder MoveTo(double x, double y) => _subpath.MoveTo(x, y);

    /// <summary>The path built so far, by this builder and the <see cref="SubpathBuilder"/> it handed out.</summary>
    /// <returns>The path.</returns>
    public Path ToPath() => new(_commands.ToImmutable());
}

/// <summary>
/// Builds a <see cref="Path"/> that has a current point: the one <see cref="PathBuilder.MoveTo"/> handed out, adding to
/// the same path. Every command is allowed here.
/// </summary>
public sealed class SubpathBuilder
{
    private readonly ImmutableArray<PathCommand>.Builder _commands;

    internal SubpathBuilder(ImmutableArray<PathCommand>.Builder commands) => _commands = commands;

    /// <summary>Starts a new subpath.</summary>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    /// <returns>This builder.</returns>
    public SubpathBuilder MoveTo(double x, double y) => Add(new MoveTo(x, y));

    /// <summary>Adds a straight line.</summary>
    /// <param name="x">The end's x coordinate.</param>
    /// <param name="y">The end's y coordinate.</param>
    /// <returns>This builder.</returns>
    public SubpathBuilder LineTo(double x, double y) => Add(new LineTo(x, y));

    /// <summary>Adds a quadratic Bézier curve.</summary>
    /// <param name="controlX">The control point's x coordinate.</param>
    /// <param name="controlY">The control point's y coordinate.</param>
    /// <param name="x">The end's x coordinate.</param>
    /// <param name="y">The end's y coordinate.</param>
    /// <returns>This builder.</returns>
    public SubpathBuilder QuadraticTo(double controlX, double controlY, double x, double y) => Add(new QuadraticTo(controlX, controlY, x, y));

    /// <summary>Adds a cubic Bézier curve.</summary>
    /// <param name="control1X">The first control point's x coordinate.</param>
    /// <param name="control1Y">The first control point's y coordinate.</param>
    /// <param name="control2X">The second control point's x coordinate.</param>
    /// <param name="control2Y">The second control point's y coordinate.</param>
    /// <param name="x">The end's x coordinate.</param>
    /// <param name="y">The end's y coordinate.</param>
    /// <returns>This builder.</returns>
    public SubpathBuilder CubicTo(double control1X, double control1Y, double control2X, double control2Y, double x, double y) =>
        Add(new CubicTo(control1X, control1Y, control2X, control2Y, x, y));

    /// <summary>Closes the current subpath; the current point becomes the subpath's start, so a line may follow
    /// (ISO 32000-1 §8.5.2.1, the h operator), and starts a new subpath there.</summary>
    /// <returns>This builder.</returns>
    public SubpathBuilder Close() => Add(ClosePath.Instance);

    /// <summary>The path built so far.</summary>
    /// <returns>The path.</returns>
    public Path ToPath() => new(_commands.ToImmutable());

    private SubpathBuilder Add(PathCommand command)
    {
        _commands.Add(command);
        return this;
    }
}
