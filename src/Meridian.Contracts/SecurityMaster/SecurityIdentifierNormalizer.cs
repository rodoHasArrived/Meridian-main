using System.Linq;
using System.Text;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Canonical normalization and lightweight format validation for Security Master identifiers.
/// This keeps resolution, duplicate detection, and UI/search projections aligned without
/// forcing the stored raw vendor string to become the only comparable form.
/// </summary>
public static class SecurityIdentifierNormalizer
{
    /// <summary>
    /// Identifiers whose value is meaningful only inside the supplied provider or venue namespace.
    /// Canonical identifiers such as ISIN, CUSIP, and FIGI remain provider-independent even when
    /// their source provider is retained as provenance.
    /// </summary>
    public static bool IsProviderScoped(SecurityIdentifierKind kind)
        => kind is SecurityIdentifierKind.ProviderSymbol or SecurityIdentifierKind.Ticker;

    /// <summary>
    /// Returns the normalized namespace that participates in identifier identity. Provider data on
    /// canonical identifier kinds is provenance, not identity, and therefore returns an empty scope.
    /// </summary>
    public static string GetIdentityScope(SecurityIdentifierDto identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        return IsProviderScoped(identifier.Kind)
            ? GetOrComputeNormalizedProvider(identifier)
            : string.Empty;
    }

    public static string NormalizeValue(SecurityIdentifierKind kind, string? value)
    {
        var trimmed = NormalizeBasic(value);
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        return kind switch
        {
            SecurityIdentifierKind.Isin
            or SecurityIdentifierKind.Cusip
            or SecurityIdentifierKind.Sedol
            or SecurityIdentifierKind.Figi
            or SecurityIdentifierKind.OccOptionSymbol
            or SecurityIdentifierKind.Lei
            or SecurityIdentifierKind.Wkn
            or SecurityIdentifierKind.Cik
                => StripNonAlphanumeric(trimmed),
            SecurityIdentifierKind.Valoren => StripNonDigit(trimmed),
            _ => trimmed
        };
    }

    public static string NormalizeProvider(string? provider)
        => NormalizeBasic(provider);

    public static string NormalizeAliasValue(string? aliasKind, string? aliasValue)
        => Enum.TryParse<SecurityIdentifierKind>(aliasKind, ignoreCase: true, out var identifierKind)
            ? NormalizeValue(identifierKind, aliasValue)
            : NormalizeBasic(aliasValue);

    public static string GetOrComputeNormalizedValue(SecurityIdentifierDto identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        return string.IsNullOrWhiteSpace(identifier.NormalizedValue)
            ? NormalizeValue(identifier.Kind, identifier.Value)
            : NormalizeValue(identifier.Kind, identifier.NormalizedValue);
    }

    public static string GetOrComputeNormalizedProvider(SecurityIdentifierDto identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        return string.IsNullOrWhiteSpace(identifier.NormalizedProvider)
            ? NormalizeProvider(identifier.Provider)
            : NormalizeProvider(identifier.NormalizedProvider);
    }

