namespace Marv.Core.Formatting;

/// <summary>
/// Represents an IRC color code. Use directly in string interpolation for stateful
/// color changes, or with <see cref="IrcFormat.Color(string, IrcColor)"/> for
/// balanced wrap-and-reset formatting.
/// </summary>
/// <remarks>
/// <para>
/// IRC colors are <b>stateful</b>: inserting a color code changes the foreground
/// (and optionally background) for all subsequent text until the next color code
/// or a reset (<c>\x0F</c>). This struct emits the raw <c>\x03NN</c> sequence
/// when used in string interpolation, enabling the stateful pattern:
/// </para>
/// <code>
/// $"{IrcColor.Cyan.On(IrcColor.Black)}[{IrcColor.Orange} Community {IrcColor.Cyan}]"
/// </code>
/// </remarks>
public readonly struct IrcColor : IEquatable<IrcColor>
{
    /// <summary>White (0).</summary>
    public static readonly IrcColor White = new(0);

    /// <summary>Black (1).</summary>
    public static readonly IrcColor Black = new(1);

    /// <summary>Blue / Navy (2).</summary>
    public static readonly IrcColor Blue = new(2);

    /// <summary>Green (3).</summary>
    public static readonly IrcColor Green = new(3);

    /// <summary>Red (4).</summary>
    public static readonly IrcColor Red = new(4);

    /// <summary>Brown (5).</summary>
    public static readonly IrcColor Brown = new(5);

    /// <summary>Purple (6).</summary>
    public static readonly IrcColor Purple = new(6);

    /// <summary>Orange (7).</summary>
    public static readonly IrcColor Orange = new(7);

    /// <summary>Yellow (8).</summary>
    public static readonly IrcColor Yellow = new(8);

    /// <summary>Light Green (9).</summary>
    public static readonly IrcColor LightGreen = new(9);

    /// <summary>Cyan / Teal (10).</summary>
    public static readonly IrcColor Cyan = new(10);

    /// <summary>Light Cyan (11).</summary>
    public static readonly IrcColor LightCyan = new(11);

    /// <summary>Light Blue (12).</summary>
    public static readonly IrcColor LightBlue = new(12);

    /// <summary>Pink (13).</summary>
    public static readonly IrcColor Pink = new(13);

    /// <summary>Grey (14).</summary>
    public static readonly IrcColor Grey = new(14);

    /// <summary>Light Grey (15).</summary>
    public static readonly IrcColor LightGrey = new(15);

    /// <summary>Default / reset to client default (99).</summary>
    public static readonly IrcColor Default = new(99);

    /// <summary>The numeric color code (0-98 standard, 99 for default).</summary>
    public int Code { get; }

    /// <summary>
    /// Creates an <see cref="IrcColor"/> with the specified numeric code.
    /// Use this for extended colors (16-98) not covered by the named constants.
    /// </summary>
    /// <param name="code">The mIRC color code (0-99).</param>
    public IrcColor(int code)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(code);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(code, 99);
        Code = code;
    }

    /// <summary>
    /// Returns the color code string for setting the foreground and background.
    /// Emits <c>\x03fg,bg</c>.
    /// </summary>
    /// <param name="background">The background color.</param>
    public string On(IrcColor background) =>
        $"\x03{Code:D2},{background.Code:D2}";

    /// <summary>
    /// Returns the raw color code string (<c>\x03NN</c>) for use in string interpolation.
    /// Sets the foreground color; background is unchanged.
    /// </summary>
    public override string ToString() => $"\x03{Code:D2}";

    /// <inheritdoc />
    public bool Equals(IrcColor other) => Code == other.Code;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is IrcColor other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Code;

    /// <summary>Equality operator.</summary>
    public static bool operator ==(IrcColor left, IrcColor right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(IrcColor left, IrcColor right) => !left.Equals(right);
}
