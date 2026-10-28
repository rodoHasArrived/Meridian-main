using System.Security.Cryptography;
using System.Text;
using Meridian.Contracts.FundStructure;

namespace Meridian.PortfolioRecords.FundAccounts;

/// <summary>Recognizes exact pre-feed Plaid deliveries without treating a bank label as provenance.</summary>
internal static class LegacyPlaidTransactionFeedBackfill
{
    public static bool CanUpgrade(BankStatementBatchDto batch, IReadOnlyList<BankStatementLineDto> allLines, Guid storedAccountId)
    {
        if (batch.IsTransactionFeed == true || batch.AccountId != storedAccountId
            || !string.Equals(batch.BankName, "Plaid", StringComparison.Ordinal) || batch.LineCount <= 0)
            return false;
        var lines = allLines.Where(line => line.BatchId == batch.BatchId).ToArray();
        if (lines.Length != batch.LineCount || lines.Any(line => line.AccountId != batch.AccountId
            || line.ClosingBalance.HasValue || line.IsTransactionFeed == true || !IsUnambiguousReference(line.Reference)))
            return false;
        var references = lines.Select(static line => line.Reference!).ToArray();
        if (references.Distinct(StringComparer.OrdinalIgnoreCase).Count() != lines.Length
            || lines.Any(line => line.LineId != HistoricalGuid($"plaid-bank-line:{line.Reference}")))
            return false;
        var material = $"plaid-bank-batch:{batch.AccountId:D}:{string.Join('|', references.Order(StringComparer.OrdinalIgnoreCase))}";
        return batch.BatchId == HistoricalGuid(material);
    }

    private static bool IsUnambiguousReference(string? value) => !string.IsNullOrEmpty(value)
        && value.All(static character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    // Compatibility evidence only: this reproduces the historical connector's MD5-to-Guid identity.
    private static Guid HistoricalGuid(string value) => new(MD5.HashData(Encoding.UTF8.GetBytes(value)));
}
