using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;

namespace Meridian.Ledger;

public enum LedgerInventoryAccountRole
{
    NotInventory,
    LotCarrying,
    KnownNonLotAsset,
    UnknownInstrumentAsset
}

/// <summary>
/// Separates share inventory from source-proven receivables and loan subledgers. Unknown instrument
/// assets remain unresolved; changing an account caption never authorizes missing-lot coverage.
/// </summary>
public static class LedgerInventoryAccountRoleClassifier
{
    public static LedgerInventoryAccountRole Classify(LedgerEntry line, JournalEntryMetadata metadata,
        IReadOnlySet<LedgerAccount> historicalLotAccounts)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(historicalLotAccounts);
        if (historicalLotAccounts.Contains(line.Account))
            return LedgerInventoryAccountRole.LotCarrying;
        if (line.Account.AccountType != LedgerAccountType.Asset)
            return LedgerInventoryAccountRole.NotInventory;
        if (IsRetainedNativeDividendReceivable(line, metadata))
            return LedgerInventoryAccountRole.KnownNonLotAsset;
        if (IsRetainedDirectLendingAsset(line, metadata))
            return LedgerInventoryAccountRole.KnownNonLotAsset;
        var dividendKind = metadata.ActivityType is nameof(AutomatedJournalEventKind.DividendDeclared)
                or nameof(AutomatedJournalEventKind.DividendReceived)
            || CorporateActionTypeDescriptorCatalog.TryNormalize(metadata.ActivityType, out var action)
                && action.LedgerPostingKind == CorporateActionLedgerPostingKind.DividendDeclaration;
        var dividendSource = metadata.LedgerView == LedgerViewKind.SecurityMaster
            || metadata.EvidenceReferences.Any(reference => reference.Kind == "Source"
                && (reference.SourceSystem is "SecurityMaster" or "CorporateActions" or "CorporateAction"
                    || reference.Uri.StartsWith("corporate-action://", StringComparison.Ordinal)
                    || reference.Uri.StartsWith("security-master://corporate-actions/", StringComparison.Ordinal)));
        if (line.Account.Name == "Dividend Receivable" && dividendKind && dividendSource)
            return LedgerInventoryAccountRole.KnownNonLotAsset;
        if (line.Account.Name == "Securities")
            return LedgerInventoryAccountRole.LotCarrying;
        return !string.IsNullOrWhiteSpace(line.Account.Symbol)
            ? LedgerInventoryAccountRole.UnknownInstrumentAsset : LedgerInventoryAccountRole.NotInventory;
    }

    // Classification only: a persisted native preparer receipt explains its exact non-lot
    // receivable role. It remains preparer attestation and confers no independent approval.
    private static bool IsRetainedNativeDividendReceivable(LedgerEntry line, JournalEntryMetadata metadata)
    {
        if (metadata.PreparerAttestation is not { Receipt.AccountRoleBinding.SchemaVersion: 1 } binding)
            return false;
        var receipt = binding.Receipt;
        var roles = receipt.AccountRoleBinding!;
        if (roles.LedgerBookId != binding.LedgerBookId || line.Account.Name != roles.DividendReceivableAccountName
            || metadata.LedgerBook != binding.LedgerBookId.ToString("D")
            || line.Dimensions?.BookId != binding.LedgerBookId.ToString("D")
            || metadata.EffectiveDate != receipt.ExDate || line.Credit != 0m || line.Debit != receipt.GrossImpactUsd
            || line.Currency is not { TransactionCurrency: "USD", FunctionalCurrency: "USD", FxRateToFunctional: 1m })
            return false;
        if (AssetAccountingPreparerAttestationDto.Validate(binding, binding.LedgerBookId, binding.PeriodId,
            binding.EventId, binding.EventVersion, binding.TenantId, binding.CompanyId, receipt.AttestedBy,
            "USD", line.Debit).Count != 0)
            return false;
        return metadata.EvidenceReferences.Any(reference => reference.Kind == "Source"
            && reference.SubjectId == binding.EventId.ToString("D")
            && reference.SourceReference == receipt.SourceEventId
            && reference.EffectiveDate == receipt.ExDate
            && Sha256Digest.FixedEquals(reference.ContentHash, receipt.SourceEvidenceHash));
    }

    private static bool IsRetainedDirectLendingAsset(LedgerEntry line, JournalEntryMetadata metadata)
    {
        var activity = metadata.ActivityType;
        if (metadata.Institution != "DirectLending" || activity is null || !activity.StartsWith("loan.", StringComparison.Ordinal)
            || metadata.Tags is null || !metadata.Tags.TryGetValue("loanId", out var loanId)
            || !metadata.Tags.TryGetValue("sourceEventType", out var eventType) || eventType != activity
            || !Guid.TryParse(loanId, out var parsedLoan) || parsedLoan == Guid.Empty
            || !string.Equals(line.Account.FinancialAccountId, loanId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(metadata.FinancialAccountId, loanId, StringComparison.OrdinalIgnoreCase))
            return false;
        return metadata.EvidenceReferences.Any(reference => reference.Kind == "Source"
            && reference.SourceSystem == "DirectLending" && reference.SubjectId == loanId
            && reference.Uri.StartsWith("direct-lending://events/", StringComparison.Ordinal));
    }
}
