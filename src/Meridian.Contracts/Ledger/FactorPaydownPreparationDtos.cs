using System.Text.Json.Serialization;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;

namespace Meridian.Contracts.Ledger;

/// <summary>Operator assertions for a paydown; scope authority, lots and amounts are resolved by the server.</summary>
public sealed record PrepareFactorPaydownRequestDto(
    Guid LedgerBookId,
    Guid SecurityId,
    Guid BookPositionId,
    Guid PeriodId,
    long ExpectedPeriodVersion,
    AccountingBasisKindDto AccountingBasis,
    string FundProfileId,
    DateOnly EffectiveDate,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal PriorFactor,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal NewFactor,
    string SourceDomain,
    string SourceEntityId,
    string SourceContentHash,
    DateTimeOffset OccurredAtUtc,
    IReadOnlyList<RetainedEvidenceIdentityDto> FactorEvidence,
    string CashAccountPath,
    string CarryingAccountPath,
    string RealizedGainAccountPath,
    string RealizedLossAccountPath,
    Guid? CorrelationId = null);

/// <summary>Read-only preview. Decimal component strings preserve exact workstation display.</summary>
public sealed record FactorPaydownPreparationDto(
    OpenLotFactorPaydownInstructionDto Instruction,
    AssetLotMutationInstructionDto LotMutation,
    ProjectAssetAccountingEventRequestDto ProjectRequest,
    IReadOnlyDictionary<string, string> Components,
    string PreparationFingerprint);

/// <summary>Rechecks the reviewed preview before retaining a governed draft; never approves or posts.</summary>
public sealed record CreateFactorPaydownDraftRequestDto(
    PrepareFactorPaydownRequestDto Request,
    string ExpectedPreparationFingerprint,
    string Description);