    public static bool TryValidateFormat(SecurityIdentifierKind kind, string? value, out string? message)
    {
        var normalized = NormalizeValue(kind, value);
        if (normalized.Length == 0)
        {
            message = "Identifier value is blank.";
            return false;
        }

        var isValid = kind switch
        {
            SecurityIdentifierKind.Isin => ValidateIsin(normalized),
            SecurityIdentifierKind.Cusip => ValidateCusip(normalized),
            SecurityIdentifierKind.Sedol => ValidateSedol(normalized),
            SecurityIdentifierKind.OccOptionSymbol => ValidateOccOptionSymbol(normalized),
            SecurityIdentifierKind.Lei => ValidateLei(normalized),
            SecurityIdentifierKind.Figi => ValidateFigi(normalized),
            SecurityIdentifierKind.Ric => ValidateRic(normalized),
            SecurityIdentifierKind.Wkn => normalized.Length == 6 && IsAlphaNumeric(normalized),
            SecurityIdentifierKind.Valoren => normalized.Length is >= 6 and <= 9 && IsDigitsOnly(normalized),
            SecurityIdentifierKind.Cik => normalized.Length is >= 1 and <= 10 && IsDigitsOnly(normalized),
            _ => true
        };

        message = isValid ? null : kind switch
        {
            SecurityIdentifierKind.Isin => "ISIN must be a 12-character alphanumeric code with a valid check digit.",
            SecurityIdentifierKind.Cusip => "CUSIP must be a 9-character code with a valid check digit.",
            SecurityIdentifierKind.Sedol => "SEDOL must be a 7-character alphanumeric code with a valid check digit.",
            SecurityIdentifierKind.OccOptionSymbol => "OCC option symbols must match the compact OSI format: root(1-6) + yymmdd + C/P + 8-digit strike.",
            SecurityIdentifierKind.Lei => "LEI must be a 20-character alphanumeric code with a valid ISO 17442 (mod 97-10) check digit.",
            SecurityIdentifierKind.Figi => "FIGI must be a 12-character code of consonants and digits with 'G' in position 3 and a valid check digit.",
            SecurityIdentifierKind.Ric => "RIC must be 1-40 characters using letters, digits, and RIC punctuation (. = # / ^ : _ -).",
            SecurityIdentifierKind.Wkn => "WKN must be a 6-character alphanumeric code.",
            SecurityIdentifierKind.Valoren => "Valoren must be a 6- to 9-digit Swiss security number.",
            SecurityIdentifierKind.Cik => "CIK must be a 1- to 10-digit SEC issuer code.",
            _ => "Identifier format is invalid."
        };

        return isValid;
    }

