using System.Globalization;
using System.Numerics;

namespace Oblodai;

/// <summary>
/// Amounts are decimal strings on the wire and <see cref="decimal"/> in the models; never put one into
/// a <see cref="double"/> (USDT has 6 decimals, BTC 8, ETH 18). <see cref="Of"/> and
/// <see cref="Parse"/> turn what you have into a <see cref="decimal"/> — refusing binary floating
/// point with <c>sdk.float_amount</c> — and the string helpers compare and add at arbitrary precision
/// using <see cref="BigInteger"/>, for amounts wider than <see cref="decimal"/> (18-decimal tokens).
/// </summary>
public static class Money
{
    /// <summary>
    /// A decimal amount from a <see cref="decimal"/>, an integer or a decimal string. A
    /// <see cref="double"/> or <see cref="float"/> is refused with <c>sdk.float_amount</c>: it cannot
    /// hold <c>0.1</c> exactly, and the wrong cent would reach the gateway without a word.
    /// </summary>
    /// <param name="value">The amount.</param>
    /// <exception cref="ConfigException"><c>sdk.float_amount</c>, or <c>sdk.bad_amount</c> for anything else.</exception>
    public static decimal Of(object? value) => value switch
    {
        decimal d => d,
        string s => Parse(s),
        int or long or short or byte or sbyte or ushort or uint or ulong => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
        double or float or Half => throw new ConfigException(
            SdkErrorCodes.FloatAmount,
            $"amount {Convert.ToString(value, CultureInfo.InvariantCulture)} is binary floating point; use a decimal (25.10m) or a decimal string (\"25.10\")",
            "amount"),
        _ => throw new ConfigException(
            SdkErrorCodes.BadAmount, $"not a decimal amount: {value?.GetType().Name ?? "null"}", "amount"),
    };

    /// <summary>A decimal string (<c>"25.10"</c>) as a <see cref="decimal"/>, keeping its scale; invariant culture.</summary>
    /// <param name="amount">The amount as the wire writes it.</param>
    /// <exception cref="ConfigException"><c>sdk.bad_amount</c>: not a plain decimal, or out of <see cref="decimal"/>'s range.</exception>
    public static decimal Parse(string amount)
    {
        Parts(amount);
        try
        {
            return decimal.Parse(amount, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        }
        catch (OverflowException)
        {
            throw new ConfigException(
                SdkErrorCodes.BadAmount, $"amount \"{amount}\" does not fit a decimal; keep it as a string", "amount");
        }
    }

    /// <summary>The wire form of an amount: invariant digits, the scale kept (<c>25.10m</c> → <c>"25.10"</c>).</summary>
    /// <param name="amount">The amount.</param>
    public static string Format(decimal amount) => amount.ToString(CultureInfo.InvariantCulture);

    /// <summary>-1, 0 or 1, comparing two decimal amounts exactly.</summary>
    /// <param name="a">Left amount.</param>
    /// <param name="b">Right amount.</param>
    public static int Compare(string a, string b)
    {
        var scale = Math.Max(ScaleOf(a), ScaleOf(b));
        return Scaled(a, scale).CompareTo(Scaled(b, scale));
    }

    /// <summary>True when both amounts denote the same value (<c>"25"</c> equals <c>"25.000000"</c>).</summary>
    /// <param name="a">Left amount.</param>
    /// <param name="b">Right amount.</param>
    public static bool AreEqual(string a, string b) => Compare(a, b) == 0;

    /// <summary>Exact sum, at the wider of the two scales.</summary>
    /// <param name="a">Left amount.</param>
    /// <param name="b">Right amount.</param>
    public static string Add(string a, string b)
    {
        var scale = Math.Max(ScaleOf(a), ScaleOf(b));
        return Unscale(Scaled(a, scale) + Scaled(b, scale), scale);
    }

    /// <summary>Exact difference, at the wider of the two scales.</summary>
    /// <param name="a">Left amount.</param>
    /// <param name="b">Right amount.</param>
    public static string Subtract(string a, string b)
    {
        var scale = Math.Max(ScaleOf(a), ScaleOf(b));
        return Unscale(Scaled(a, scale) - Scaled(b, scale), scale);
    }

    /// <summary>True when the amount is zero at any scale.</summary>
    /// <param name="amount">The amount.</param>
    public static bool IsZero(string amount) => Scaled(amount, ScaleOf(amount)).IsZero;

    /// <summary>
    /// Longest amount these helpers accept. The wire has no amount anywhere near it; the bound exists so
    /// a hostile or corrupted string cannot turn a comparison into a multi-megabyte <see cref="BigInteger"/>.
    /// </summary>
    public const int MaxLength = 64;

    private static (bool Negative, string Integer, string Fraction) Parts(string? amount)
    {
        if (string.IsNullOrEmpty(amount))
        {
            throw Invalid(amount);
        }

        if (amount.Length > MaxLength)
        {
            throw new ConfigException(
                SdkErrorCodes.BadAmount,
                $"not a decimal amount: {amount.Length} characters, the maximum is {MaxLength}",
                "amount");
        }

        var negative = amount[0] == '-';
        var body = negative ? amount[1..] : amount;
        var dot = body.IndexOf('.');
        var integer = dot < 0 ? body : body[..dot];
        var fraction = dot < 0 ? string.Empty : body[(dot + 1)..];

        // One dot at most, digits on both sides of it, and nothing else: no exponent, no thousands
        // separator, no leading plus, no whitespace. Everything the gateway sends passes; nothing that
        // would silently mean a different number does.
        if (integer.Length == 0 || !integer.All(char.IsAsciiDigit) || !fraction.All(char.IsAsciiDigit)
            || (dot >= 0 && fraction.Length == 0))
        {
            throw Invalid(amount);
        }

        return (negative, integer, fraction);
    }

    /// <summary>
    /// The SDK's own error, never a native <see cref="FormatException"/>: a caller catching
    /// <c>OblodaiException</c> around SDK calls must not have one kind of bad input escape that net.
    /// </summary>
    private static ConfigException Invalid(string? amount)
        => new(SdkErrorCodes.BadAmount, $"not a decimal amount: \"{amount}\"", "amount");

    private static int ScaleOf(string amount) => Parts(amount).Fraction.Length;

    private static BigInteger Scaled(string amount, int scale)
    {
        var (negative, integer, fraction) = Parts(amount);
        var value = BigInteger.Parse(integer + fraction.PadRight(scale, '0'));
        return negative ? -value : value;
    }

    private static string Unscale(BigInteger value, int scale)
    {
        var negative = value.Sign < 0;
        var digits = BigInteger.Abs(value).ToString().PadLeft(scale + 1, '0');
        var integer = digits[..^scale];
        var fraction = scale == 0 ? string.Empty : "." + digits[^scale..];
        return (negative ? "-" : string.Empty) + integer + fraction;
    }
}
