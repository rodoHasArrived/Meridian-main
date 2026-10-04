using System.Globalization;
using System.Text;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;

namespace Meridian.Instruments.AssetOperations;

public sealed partial class CorporateActionAccountingProjectionService
{
    private static string BuildEventIdentity(CorporateActionAccountingProjectionRequest request)
        => string.Join(
            '|',
            request.SourceCorporateActionId.ToString("N"),
            request.SourceEventVersion.ToString(CultureInfo.InvariantCulture),
            request.CaseId?.ToString("N") ?? "-",
            request.SecurityId.ToString("N"),
            request.PositionId.ToString("N"),
            request.AccountingScope?.LedgerBookId.ToString("N") ?? "-",
            request.ActionType.ToString(),
            request.AccountingBasis.ToString(),
            request.EffectiveDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            request.SourceContentHash.Trim());

    /// <summary>
    /// Fingerprints every dependency that can stale a case projection. The source event identity
    /// remains stable, while the projection run changes with case/election/policy/position/economic
    /// or retained-evidence inputs. Length-prefixed tokens avoid delimiter ambiguity.
    /// </summary>
    private static string BuildProjectionInputHash(
        CorporateActionAccountingProjectionRequest request,
        CorporateActionTreatmentDecisionDto decision,
        IReadOnlyList<CorporateActionProjectionEvidenceDependencyDto> evidence,
        string currency)
    {
        var builder = new StringBuilder(1024);
        AppendToken(builder, ClearwaterCorporateActionRuleProfileV1.ProfileKey);
        AppendToken(builder, ClearwaterCorporateActionRuleProfileV1.ProfileVersion);
        AppendToken(builder, ClearwaterCorporateActionRuleProfileV1.SelectedRuleVersion);
        AppendToken(builder, ModelKey);
        AppendToken(builder, ModelVersion);
        AppendToken(builder, EngineVersion);
        AppendToken(builder, decision.RuleProfile.RulePackId);
        AppendToken(builder, decision.RuleProfile.RulePackVersion);
        AppendToken(builder, decision.RuleProfile.SelectedRuleId);
        AppendToken(builder, decision.RuleProfile.SelectedRuleVersion);
        AppendToken(builder, request.SourceCorporateActionId.ToString("N"));
        AppendToken(builder, Invariant(request.SourceEventVersion));
        AppendToken(builder, request.CaseId?.ToString("N"));
        AppendToken(builder, Invariant(request.CaseVersion));
        AppendToken(builder, Invariant(request.ElectionVersion));
        AppendToken(builder, Invariant(request.PolicyDecisionVersion));
        AppendToken(builder, request.PositionSnapshotId?.ToString("N"));
        AppendToken(builder, request.LotSnapshotId?.ToString("N"));
        AppendToken(builder, Invariant(request.LotSnapshotVersion));
        AppendToken(builder, request.PolicyDecisionId?.ToString("N"));
        AppendToken(builder, request.ElectionId?.ToString("N"));
        AppendToken(builder, request.SecurityId.ToString("N"));
        AppendToken(builder, request.PositionId.ToString("N"));
        AppendToken(builder, Invariant(request.PositionVersion));
        AppendToken(builder, Invariant(request.ExpectedPositionVersion));
        AppendToken(builder, request.ActionType.ToString());
        AppendToken(builder, request.AccountingBasis.ToString());
        AppendToken(builder, request.EffectiveDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        AppendToken(builder, request.RuleProfileAsOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        AppendToken(builder, currency);
        AppendToken(builder, request.SourceDomain?.Trim());
        AppendToken(builder, request.SourceEntityId?.Trim());
        AppendToken(builder, request.SourceContentHash?.Trim());
        AppendToken(builder, request.AccountingScope?.TenantId.Trim());
        AppendToken(builder, request.AccountingScope?.CompanyId.Trim());
        AppendToken(builder, request.AccountingScope?.FundProfileId.Trim());
        AppendToken(builder, request.AccountingScope?.LedgerBookId.ToString("N"));
        AppendToken(builder, request.AccountingScope?.PeriodId.ToString("N"));
        AppendToken(builder, Invariant(request.AccountingScope?.ExpectedPeriodVersion));
        AppendToken(builder, request.AccountingScope?.Jurisdiction.Trim());

        foreach (var mutation in request.AuthoritativeLotMutations)
        {
            AppendLotMutationTokens(builder, mutation);
        }

        var economics = request.Economics;
        AppendToken(builder, Invariant(economics.PositionQuantity));
        AppendToken(builder, Invariant(economics.AffectedQuantity));
        AppendToken(builder, Invariant(economics.CarryingAmount));
        AppendToken(builder, Invariant(economics.ParAmount));
        AppendToken(builder, Invariant(economics.GrossCashConsideration));
        AppendToken(builder, Invariant(economics.AccruedIncome));
        AppendToken(builder, Invariant(economics.Rate));
        AppendToken(builder, Invariant(economics.CashRatePerUnit));
        AppendToken(builder, Invariant(economics.SplitRatio));
        AppendToken(builder, Invariant(economics.DistributionRatio));
        AppendToken(builder, Invariant(economics.PurchasePricePerUnit));
        AppendToken(builder, Invariant(economics.SubscriptionPricePerUnit));
        AppendToken(builder, economics.IdentifierChanged ? "1" : "0");
        AppendToken(builder, economics.IsMakeWhole ? "1" : "0");
        AppendToken(builder, economics.IsPartial ? "1" : "0");
        foreach (var successor in economics.Successors
                     .OrderBy(static successor => successor.SecurityId)
                     .ThenBy(static successor => successor.Role)
                     .ThenBy(static successor => successor.Quantity)
                     .ThenBy(static successor => successor.BookValueAllocationPercent)
                     .ThenBy(static successor => successor.FairValue))
        {
            AppendToken(builder, successor.SecurityId.ToString("N"));
            AppendToken(builder, successor.Role.ToString());
            AppendToken(builder, Invariant(successor.Quantity));
            AppendToken(builder, Invariant(successor.BookValueAllocationPercent));
            AppendToken(builder, Invariant(successor.FairValue));
        }

        var policy = request.PolicyInputs;
        AppendToken(builder, policy.BankruptcyMethod?.ToString());
        AppendToken(builder, policy.ExchangeOfferMethod?.ToString());
        AppendToken(builder, policy.CashRecognition?.ToString());
        AppendToken(builder, policy.MergerRecognition?.ToString());
        AppendToken(builder, policy.SpinOffTaxTreatment?.ToString());
        AppendToken(builder, policy.StockDividendBasisTreatment?.ToString());
        AppendToken(builder, policy.ApprovedTaxClassification?.ToString());
        AppendToken(builder, policy.ExchangeOfferIsMaterial?.ToString());
        AppendToken(builder, policy.StatutoryConversionTreatmentApproved ? "1" : "0");
        AppendToken(builder, policy.ScripDividendTreatmentApproved ? "1" : "0");
        AppendToken(builder, policy.RightsZeroValueApproved ? "1" : "0");
        AppendToken(builder, policy.CarryHoldingPeriod?.ToString());
        AppendToken(builder, Invariant(policy.StatutoryTenderIncomeAllocationPercent));
        AppendToken(builder, policy.ConsentTermsChanged?.ToString());
        AppendToken(builder, policy.ConsentModificationAssessmentApproved ? "1" : "0");
        AppendToken(builder, Invariant(policy.ApprovedCashRecognitionAmount));
        AppendToken(builder, Invariant(policy.ApprovedSuccessorBasis));
        AppendToken(builder, policy.ScripFinalDistributionCaseId?.ToString("N"));
        AppendToken(builder, policy.FractionalCashInLieuCaseId?.ToString("N"));

        foreach (var item in evidence
                     .OrderBy(static item => item.Role)
                     .ThenBy(static item => item.EvidenceId, StringComparer.Ordinal)
                     .ThenBy(static item => item.EvidenceVersion))
        {
            AppendToken(builder, item.Role.ToString());
            AppendToken(builder, item.EvidenceId.Trim());
            AppendToken(builder, item.EvidenceUri.Trim());
            AppendToken(builder, item.ContentHashSha256.Trim());
            AppendToken(builder, Invariant(item.EvidenceVersion));
            AppendToken(builder, item.SubjectType.Trim());
            AppendToken(builder, item.SubjectId.Trim());
        }

        return Sha256Digest.ComputeUtf8(builder.ToString());
    }

    private static string BuildPostingIntentHash(
        string projectionInputHash,
        CorporateActionTreatmentDecisionDto decision,
        ProjectionComputation computation,
        string currency)
    {
        var builder = new StringBuilder(1024);
        AppendToken(builder, "corporate-action-posting-intent/v1");
        AppendToken(builder, projectionInputHash);
        AppendToken(builder, decision.RuleProfile.RulePackId);
        AppendToken(builder, decision.RuleProfile.RulePackVersion);
        AppendToken(builder, decision.RuleProfile.SelectedRuleId);
        AppendToken(builder, decision.RuleProfile.SelectedRuleVersion);
        AppendToken(builder, decision.ActionType.ToString());
        AppendToken(builder, decision.AccountingBasis.ToString());
        AppendToken(builder, currency);
        AppendToken(builder, Invariant(computation.EventAmount));
        AppendToken(builder, computation.RequiresJournalCandidate ? "1" : "0");
        foreach (var operation in computation.Recipe)
        {
            AppendToken(builder, operation.Kind.ToString());
            AppendToken(builder, operation.SecurityId?.ToString("N"));
            AppendToken(builder, operation.SuccessorRole?.ToString());
            AppendToken(builder, Invariant(operation.Quantity));
            AppendToken(builder, Invariant(operation.Amount));
            AppendToken(builder, operation.Description);
            AppendToken(builder, operation.LinkedCaseId?.ToString("N"));
        }

        foreach (var mutation in computation.LotMutations)
        {
            AppendLotMutationTokens(builder, mutation);
        }

        for (var index = 0; index < computation.PostingComponents.Count; index++)
        {
            var component = computation.PostingComponents[index];
            AppendToken(builder, Invariant(index));
            AppendToken(builder, component.Kind.ToString());
            AppendToken(builder, Invariant(component.Amount));
            AppendToken(builder, component.Currency);
            AppendToken(builder, component.Description);
        }

        return Sha256Digest.ComputeUtf8(builder.ToString());
    }

    private static void AppendLotMutationTokens(
        StringBuilder builder,
        CorporateActionLotMutationDto mutation)
    {
        AppendToken(builder, mutation.Kind.ToString());
        AppendToken(builder, mutation.SecurityId.ToString("N"));
        AppendToken(builder, mutation.TargetSecurityId?.ToString("N"));
        AppendToken(builder, Invariant(mutation.Quantity));
        AppendToken(builder, Invariant(mutation.CarryingAmount));
        AppendToken(builder, Invariant(mutation.BasisAmount));
        AppendToken(builder, Invariant(mutation.AllocationPercent));
        AppendToken(builder, mutation.HoldingPeriodTreatment.ToString());
        AppendToken(builder, mutation.Description);
        AppendToken(builder, mutation.LinkedCaseId?.ToString("N"));
        AppendToken(builder, mutation.SourceLotId?.ToString("N"));
        AppendToken(builder, Invariant(mutation.ExpectedSourceLotVersion));
        AppendToken(builder, Invariant(mutation.SourceQuantity));
        AppendToken(builder, Invariant(mutation.SourceCarryingAmount));
        AppendToken(builder, Invariant(mutation.SourceBasisAmount));
        AppendLotStateTokens(builder, mutation.SourceBefore);
        AppendLotStateTokens(builder, mutation.SourceAfter);
        AppendToken(builder, mutation.TargetLotId?.ToString("N"));
        AppendToken(builder, mutation.TargetOperation?.ToString());
        AppendToken(builder, Invariant(mutation.ExpectedTargetLotVersion));
        AppendLotStateTokens(builder, mutation.TargetBefore);
        AppendLotStateTokens(builder, mutation.TargetAfter);
        foreach (var reportingTag in mutation.ReportingTags.OrderBy(static tag => tag, StringComparer.Ordinal))
        {
            AppendToken(builder, reportingTag);
        }
    }

    private static void AppendLotStateTokens(
        StringBuilder builder,
        CorporateActionLotStateSnapshotDto? snapshot)
    {
        AppendToken(builder, Invariant(snapshot?.Quantity));
        AppendToken(builder, Invariant(snapshot?.CarryingValue));
        AppendToken(builder, Invariant(snapshot?.BasisAmount));
    }

    private static string? Invariant(long? value) =>
        value?.ToString(CultureInfo.InvariantCulture);

    private static string? Invariant(decimal? value) =>
        value?.ToString("G29", CultureInfo.InvariantCulture);

    private static void AppendToken(StringBuilder builder, string? value)
    {
        var token = value ?? "<null>";
        builder.Append(token.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(token);
        builder.Append(';');
    }

    private static Guid DeterministicGuid(string value)
    {
        var hash = Sha256Digest.ComputeBytesUtf8(value);
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes);
    }

    private static bool IsSupportedCurrency(string currency)
        => currency.Length == 3 && currency.All(static character => character is >= 'A' and <= 'Z');

    private static decimal Round(decimal amount, string currency)
        => decimal.Round(amount, CurrencyMinorUnits(currency), MidpointRounding.AwayFromZero);

    private static int CurrencyMinorUnits(string currency)
        => currency switch
        {
            "BHD" or "IQD" or "JOD" or "KWD" or "LYD" or "OMR" or "TND" => 3,
            "BIF" or "CLP" or "DJF" or "GNF" or "ISK" or "JPY" or "KMF" or "KRW" or "PYG" or
                "RWF" or "UGX" or "UYI" or "VND" or "VUV" or "XAF" or "XOF" or "XPF" => 0,
            _ => 2
        };

    private sealed record ProjectionComputation(
        decimal EventAmount,
        IReadOnlyList<CorporateActionEconomicOperationDto> Recipe,
        IReadOnlyList<CorporateActionLotMutationDto> LotMutations,
        IReadOnlyList<CorporateActionPostingComponentDto> PostingComponents,
        bool RequiresJournalCandidate)
    {
        public static ProjectionComputation Empty { get; } = new(0m, [], [], [], false);
    }
}