    private static string NormalizeBasic(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToUpperInvariant();

    private static string StripNonAlphanumeric(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static string StripNonDigit(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsDigit(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static bool IsAlphaNumeric(string value)
        => value.All(static character => char.IsLetterOrDigit(character));

    private static bool IsDigitsOnly(string value)
        => value.All(static character => char.IsDigit(character));

    private static bool ValidateIsin(string normalized)
    {
        if (normalized.Length != 12 || !IsAlphaNumeric(normalized))
        {
            return false;
        }

        var digits = new List<int>(24);
        foreach (var character in normalized)
        {
            if (char.IsDigit(character))
            {
                digits.Add(character - '0');
            }
            else if (character is >= 'A' and <= 'Z')
            {
                var expanded = character - 'A' + 10;
                digits.Add(expanded / 10);
                digits.Add(expanded % 10);
            }
            else
            {
                return false;
            }
        }

        var sum = 0;
        var doubleDigit = false;
        for (var index = digits.Count - 1; index >= 0; index--)
        {
            var digit = digits[index];
            if (doubleDigit)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
            doubleDigit = !doubleDigit;
        }

        return sum % 10 == 0;
    }

    private static bool ValidateCusip(string normalized)
    {
        if (normalized.Length != 9)
        {
            return false;
        }

        static bool TryGetValue(char character, out int value)
        {
            if (character is >= '0' and <= '9')
            {
                value = character - '0';
                return true;
            }

            if (character is >= 'A' and <= 'Z')
            {
                value = character - 'A' + 10;
                return true;
            }

            value = character switch
            {
                '*' => 36,
                '@' => 37,
                '#' => 38,
                _ => -1
            };

            return value >= 0;
        }

        var sum = 0;
        for (var index = 0; index < 8; index++)
        {
            if (!TryGetValue(normalized[index], out var mapped))
            {
                return false;
            }

            if (index % 2 == 1)
            {
                mapped *= 2;
            }

            sum += (mapped / 10) + (mapped % 10);
        }

        if (!char.IsDigit(normalized[8]))
        {
            return false;
        }

        var expectedCheckDigit = (10 - (sum % 10)) % 10;
        return expectedCheckDigit == normalized[8] - '0';
    }

    private static bool ValidateSedol(string normalized)
    {
        if (normalized.Length != 7 || !IsAlphaNumeric(normalized))
        {
            return false;
        }

        ReadOnlySpan<int> weights = [1, 3, 1, 7, 3, 9, 1];
        var sum = 0;
        for (var index = 0; index < normalized.Length; index++)
        {
            var character = normalized[index];
            int mapped;
            if (character is >= '0' and <= '9')
            {
                mapped = character - '0';
            }
            else if (character is >= 'A' and <= 'Z')
            {
                mapped = character - 'A' + 10;
            }
            else
            {
                return false;
            }

            sum += mapped * weights[index];
        }

        return sum % 10 == 0;
    }

    private static bool ValidateOccOptionSymbol(string normalized)
    {
        if (normalized.Length is < 16 or > 21)
        {
            return false;
        }

        var rootLength = normalized.Length - 15;
        if (rootLength is < 1 or > 6)
        {
            return false;
        }

        var root = normalized[..rootLength];
        if (!root.All(static character => character is >= 'A' and <= 'Z'))
        {
            return false;
        }

        var expiry = normalized.Substring(rootLength, 6);
        if (!expiry.All(static character => character is >= '0' and <= '9'))
        {
            return false;
        }

        var putCall = normalized[rootLength + 6];
        if (putCall is not ('C' or 'P'))
        {
            return false;
        }

        var strike = normalized[(rootLength + 7)..];
        return strike.Length == 8 && strike.All(static character => character is >= '0' and <= '9');
    }

    private static bool ValidateLei(string normalized)
    {
        if (normalized.Length != 20 || !IsAlphaNumeric(normalized))
        {
            return false;
        }

        // ISO 17442 carries an ISO 7064 MOD 97-10 checksum in the last two digits. Expanding
        // letters to two-digit values (A=10 .. Z=35) and taking the whole 20-character code
        // modulo 97 yields 1 for a valid LEI. See also the reference algorithm in
        // src/Meridian.FSharp/Domain/SecurityIdentifiers.fs.
        return Mod97(normalized) == 1;
    }

    private static bool ValidateFigi(string normalized)
    {
        if (normalized.Length != 12)
        {
            return false;
        }

        // FIGI values use upper-case consonants (vowels are never used, to avoid ISIN/ticker
        // collisions) and digits; the third character is always 'G' and the last is a MOD-10
        // check digit.
        for (var index = 0; index < 12; index++)
        {
            var character = normalized[index];
            var isDigit = character is >= '0' and <= '9';
            var isConsonant = character is >= 'A' and <= 'Z' && !IsVowel(character);
            if (!isDigit && !isConsonant)
            {
                return false;
            }
        }

        if (normalized[2] != 'G' || !char.IsDigit(normalized[11]))
        {
            return false;
        }

        return FigiCheckDigit(normalized) == normalized[11] - '0';
    }

    private static bool ValidateRic(string normalized)
    {
        // Reuters Instrument Codes are a root plus optional exchange/chain suffix and may include
        // '.', '=', '#', '/', '^', ':', '_' and '-'. There is no published check digit, so this
        // stays a lightweight structural guard: RIC characters only, at least one alphanumeric,
        // and no embedded whitespace (already removed by trimming during normalization).
        if (normalized.Length is < 1 or > 40)
        {
            return false;
        }

        // RICs are strictly ASCII; use an explicit ASCII test rather than the Unicode-aware
        // char.IsLetterOrDigit so accented/non-Latin letters cannot bypass the guard. Values are
        // already upper-cased during normalization.
        var hasAlphaNumeric = false;
        foreach (var character in normalized)
        {
            if (character is (>= 'A' and <= 'Z') or (>= '0' and <= '9'))
            {
                hasAlphaNumeric = true;
            }
            else if (character is not ('.' or '=' or '#' or '/' or '^' or ':' or '_' or '-'))
            {
                return false;
            }
        }

        return hasAlphaNumeric;
    }

    private static bool IsVowel(char character)
        => character is 'A' or 'E' or 'I' or 'O' or 'U';

    private static int Mod97(string value)
    {
        var remainder = 0;
        foreach (var character in value)
        {
            remainder = char.IsDigit(character)
                ? (remainder * 10 + (character - '0')) % 97
                : (remainder * 100 + (character - 'A' + 10)) % 97;
        }

        return remainder;
    }

    private static int FigiCheckDigit(string figi)
    {
        // Luhn-style MOD 10 over the first 11 characters (digits keep face value; letters map
        // A=10 .. Z=35); values in odd (0-based) positions are doubled and their decimal digits
        // summed. The check digit makes the running total a multiple of 10.
        var sum = 0;
        for (var index = 0; index < 11; index++)
        {
            var character = figi[index];
            var value = char.IsDigit(character) ? character - '0' : character - 'A' + 10;
            if (index % 2 == 1)
            {
                value *= 2;
            }

            sum += (value / 10) + (value % 10);
        }

        return (10 - (sum % 10)) % 10;
    }
}
