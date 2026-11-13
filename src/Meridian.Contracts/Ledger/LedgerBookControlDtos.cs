using System.Text.Json.Serialization;

namespace Meridian.Contracts.Ledger;

[JsonConverter(typeof(JsonStringEnumConverter<LedgerBookControlModeDto>))]
public enum LedgerBookControlModeDto { LegacyWarning, Enforced }

public sealed record LedgerBookControlProfileDto(
    Guid LedgerBookId,
    LedgerBookControlModeDto Mode,
    long Version,
    string PolicyVersion,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ActivatedAtUtc = null,
    string? ActivatedBy = null,
    string? MigrationReceiptId = null);

public sealed record LedgerControlDecisionDto(
    string Control,
    string Code,
    bool Allowed,
    string Message,
    string? EvidenceRevision = null);

public sealed record LedgerBookControlPreflightDto(
    Guid LedgerBookId,
    long ProfileVersion,
    string PolicyVersion,
    IReadOnlyList<LedgerControlDecisionDto> Decisions,
    string Digest,
    DateTimeOffset PreparedAtUtc)
{
    public bool CanActivate => Decisions.All(item => item.Allowed);
}

public sealed record ActivateLedgerBookControlsRequestDto(
    long ExpectedVersion,
    string PreflightDigest,
    string Reason,
    string EvidenceReference);
