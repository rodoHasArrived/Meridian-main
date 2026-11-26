namespace Meridian.Contracts.CashFlows;

/// <summary>
/// Decimal power for prepayment survival (prepayment arithmetic v2). Survival raises a rate base in
/// [0, 1] to a non-negative fractional exponent; doing it in decimal keeps whole-period cases exact
/// (an exponent of 1 returns the base itself) and avoids the binary rounding of <see cref="Math.Pow"/>.
/// The general case is exp(exponent × ln(base)), each series carried to decimal's working precision.
/// </summary>
internal static class DecimalMath
{
    /// <summary>ln 2 to 28 significant digits.</summary>
    private const decimal Ln2 = 0.6931471805599453094172321215m;

    /// <summary>exp(y) is below decimal's smallest positive value for y under this bound.</summary>
    private const decimal ExpUnderflow = -66m;

    /// <summary>
    /// <paramref name="baseValue"/> raised to <paramref name="exponent"/>, for a base in [0, 1] and a
    /// non-negative exponent (the survival domain).
    /// </summary>
    public static decimal Power(decimal baseValue, decimal exponent)
    {
        if (baseValue is < 0m or > 1m)
            throw new ArgumentOutOfRangeException(nameof(baseValue), baseValue, "The survival base must lie in [0, 1].");
        if (exponent < 0m)
            throw new ArgumentOutOfRangeException(nameof(exponent), exponent, "The survival exponent must be non-negative.");
        if (exponent == 0m || baseValue == 1m)
            return 1m;
        if (baseValue == 0m)
            return 0m;
        if (exponent == 1m)
            return baseValue;
        if (decimal.Truncate(exponent) == exponent && exponent <= 64m)
            return IntegerPower(baseValue, (int)exponent);
        return Exp(exponent * Ln(baseValue));
    }

    private static decimal IntegerPower(decimal baseValue, int exponent)
    {
        var result = 1m;
        for (var i = 0; i < exponent; i++)
            result *= baseValue;
        return result;
    }

    /// <summary>Natural log of a value in (0, 1].</summary>
    internal static decimal Ln(decimal value)
    {
        if (value <= 0m || value > 1m)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Ln is defined here on (0, 1].");
        if (value == 1m)
            return 0m;

        // Scale into [0.5, 1): ln(value) = ln(scaled) - k·ln 2.
        var halvings = 0;
        while (value < 0.5m)
        {
            value *= 2m;
            halvings++;
        }

        // ln(x) = 2·atanh(z) with z = (x - 1) / (x + 1) in [-1/3, 0), so z² ≤ 1/9 and the series
        // converges geometrically.
        var z = (value - 1m) / (value + 1m);
        var zSquared = z * z;
        var power = z;
        var sum = 0m;
        for (var n = 1; ; n += 2)
        {
            var term = power / n;
            if (term == 0m)
                break;
            sum += term;
            power *= zSquared;
        }

        return 2m * sum - halvings * Ln2;
    }

    /// <summary>exp(y) for y ≤ 0.</summary>
    internal static decimal Exp(decimal y)
    {
        if (y > 0m)
            throw new ArgumentOutOfRangeException(nameof(y), y, "Exp is defined here for non-positive arguments.");
        if (y == 0m)
            return 1m;
        if (y < ExpUnderflow)
            return 0m;

        // y = -k·ln 2 + r with r in [0, ln 2): exp(y) = exp(r) / 2^k.
        var halvings = (int)decimal.Ceiling(-y / Ln2);
        var r = y + halvings * Ln2;
        var term = 1m;
        var sum = 1m;
        for (var n = 1; ; n++)
        {
            term = term * r / n;
            if (term == 0m)
                break;
            sum += term;
        }

        for (var i = 0; i < halvings; i++)
            sum /= 2m;
        return sum;
    }
}
