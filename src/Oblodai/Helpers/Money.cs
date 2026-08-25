using System.Numerics;

namespace Oblodai;

/// <summary>
/// Amounts are decimal strings on the wire and stay strings here; never parse one into a
/// <see cref="double"/> (USDT has 6 decimals, BTC 8, ETH 18). These helpers compare and add at
/// arbitrary precision using <see cref="BigInteger"/>.
/// </summary>
public static class Money
{
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

    private static (bool Negative, string Integer, string Fraction) Parts(string amount)
    {
        if (string.IsNullOrEmpty(amount))
        {
            throw new FormatException("not a decimal amount: \"\"");
        }

        var negative = amount[0] == '-';
        var body = negative ? amount[1..] : amount;
        var dot = body.IndexOf('.');
        var integer = dot < 0 ? body : body[..dot];
        var fraction = dot < 0 ? string.Empty : body[(dot + 1)..];

        if (integer.Length == 0 || !integer.All(char.IsAsciiDigit) || !fraction.All(char.IsAsciiDigit)
            || (dot >= 0 && fraction.Length == 0))
        {
            throw new FormatException($"not a decimal amount: \"{amount}\"");
        }

        return (negative, integer, fraction);
    }

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
