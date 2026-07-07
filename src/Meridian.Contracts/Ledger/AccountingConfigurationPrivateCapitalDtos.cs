using System.Text.Json.Serialization;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;

namespace Meridian.Contracts.Ledger;

public sealed record PrivateCapitalFundEventDto(
    string FundEventId,
    string FundEventType,
    ManualJournalEntryTypeDto EntryType,
    ManualJournalEntryStatusDto JournalStatus,
    Guid JournalEntryId,
    DateOnly EffectiveDate,
    string CapitalAccountId,
    string? InvestorId,
    string Currency,
    decimal GrossAmount,
    decimal NetCapitalActivity,
    string Memo,
    string? PaymentIntentId,
    string? SettlementReference,
    IReadOnlyList<string> EvidenceLinks,
    IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues,
    DateTimeOffset UpdatedAtUtc,
    bool IsPosted = false,
    string? ApprovalId = null);

public sealed record PrivateCapitalCapitalAccountActivityDto(
    string CapitalAccountId,
    string? InvestorId,
    string Currency,
    decimal Contributions,
    decimal Distributions,
    decimal Subscriptions,
    decimal Redemptions,
    decimal ManagementFees,
    decimal NetActivity,
    int FundEventCount,
    DateOnly? LastEffectiveDate,
    string? LastFundEventType,
    IReadOnlyList<string> FundEventIds);

public sealed record PrivateCapitalCapitalAccountSubledgerEntryDto(
    string SubledgerEntryId,
    string CapitalAccountId,
    string? InvestorId,
    string Currency,
    string FundEventId,
    string FundEventType,
    ManualJournalEntryTypeDto EntryType,
    ManualJournalEntryStatusDto ApprovalState,
    Guid JournalEntryId,
    DateOnly EffectiveDate,
    decimal GrossAmount,
    decimal NetCapitalActivity,
    decimal RunningNetActivity,
    string Memo,
    IReadOnlyList<string> EvidenceLinks,
    IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues,
    DateTimeOffset UpdatedAtUtc,
    bool IsPosted = false);

public sealed record PrivateCapitalLedgerLineImpactDto(
    string LineId,
    string AccountPath,
    AccountingTemplateLineSideDto Side,
    decimal Amount,
    string Currency,
    string? EntityId,
    Guid? SecurityId,
    string? SecurityDisplayName,
    string? EvidenceLink);

public sealed record PrivateCapitalLedgerImpactDto(
    string LedgerImpactId,
    Guid JournalEntryId,
    string FundEventId,
    string FundEventType,
    string CapitalAccountId,
    string? InvestorId,
    ManualJournalEntryStatusDto ApprovalState,
    DateOnly EffectiveDate,
    string Currency,
    decimal TotalDebits,
    decimal TotalCredits,
    decimal Imbalance,
    int LineCount,
    bool IsBalanced,
    bool IsPostingReady,
    IReadOnlyList<string> EvidenceLinks,
    IReadOnlyList<PrivateCapitalLedgerLineImpactDto> Lines,
    IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues);

public sealed record PrivateCapitalReportOutputDto(
    string ReportOutputId,
    string ReportOutputType,
    string DisplayName,
    string ReportRoute,
    string FundEventId,
    string FundEventType,
    string CapitalAccountId,
    string? InvestorId,
    ManualJournalEntryStatusDto ApprovalState,
    DateOnly EffectiveDate,
    string Currency,
    decimal NetCapitalActivity,
    int EvidenceLinkCount,
    IReadOnlyList<string> EvidenceLinks,
    bool IsReportReady,
    IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues,
    bool IsPublished = false,
    string? ReportPackId = null,
    string? ReportWorkflowState = null,
    string? PublicationManifestId = null,
    string? RetainedManifestPath = null,
    string? PublicationEvidenceHash = null,
    DateTimeOffset? PublishedAtUtc = null,
    string? PublishedBy = null,
    int ReportLineProvenanceCount = 0,
    string? ReportOutputRoute = null,
    string? FundEventRecordRoute = null,
    string? CapitalAccountSubledgerRoute = null,
    string? EvidenceRoute = null,
    string? ApprovalRoute = null,
    string? ReadinessLabel = null,
    string? ReadinessReason = null,
    string? NextAction = null,
    string? NextActionRoute = null);

