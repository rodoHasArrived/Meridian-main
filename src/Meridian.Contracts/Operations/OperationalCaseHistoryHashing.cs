using System.Security.Cryptography;
using System.Text.Json;

namespace Meridian.Contracts.Operations;

/// <summary>Canonical hash calculation for one persisted case-history record.</summary>
public static class OperationalCaseHistoryHashing
{
    public const int MaxDataEntries = 64;
    public const int MaxDataKeyLength = 128;
    public const int MaxDataValueLength = 1_048_576;
    public const int MaxDataTotalCharacters = 2_097_152;

    public static string ComputeRecordHashSha256(OperationalCaseHistoryRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var dataErrors = ValidateData(record.Data);
        if (dataErrors.Count > 0)
        {
            throw new ArgumentException(
                $"Operational case-history data is invalid: {string.Join(" ", dataErrors)}",
                nameof(record));
        }

        var canonicalRecord = record with
        {
            RecordHashSha256 = string.Empty,
            Data = new SortedDictionary<string, string>(
                record.Data.ToDictionary(static item => item.Key, static item => item.Value, StringComparer.Ordinal),
                StringComparer.Ordinal)
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            canonicalRecord,
            OperationsContractsJsonContext.Default.OperationalCaseHistoryRecord);

        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    public static bool HasValidRecordHash(OperationalCaseHistoryRecord record) =>
        string.Equals(
            record.RecordHashSha256,
            ComputeRecordHashSha256(record),
            StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<string> ValidateData(IReadOnlyDictionary<string, string>? data)
    {
        if (data is null)
            return ["Data cannot be null."];

        var errors = new List<string>();
        if (data.Count > MaxDataEntries)
            errors.Add($"Data cannot contain more than {MaxDataEntries} entries.");

        long totalCharacters = 0;
        foreach (var (key, value) in data)
        {
            if (string.IsNullOrWhiteSpace(key))
                errors.Add("Data keys cannot be empty.");
            else if (key.Length > MaxDataKeyLength)
                errors.Add($"Data key '{key[..MaxDataKeyLength]}' exceeds {MaxDataKeyLength} characters.");

            if (value is null)
                errors.Add($"Data value for '{key}' cannot be null.");
            else if (value.Length > MaxDataValueLength)
                errors.Add($"Data value for '{key}' exceeds {MaxDataValueLength} characters.");

            totalCharacters += key?.Length ?? 0;
            totalCharacters += value?.Length ?? 0;
        }

        if (totalCharacters > MaxDataTotalCharacters)
            errors.Add($"Data cannot exceed {MaxDataTotalCharacters} total characters.");

        return errors;
    }
}
