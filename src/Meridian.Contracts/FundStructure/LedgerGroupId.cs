namespace Meridian.Contracts.FundStructure;

public readonly record struct LedgerGroupId
{
    public const string UnassignedValue = "unassigned";
    public const string ValidationMessage =
        "Ledger group IDs must be non-empty and may contain only letters, digits, '-', '_', '.', or ':'.";

    public static LedgerGroupId Unassigned { get; } = new(UnassignedValue, skipValidation: true);

    public string Value { get; }

    public LedgerGroupId(string value)
    {
        Value = Normalize(value);
    }

    private LedgerGroupId(string value, bool skipValidation)
    {
        Value = skipValidation ? value : Normalize(value);
    }

    public static bool TryCreate(string? value, out LedgerGroupId ledgerGroupId)
    {
        if (!TryNormalize(value, out var normalized))
        {
            ledgerGroupId = default;
            return false;
        }

        ledgerGroupId = string.Equals(normalized, UnassignedValue, StringComparison.Ordinal)
            ? Unassigned
            : new LedgerGroupId(normalized, skipValidation: true);
        return true;
    }

    public static LedgerGroupId Create(string value) => new(value);

    public static string Normalize(string value)
    {
        if (!TryNormalize(value, out var normalized))
        {
            throw new FormatException(ValidationMessage);
        }

        return normalized;
    }

    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim();
        if (string.Equals(candidate, UnassignedValue, StringComparison.OrdinalIgnoreCase))
        {
            normalized = UnassignedValue;
            return true;
        }

        foreach (var ch in candidate)
        {
            if (char.IsLetterOrDigit(ch))
            {
                continue;
            }

            if (ch is '-' or '_' or '.' or ':')
            {
                continue;
            }

            return false;
        }

        normalized = candidate;
        return true;
    }

    public override string ToString() => Value;
}
