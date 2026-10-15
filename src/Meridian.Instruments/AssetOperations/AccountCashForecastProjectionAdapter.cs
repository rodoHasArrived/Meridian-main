using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;

namespace Meridian.Instruments.AssetOperations;

/// <summary>Adapts existing obligation/factor economics while requiring explicit account and cash dates.</summary>
public static class AccountCashForecastProjectionAdapter
{
    public static AccountCashFlowDto FromObligation(
        AccountCashForecastScopeDto scope,
        string stableFlowId,
        AssetProjectedCashFlowDto flow,
        AssetCashFlowProjectionRunDto run,
        AssetTermsVersionDto terms,
        decimal heldQuantity,
        DateOnly settlementDate,
        DateOnly cashReceiptDate,
        AccountCashProvenanceDto provenance,
        string? supersedesVersionId = null)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(provenance);
        if (flow.SecurityId == Guid.Empty || flow.ProjectedCashFlowId == Guid.Empty || run.ProjectionRunId == Guid.Empty
            || terms.TermsVersionId == Guid.Empty || flow.SecurityId != run.SecurityId || flow.SecurityId != terms.SecurityId
            || flow.ProjectionRunId != run.ProjectionRunId || heldQuantity == 0m
            || !string.Equals(run.Status, "Completed", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(run.EngineVersion) || string.IsNullOrWhiteSpace(terms.TermsHash)
            || run.GeneratedAt == default || run.GeneratedAt.Offset != TimeSpan.Zero
            || terms.RecordedAt == default || terms.RecordedAt.Offset != TimeSpan.Zero
            || provenance.SourceDomain != (flow.SourceDomain ?? run.SourceDomain)
            || provenance.SourceEntityId != (flow.SourceEntityId ?? run.SourceEntityId))
            throw new ArgumentException("A held obligation must retain consistent Security Master, projection run and terms identities.", nameof(flow));
        return new AccountCashFlowDto(stableFlowId, flow.ProjectedCashFlowId.ToString("D"), scope.AccountId, scope.BookId,
            AccountCashFlowStatus.Expected, flow.FlowType, PortfolioCashLadderEngine.GetSignedInstrumentAmount(flow, heldQuantity),
            flow.Currency, flow.DueDate, settlementDate, cashReceiptDate, provenance,
            flow.SecurityId, run.ProjectionRunId, run.EngineVersion, terms.TermsVersionId, terms.TermsHash, supersedesVersionId);
    }

    public static AccountCashFlowDto FromFactorPaydown(
        AccountCashForecastScopeDto scope,
        string stableFlowId,
        FactorPaydownProjectionRequest factor,
        DateOnly settlementDate,
        DateOnly cashReceiptDate,
        AccountCashProvenanceDto provenance,
        string? supersedesVersionId = null)
    {
        ArgumentNullException.ThrowIfNull(factor);
        ArgumentNullException.ThrowIfNull(provenance);
        var result = new FactorPaydownProjectionService().Project(factor);
        if (!result.ProducesPostingCandidate || result.EconomicEvent is null || result.Lineage is null
            || provenance.SourceDomain != factor.SourceDomain || provenance.SourceEntityId != factor.SourceEntityId
            || !Sha256Digest.FixedEquals(provenance.SourceContentHash, factor.SourceContentHash))
            throw new ArgumentException("A valid retained factor-paydown projection and matching source evidence are required.", nameof(factor));
        return new AccountCashFlowDto(stableFlowId, result.EconomicEvent.EventId.ToString("D"), scope.AccountId, scope.BookId,
            AccountCashFlowStatus.Expected, "FactorPaydown", result.PrincipalPaydown!.Value, factor.Currency,
            factor.EffectiveDate, settlementDate, cashReceiptDate, provenance, factor.SecurityId,
            result.Lineage.ProjectionRunId, FactorPaydownProjectionService.EngineVersion,
            TermsHash: factor.SourceContentHash, SupersedesVersionId: supersedesVersionId);
    }
}