public sealed record PrivateCapitalEvidenceCategoryDto(
    string CategoryId,
    string Label,
    bool IsReady,
    string Summary,
    int EvidenceLinkCount,
    IReadOnlyList<string> EvidenceLinks,
    IReadOnlyList<string>? RequiredEvidence = null)
{
    public IReadOnlyList<string> RequiredEvidence { get; init; } =
        RequiredEvidence ?? [];
}

public sealed record PrivateCapitalPaymentIntentEvidenceDto(
    string? PaymentIntentId,
    string? SettlementReference,
    PrivateCapitalPaymentIntentEvidenceStatusDto Status,
    bool IsReady,
    PaymentIntentCashDirectionDto Direction,
    decimal Amount,
    string Currency,
    DateOnly EffectiveDate,
    string Summary,
    int CashEvidenceLinkCount,
    IReadOnlyList<string> CashEvidenceLinks,
    IReadOnlyList<string>? RequiredEvidence = null,
    string? EvidenceRoute = null)
{
    public IReadOnlyList<string> RequiredEvidence { get; init; } =
        RequiredEvidence ?? [];
}

public sealed record PaymentIntentExpectedCashMovementDto(
    string PaymentIntentId,
    PaymentIntentCashDirectionDto Direction,
    decimal Amount,
    string Currency,
    DateOnly EffectiveDate,
    string? SettlementReference,
    string? FundEventId,
    string? FundEventType,
    string? CapitalAccountId,
    string? InvestorId,
    string Purpose,
    string? Payee = null,
    string? AccountScope = null,
    string? BusinessPurpose = null,
    string? ApprovalPolicy = null,
    IReadOnlyList<string>? SourceEvidenceLinks = null)
{
    public IReadOnlyList<string> SourceEvidenceLinks { get; init; } =
        SourceEvidenceLinks ?? [];
}

public sealed record PaymentIntentApprovalStepDto(
    int Sequence,
    string Role,
    string Actor,
    string Status,
    DateTimeOffset? DecidedAtUtc = null,
    string? EvidenceRoute = null);

public sealed record PaymentIntentBankEvidenceDto(
    string EvidenceId,
    string EvidenceKind,
    string Status,
    string Summary,
    Guid? BankTransactionId = null,
    string? TransactionType = null,
    decimal? Amount = null,
    string? Currency = null,
    DateOnly? EffectiveDate = null,
    DateTimeOffset? RecordedAtUtc = null,
    string? ExternalRef = null,
    string? EvidenceRoute = null,
    string? RecordedBy = null);

public sealed record PaymentIntentReconciliationLinkDto(
    string LinkId,
    string Status,
    string Summary,
    string? EvidenceRoute = null,
    string? ReconciliationCaseId = null,
    string? ReconciliationRunId = null);

