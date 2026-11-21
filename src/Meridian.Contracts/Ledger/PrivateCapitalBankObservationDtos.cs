using Meridian.Contracts.Banking;

namespace Meridian.Contracts.Ledger;

/// <summary>Structured settled cash observation retained in an accepted Evidence Vault JSON document.</summary>
public sealed record PrivateCapitalBankObservationSourceDto(
    Guid BankTransactionId,
    Guid EntityId,
    decimal Amount,
    string Currency,
    DateOnly EffectiveDate,
    DateOnly TransactionDate,
    DateOnly SettlementDate,
    string Direction,
    string StatementReference,
    DateTimeOffset ObservedAtUtc);

/// <summary>Select retained source bytes; no caller-supplied amount or cash authority is accepted.</summary>
public sealed record ImportPrivateCapitalBankObservationRequest(string EvidenceLink, string DocumentId);

/// <summary>Retained observation result. This does not post cash or establish live-bank compatibility.</summary>
public sealed record PrivateCapitalBankObservationImportResultDto(
    BankTransactionDto BankTransaction,
    string EvidenceLink,
    string DocumentId,
    string SourceHashSha256,
    bool IsReplay);
