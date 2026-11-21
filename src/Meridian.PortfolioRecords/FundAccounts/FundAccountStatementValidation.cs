using Meridian.Contracts.FundStructure;

namespace Meridian.PortfolioRecords.FundAccounts;

/// <summary>Preserves statement lineage before either storage implementation accepts a batch.</summary>
internal static class FundAccountStatementValidation
{
    internal static void Validate(IngestCustodianStatementRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Lines);
        if (request.BatchId == Guid.Empty || request.AccountId == Guid.Empty || request.AsOfDate == default)
            throw new ArgumentException("Custodian statements require account, batch and date identities.");
        if (request.Authority is { } authority && (authority.Revision <= 0
            || string.IsNullOrWhiteSpace(authority.SourceEvidenceId)
            || string.IsNullOrWhiteSpace(authority.SourceContentSha256)
            || authority.SourceContentSha256.Length != 64 || authority.SourceContentSha256.Any(c => !Uri.IsHexDigit(c))
            || authority.SupersedesBatchId == request.BatchId))
            throw new ArgumentException("Custodian statement authority requires positive revision and retained source evidence/hash.");
        var identities = new HashSet<Guid>();
        foreach (var line in request.Lines)
        {
            if (line is null || line.AccountId != request.AccountId || line.BatchId != request.BatchId
                || line.AsOfDate != request.AsOfDate)
            {
                throw new ArgumentException(
                    "Every custodian statement line must belong to the requested account, batch, and as-of date.",
                    nameof(request.Lines));
            }
            if (line.LineId == Guid.Empty || !identities.Add(line.LineId))
                throw new ArgumentException("Custodian statement line identities must be nonempty and unique.");
            if (line.Bond is { } bond)
            {
                if (line.SecurityId is null || line.SecurityId == Guid.Empty)
                    throw new ArgumentException("A normalized bond holding requires resolved Security Master identity.");
                var normalized = BondHoldingNormalization.Calculate(bond, line.Currency);
                if (Meridian.Contracts.Ledger.LedgerCurrencyRounding.RoundCurrency(line.MarketValue, line.Currency)
                    != Meridian.Contracts.Ledger.LedgerCurrencyRounding.RoundCurrency(normalized.Dirty, line.Currency))
                    throw new ArgumentException("Bond holding market value must use dirty cash value.");
            }
        }
    }

    internal static void Validate(IngestBankStatementRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Lines);
        if (request.StatementRevision <= 0)
        {
            throw new ArgumentException("A bank statement revision must be positive.", nameof(request.StatementRevision));
        }

        // Authority selectors only see retained lines. An empty higher revision would keep its batch
        // header but leave the lower revision's cash flows and closing balance authoritative.
        if (request.Lines.Count == 0)
        {
            throw new ArgumentException("A bank statement revision must contain at least one line.", nameof(request.Lines));
        }

        var sequences = new HashSet<int>();
        var lineIds = new HashSet<Guid>();
        var index = 0;
        foreach (var line in request.Lines)
        {
            if (line is null || line.AccountId != request.AccountId || line.BatchId != request.BatchId)
            {
                throw new ArgumentException(
                    "Every bank statement line must belong to the requested account and batch.",
                    nameof(request.Lines));
            }

            var sequence = line.LineSequence ?? index + 1;
            if (sequence <= 0 || !sequences.Add(sequence) || !lineIds.Add(line.LineId))
            {
                throw new ArgumentException("Bank statement lines require unique identities and positive, unique source sequences.", nameof(request.Lines));
            }
            index++;
        }
    }

    internal static IReadOnlyList<BankStatementLineDto> RetainAuthority(IngestBankStatementRequest request) =>
        request.Lines.Select((line, index) => line with
        {
            LineSequence = line.LineSequence ?? index + 1,
            StatementRevision = request.StatementRevision,
            StatementAsOfDate = request.StatementDate,
            IsTransactionFeed = request.IsTransactionFeed ? true : null
        }).ToArray();
}
