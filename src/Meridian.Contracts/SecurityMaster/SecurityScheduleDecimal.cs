using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>Exact decimal admission and accumulation for contractual schedule values.</summary>
internal static class SecurityScheduleDecimal
{
    private const int MaximumScale = 28;
    private static readonly BigInteger MaximumCoefficient = new(decimal.MaxValue);

    internal enum SumResult
    {
        Exact,
        Overflow,
        PrecisionLoss
    }

    internal static bool TryRead(JsonElement element, out decimal value)
    {
        value = default;
        string raw;
        decimal candidate;
        bool hasCandidate;
        var jsonNumber = element.ValueKind == JsonValueKind.Number;
        if (jsonNumber)
        {
            raw = element.GetRawText();
            hasCandidate = element.TryGetDecimal(out candidate);
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            raw = element.GetString()!;
            // Retain the legacy NumberStyles.Number grammar, including grouping and trailing signs.
            // Its rounded result is not evidence that the original value fits a decimal exactly.
            hasCandidate = decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out candidate);
            if (!hasCandidate)
                return false;
        }
        else
        {
            return false;
        }

        if (!TryReadExact(raw, jsonNumber, out var exact)
            || (!jsonNumber && candidate != exact))
        {
            return false;
        }

        // Preserve the original decimal scale when the standard reader represented the value
        // exactly. The source JSON itself remains untouched, including insignificant trailing zeros.
        value = hasCandidate && candidate == exact ? candidate : exact;
        return true;
    }

    internal static SumResult Sum(IEnumerable<decimal> values, out decimal total)
    {
        // Accumulate units of 10^-28 without decimal arithmetic. A rounded intermediate subtotal
        // could discard a payment, and rejecting it could make acceptance depend on input order:
        // later payments can make the complete total exactly representable again.
        var coefficient = BigInteger.Zero;
        foreach (var value in values)
        {
            var bits = decimal.GetBits(value);
            var amount = (BigInteger)(uint)bits[0]
                         | ((BigInteger)(uint)bits[1] << 32)
                         | ((BigInteger)(uint)bits[2] << 64);
            var scale = (bits[3] >> 16) & 0xff;
            if ((bits[3] & int.MinValue) != 0)
                amount = -amount;
            coefficient += amount * BigInteger.Pow(10, MaximumScale - scale);
        }
        return Convert(coefficient, MaximumScale, out total);
    }

    private static bool TryReadExact(string raw, bool jsonNumber, out decimal value)
    {
        value = default;
        var mantissa = raw.AsSpan();
        ReadOnlySpan<char> exponentText = default;
        var exponentIndex = jsonNumber ? mantissa.IndexOfAny('e', 'E') : -1;
        if (exponentIndex >= 0)
        {
            exponentText = mantissa[(exponentIndex + 1)..];
            mantissa = mantissa[..exponentIndex];
        }

        // JSON has already validated a numeric token; string grammar was checked above. All
        // remaining non-digit characters are separators, whitespace or an admitted leading/trailing sign.
        var digits = new StringBuilder(mantissa.Length);
        var fractionalDigits = 0;
        var fractional = false;
        var negative = false;
        foreach (var character in mantissa)
        {
            if (character is >= '0' and <= '9')
            {
                digits.Append(character);
                if (fractional)
                    fractionalDigits++;
            }
            else if (character == '.')
                fractional = true;
            else if (character == '-')
                negative = true;
        }

        var digitText = digits.ToString();
        var start = 0;
        var end = digitText.Length;
        while (start < end && digitText[start] == '0')
            start++;
        if (start == end)
            return true; // Zero is exact regardless of exponent or the number of fractional zeros.
        while (digitText[end - 1] == '0')
            end--;
        if (end - start > 29)
            return false;

        var baseScale = (long)fractionalDigits - (digitText.Length - end);
        long exponent = 0;
        if (!exponentText.IsEmpty
            && !long.TryParse(exponentText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent))
            return false;
        // The significant coefficient has at most 29 digits. This bound also prevents huge JSON
        // exponents from triggering unbounded powers or overflowing the scale calculation.
        if (exponent < baseScale - MaximumScale || exponent > baseScale + MaximumScale)
            return false;
        var scale = (int)(baseScale - exponent);
        var coefficient = BigInteger.Parse(digitText.AsSpan(start, end - start), NumberStyles.None, CultureInfo.InvariantCulture);
        if (negative)
            coefficient = -coefficient;
        if (scale < 0)
        {
            coefficient *= BigInteger.Pow(10, -scale);
            scale = 0;
        }
        return Convert(coefficient, scale, out value) == SumResult.Exact;
    }

    private static SumResult Convert(BigInteger coefficient, int scale, out decimal value)
    {
        value = default;
        var negative = coefficient.Sign < 0;
        coefficient = BigInteger.Abs(coefficient);
        while (scale > 0 && coefficient % 10 == 0)
        {
            coefficient /= 10;
            scale--;
        }
        if (coefficient > MaximumCoefficient * BigInteger.Pow(10, scale))
            return SumResult.Overflow;
        if (scale > MaximumScale || coefficient > MaximumCoefficient)
            return SumResult.PrecisionLoss;

        value = new decimal(
            unchecked((int)(uint)(coefficient & uint.MaxValue)),
            unchecked((int)(uint)((coefficient >> 32) & uint.MaxValue)),
            unchecked((int)(uint)((coefficient >> 64) & uint.MaxValue)),
            negative, (byte)scale);
        return SumResult.Exact;
    }
}
