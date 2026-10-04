using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Ledger;

namespace Meridian.ProviderSdk.AccountingSystem;

/// <summary>Provider-owned checks for retained review artifacts. This contract cannot post journals.</summary>
public interface IAccountingSystemExportValidator
{
    Task<IReadOnlyList<AccountingConfigurationValidationIssueDto>> ValidateExportAsync(
        AccountingSystemExportValidationContext context,
        CancellationToken ct = default);
}

public sealed record AccountingSystemExportValidationContext(
    Guid? LedgerBookId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    AccountingSystemImportDetailDto? Import,
    IReadOnlyList<ExternalGlExportLineDto> Lines,
    IReadOnlyList<string> EvidenceLinks);