public sealed record PaymentIntentAuditEventDto(
    string AuditEventId,
    DateTimeOffset RecordedAtUtc,
    string Actor,
    string Action,
    string Summary,
    IReadOnlyList<string>? EvidenceLinks = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record PaymentIntentWorkflowDto(
    string PaymentIntentId,
    string? SettlementReference,
    string FundProfileId,
    Guid? LedgerBookId,
    string FundEventId,
    Guid JournalEntryId,
    string Requester,
    DateTimeOffset RequestedAtUtc,
    PaymentIntentWorkflowStatusDto Status,
    string StatusLabel,
    string ReadinessReason,
    string ExecutionDeferredReason,
    PaymentIntentExpectedCashMovementDto ExpectedCashMovement,
    string EvidenceRoute,
    string WorkbenchRoute,
    IReadOnlyList<PaymentIntentApprovalStepDto>? ApprovalChain = null,
    IReadOnlyList<PaymentIntentBankEvidenceDto>? BankEvidence = null,
    IReadOnlyList<PaymentIntentReconciliationLinkDto>? ReconciliationLinks = null,
    IReadOnlyList<PaymentIntentAuditEventDto>? AuditHistory = null)
{
    public IReadOnlyList<PaymentIntentApprovalStepDto> ApprovalChain { get; init; } =
        ApprovalChain ?? [];

    public IReadOnlyList<PaymentIntentBankEvidenceDto> BankEvidence { get; init; } =
        BankEvidence ?? [];

    public IReadOnlyList<PaymentIntentReconciliationLinkDto> ReconciliationLinks { get; init; } =
        ReconciliationLinks ?? [];

    public IReadOnlyList<PaymentIntentAuditEventDto> AuditHistory { get; init; } =
        AuditHistory ?? [];
}

public sealed record PrivateCapitalFundEventLedgerRecordDto(
    string FundEventRecordId,
    string FundEventId,
    string FundEventType,
    string CapitalAccountId,
    string? InvestorId,
    ManualJournalEntryStatusDto ApprovalState,
    Guid JournalEntryId,
    DateOnly EffectiveDate,
    string Currency,
    decimal GrossAmount,
    decimal NetCapitalActivity,
    decimal CapitalAccountOpeningNetActivity,
    decimal CapitalAccountEndingNetActivity,
    string Memo,
    string? PaymentIntentId,
    string? SettlementReference,
    string ActivityRoute,
    string EvidenceRoute,
    string? ApprovalId,
    string? ApprovalRoute,
    bool IsPosted,
    bool IsPostingReady,
    bool IsReportReady,
    bool IsPublished,
    PrivateCapitalFundEventLedgerReadinessDto Readiness,
    string ReadinessLabel,
    string ReadinessReason,
    string NextAction,
    string? NextActionRoute,
    int EvidenceLinkCount,
    int CapitalAccountSubledgerEntryCount,
    int LedgerImpactCount,
    int ReportOutputCount,
    int ValidationIssueCount,
    string? PrimaryReportOutputId,
    string? PrimaryReportOutputType,
    string? PrimaryReportRoute,
    string? ReportWorkflowState,
    string? PublicationManifestId,
    string? RetainedManifestPath,
    int ReportLineProvenanceCount,
    IReadOnlyList<string> EvidenceLinks,
    PrivateCapitalFundEventDto FundEvent,
    IReadOnlyList<PrivateCapitalCapitalAccountSubledgerEntryDto> CapitalAccountSubledgerEntries,
    IReadOnlyList<PrivateCapitalLedgerImpactDto> LedgerImpacts,
    IReadOnlyList<PrivateCapitalReportOutputDto> ReportOutputs,
    IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues,
    IReadOnlyList<PrivateCapitalEvidenceCategoryDto>? EvidenceCategories = null,
    PrivateCapitalPaymentIntentEvidenceDto? PaymentIntentEvidence = null)
{
    public IReadOnlyList<PrivateCapitalEvidenceCategoryDto> EvidenceCategories { get; init; } =
        EvidenceCategories ?? [];
}

public sealed record PrivateCapitalFundEventCommandCenterLaneDto(
    string LaneId,
    string Label,
    string Status,
    bool IsReady,
    string Summary,
    string? Route,
    int EvidenceLinkCount,
    IReadOnlyList<string> EvidenceLinks,
    IReadOnlyList<string>? RequiredActions = null)
{
    public IReadOnlyList<string> RequiredActions { get; init; } =
        RequiredActions ?? [];
}

public sealed record PrivateCapitalFundEventCommandCenterSupportPackageDto(
    string PackageId,
    string Label,
    string Status,
    string? Route,
    int EvidenceLinkCount,
    IReadOnlyList<string> EvidenceLinks,
    IReadOnlyList<string>? RequiredActions = null)
{
    public IReadOnlyList<string> RequiredActions { get; init; } =
        RequiredActions ?? [];
}

public sealed record PrivateCapitalFundEventCommandCenterDto(
    string FundEventId,
    string FundEventType,
    string FundProfileId,
    Guid? LedgerBookId,
    DateTimeOffset ProjectedAtUtc,
    string CommandCenterRoute,
    PrivateCapitalFundEventLedgerReadinessDto Readiness,
    string ReadinessLabel,
    string ReadinessReason,
    string NextAction,
    string? NextActionRoute,
    int ReadyLaneCount,
    int BlockedLaneCount,
    PrivateCapitalFundEventLedgerRecordDto FundEventRecord,
    IReadOnlyList<PrivateCapitalFundEventCommandCenterLaneDto> Lanes,
    IReadOnlyList<PrivateCapitalFundEventCommandCenterSupportPackageDto> SupportPackages,
    IReadOnlyList<string> LiveCapabilities,
    IReadOnlyList<string> PlannedCapabilities);

public sealed record PrivateCapitalCapitalAccountSubledgerDto(
    string SubledgerId,
    string FundProfileId,
    Guid? LedgerBookId,
    DateTimeOffset ProjectedAtUtc,
    string CapitalAccountId,
    string? InvestorId,
    string Currency,
    string ActivityRoute,
    decimal Contributions,
    decimal Distributions,
    decimal Subscriptions,
    decimal Redemptions,
    decimal ManagementFees,
    decimal OpeningNetActivity,
    decimal EndingNetActivity,
    decimal NetCapitalActivity,
    int FundEventCount,
    int ApprovalQueueCount,
    int PostedFundEventCount,
    int PublishedReportOutputCount,
    int EvidenceLinkCount,
    int ValidationIssueCount,
    DateOnly? FirstEffectiveDate,
    DateOnly? LastEffectiveDate,
    string? LastFundEventType,
    IReadOnlyList<string> EvidenceLinks,
    PrivateCapitalCapitalAccountActivityDto? CapitalAccount,
    IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> FundEventRecords,
    IReadOnlyList<PrivateCapitalCapitalAccountSubledgerEntryDto> SubledgerEntries,
    IReadOnlyList<PrivateCapitalLedgerImpactDto> LedgerImpacts,
    IReadOnlyList<PrivateCapitalReportOutputDto> ReportOutputs,
    IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues,
    PrivateCapitalFundEventLedgerReadinessDto Readiness = PrivateCapitalFundEventLedgerReadinessDto.EvidenceMissing,
    string ReadinessLabel = "",
    string ReadinessReason = "",
    string NextAction = "",
    string? NextActionRoute = null,
    IReadOnlyList<PrivateCapitalEvidenceCategoryDto>? EvidenceCategories = null,
    PrivateCapitalPaymentIntentEvidenceDto? PaymentIntentEvidence = null)
{
    public IReadOnlyList<PrivateCapitalEvidenceCategoryDto> EvidenceCategories { get; init; } =
        EvidenceCategories ?? [];
}

public sealed record PrivateCapitalActivityProjectionDto(
    string FundProfileId,
    Guid? LedgerBookId,
    DateTimeOffset ProjectedAtUtc,
    int FundEventCount,
    int CapitalAccountCount,
    int SubmittedFundEventCount,
    int ApprovalQueueCount,
    int PostedFundEventCount,
    int PublishedReportOutputCount,
    decimal NetCapitalActivity,
    string Currency,
    IReadOnlyList<PrivateCapitalFundEventDto> FundEvents,
    IReadOnlyList<PrivateCapitalCapitalAccountActivityDto> CapitalAccounts,
    IReadOnlyList<PrivateCapitalCapitalAccountSubledgerEntryDto> CapitalAccountSubledgerEntries,
    IReadOnlyList<PrivateCapitalLedgerImpactDto> LedgerImpacts,
    IReadOnlyList<PrivateCapitalReportOutputDto> ReportOutputs,
    IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues,
    IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto>? FundEventRecords = null,
    IReadOnlyList<PrivateCapitalCapitalAccountSubledgerDto>? CapitalAccountSubledgers = null,
    IReadOnlyList<PaymentIntentWorkflowDto>? PaymentIntents = null)
{
    public IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> FundEventRecords { get; init; } =
        FundEventRecords ?? [];

    public IReadOnlyList<PrivateCapitalCapitalAccountSubledgerDto> CapitalAccountSubledgers { get; init; } =
        CapitalAccountSubledgers ?? [];

    public IReadOnlyList<PaymentIntentWorkflowDto> PaymentIntents { get; init; } =
        PaymentIntents ?? [];
}

public sealed record CapitalAccountWorkbenchInvestorAccountDto(
    string AccountKey,
    string CapitalAccountId,
    string? InvestorId,
    string Currency,
    string ActivityRoute,
    PrivateCapitalFundEventLedgerReadinessDto Readiness,
    string ReadinessLabel,
    string ReadinessReason,
    string NextAction,
    string? NextActionRoute,
    decimal OpeningNetActivity,
    decimal EndingNetActivity,
    decimal NetCapitalActivity,
    decimal Contributions,
    decimal Distributions,
    decimal Subscriptions,
    decimal Redemptions,
    decimal ManagementFees,
    int FundEventCount,
    int PostedFundEventCount,
    int ApprovalQueueCount,
    int PublishedReportOutputCount,
    int EvidenceLinkCount,
    int ValidationIssueCount,
    string EvidenceCategorySummary,
    IReadOnlyList<string> EvidenceLinks,
    IReadOnlyList<PrivateCapitalEvidenceCategoryDto> EvidenceCategories,
    IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> FundEventRecords,
    IReadOnlyList<PrivateCapitalCapitalAccountSubledgerEntryDto> SubledgerEntries,
    IReadOnlyList<PrivateCapitalLedgerImpactDto> LedgerImpacts,
    IReadOnlyList<PrivateCapitalReportOutputDto> ReportOutputs,
    IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues,
    PrivateCapitalPaymentIntentEvidenceDto? PaymentIntentEvidence = null);

public sealed record CapitalAccountWorkbenchAllocationRuleDto(
    string RuleId,
    string CapitalAccountId,
    string? InvestorId,
    string CategoryId,
    string Label,
    string Basis,
    bool IsSatisfied,
    string Reason,
    string? Route,
    int EvidenceLinkCount,
    IReadOnlyList<string> EvidenceLinks,
    IReadOnlyList<string> RequiredEvidence)
{
    public string RuleVersion { get; init; } = "";
    public DateOnly? EffectiveFrom { get; init; }
    public DateOnly? EffectiveTo { get; init; }
    public string Formula { get; init; } = "";
    public string ApprovalState { get; init; } = "";
    public string? ApprovalReference { get; init; }
    public string ReplayTrace { get; init; } = "";
    public IReadOnlyList<CapitalAccountWorkbenchAllocationInputDto> Inputs { get; init; } = [];
    public IReadOnlyList<string> RelatedFundEventIds { get; init; } = [];
}

public sealed record CapitalAccountWorkbenchAllocationInputDto(
    string InputId,
    string Kind,
    string SourceId,
    string Label,
    decimal? Amount,
    string? Currency,
    DateOnly? EffectiveDate,
    string? EvidenceRoute);

public sealed record CapitalAccountWorkbenchStatementLineageDto(
    string LineageId,
    string CapitalAccountId,
    string? InvestorId,
    string ReportOutputId,
    string ReportOutputType,
    string DisplayName,
    string ReportRoute,
    string? ReportPackId,
    string? ReportWorkflowState,
    bool IsPublished,
    bool IsReportReady,
    string? PublicationManifestId,
    string? RetainedManifestPath,
    string? PublicationEvidenceHash,
    DateTimeOffset? PublishedAtUtc,
    string? PublishedBy,
    int ReportLineProvenanceCount,
    bool HasRestatementLineage,
    string RestatementStatus,
    string? RestatementReasonCode,
    Guid? RestatementPriorVersionReportId,
    string? RestatementApprover,
    int RestatementChangedLineCount,
    int RestatementEvidenceLinkCount,
    string? ReportOutputRoute,
    string? EvidenceRoute,
    string? CapitalAccountSubledgerRoute,
    IReadOnlyList<string> EvidenceLinks,
    IReadOnlyList<string> RestatementEvidenceLinks)
{
    public IReadOnlyList<CapitalAccountWorkbenchRestatementChangedLineDto> RestatementChangedLines { get; init; } = [];
}

public sealed record CapitalAccountWorkbenchRestatementChangedLineDto(
    string LineKey,
    string PreviousValue,
    string CurrentValue,
    int EvidenceLinkCount,
    IReadOnlyList<string> EvidenceLinks);

public sealed record CapitalAccountWorkbenchAuditDrillThroughDto(
    string DrillThroughId,
    string Kind,
    string Label,
    string Summary,
    string? Route,
    bool IsAvailable,
    int EvidenceLinkCount,
    IReadOnlyList<string> EvidenceLinks,
    IReadOnlyList<string> RelatedIds);

public sealed record CapitalAccountWorkbenchDto(
    string FundProfileId,
    Guid? LedgerBookId,
    DateTimeOffset ProjectedAtUtc,
    string? CapitalAccountId,
    string? InvestorId,
    string Currency,
    string WorkbenchRoute,
    string StatusLabel,
    string StatusReason,
    int InvestorAccountCount,
    int FundEventCount,
    int StatementCount,
    int RestatementLineageCount,
    int AuditDrillThroughCount,
    decimal NetCapitalActivity,
    IReadOnlyList<CapitalAccountWorkbenchInvestorAccountDto> InvestorAccounts,
    IReadOnlyList<CapitalAccountWorkbenchAllocationRuleDto> AllocationRules,
    IReadOnlyList<CapitalAccountWorkbenchStatementLineageDto> StatementLineage,
    IReadOnlyList<CapitalAccountWorkbenchAuditDrillThroughDto> AuditDrillThroughs,
    IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues,
    IReadOnlyList<string>? LiveCapabilities = null,
    IReadOnlyList<string>? PlannedCapabilities = null)
{
    public IReadOnlyList<string> LiveCapabilities { get; init; } =
        LiveCapabilities ?? [];

    public IReadOnlyList<string> PlannedCapabilities { get; init; } =
        PlannedCapabilities ?? [];
}
