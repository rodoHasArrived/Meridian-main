using Meridian.Contracts.FundStructure;

namespace Meridian.Contracts.Ledger;

public sealed record AccountingBookContextDto(
    Guid LedgerBookId,
    string FundProfileId,
    Guid FundStructureNodeId,
    FundStructureNodeKindDto FundStructureNodeKind,
    string DisplayName,
    string BaseCurrency,
    AccountingBasisKindDto AccountingBasis,
    string AccountingPolicyId,
    string AccountingPolicyVersion,
    Guid? PeriodId = null,
    LedgerDimensionSetDto? Dimensions = null);

public sealed record AccountingRulePackReferenceDto(
    string RulePackId,
    string RulePackVersion,
    string? SelectedRuleId = null,
    string? SelectedRuleVersion = null);
