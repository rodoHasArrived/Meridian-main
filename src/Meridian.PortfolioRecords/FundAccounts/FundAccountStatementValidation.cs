using Meridian.Contracts.FundStructure;

namespace Meridian.PortfolioRecords.FundAccounts;

/// <summary>Preserves statement lineage before either storage implementation accepts a batch.</summary>
internal static class FundAccountStatementValidation
{
    internal static void Validate(IngestCustodianStatementRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Lines);
        foreach (var line in request.Lines)
        {
            if (line is null || line.AccountId != request.AccountId || line.BatchId != request.BatchId
                || line.AsOfDate != request.AsOfDate)
            {
                throw new ArgumentException(
                    "Every custodian statement line must belong to the requested account, batch, and as-of date.",
                    nameof(request.Lines));
            }
        }
    }

    internal static void Validate(IngestBankStatementRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Lines);
        foreach (var line in request.Lines)
        {
            if (line is null || line.AccountId != request.AccountId || line.BatchId != request.BatchId)
            {
                throw new ArgumentException(
                    "Every bank statement line must belong to the requested account and batch.",
                    nameof(request.Lines));
            }
        }
    }
}
