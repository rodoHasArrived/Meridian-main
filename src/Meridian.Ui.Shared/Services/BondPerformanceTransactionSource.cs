using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Instruments.AssetOperations;
using Meridian.Ui.Shared.Evidence;

namespace Meridian.Ui.Shared.Services;

/// <summary>Verified, independently reviewed account transaction populations; imports accept source identifiers only.</summary>
public sealed class BondPerformanceTransactionSource(IEvidenceArtifactStore vault,
    IBondPerformanceTransactionPopulationStore populations, IBondMonitoringService monitoring,
    ISecurityMasterQueryService securities, IAccountBondHoldingsSource holdings,
    IBondPerformanceSourceVerifier verifier, TimeProvider? clock = null)
    : IBondPerformanceTransactionSource
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<BondPerformanceTransactionPopulationDto> ImportAsync(AccountCashForecastScopeDto scope,
        string evidenceUri, string documentId, string actor, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (!AccountCashForecastEngine.IsCompleteScope(scope))
            throw new ArgumentException("An exact account/book source scope is required.");
        var (document, evidence) = await ReadSource(scope, evidenceUri, documentId, _clock.GetUtcNow(), ct).ConfigureAwait(false);
        var population = new BondPerformanceTransactionPopulationDto(BondPerformanceTransactionRules.Identity(scope, evidence),
            scope, document, evidence, _clock.GetUtcNow(), actor.Trim());
        BondPerformanceTransactionRules.Validate(population);
        return await populations.AppendAsync(population, ct).ConfigureAwait(false);
    }

    public async Task<BondPerformanceTransactionEvidenceDto> GetAsync(AccountCashForecastScopeDto scope, DateOnly from,
        DateOnly through, DateTimeOffset knownAt, CancellationToken ct = default)
    {
        if (!AccountCashForecastEngine.IsCompleteScope(scope) || from == default || through <= from
            || knownAt == default || knownAt.Offset != TimeSpan.Zero || knownAt > _clock.GetUtcNow()
            || through > DateOnly.FromDateTime(knownAt.UtcDateTime))
            throw new ArgumentException("An elapsed period, exact scope and UTC knowledge cutoff are required.");
        var eligible = (await populations.ReadAsync(scope, ct).ConfigureAwait(false))
            .Where(row => row.RecordedAtUtc <= knownAt && row.Evidence.RetainedAtUtc <= knownAt
                && row.Document.PeriodStart == from && row.Document.PeriodEnd == through)
            .GroupBy(row => (row.Document.SourceDomain, row.Document.SourceEntityId))
            .Select(group => group.MaxBy(row => row.Document.Revision)!).ToArray();
        if (eligible.Length != 1)
            return new(false, [], [], [eligible.Length == 0
                ? "No reviewed complete transaction population covers the requested account/book period."
                : "Competing reviewed transaction populations cover this period; source authority must be resolved explicitly."]);
        var selected = eligible[0];
        var (source, evidence) = await ReadSource(scope, selected.Evidence.EvidenceUri, selected.Evidence.EvidenceId, knownAt, ct).ConfigureAwait(false);
        if (evidence != selected.Evidence || JsonSerializer.Serialize(source, BondPerformanceTransactionsJsonContext.Default.BondPerformanceTransactionsDocument)
            != JsonSerializer.Serialize(selected.Document, BondPerformanceTransactionsJsonContext.Default.BondPerformanceTransactionsDocument))
            throw new InvalidDataException("Retained transaction source content or accepted review has changed; a revised reviewed import is required.");
        var history = await monitoring.GetHistoryAsync(scope, ct).ConfigureAwait(false);
        var monitor = history.Where(row => row.Inputs.RecordedAtUtc <= knownAt).OrderBy(row => row.Inputs.RecordedAtUtc).LastOrDefault();
        if (monitor is not null)
            await verifier.VerifyMonitoringAsync(monitor.Inputs, ct).ConfigureAwait(false);
        var allPayments = monitor?.Inputs.Payments ?? [];
        var superseded = allPayments.Select(row => row.SupersedesVersionId).Where(id => id is not null).ToHashSet(StringComparer.Ordinal);
        // The completeness proof binds this exact closing-mark interval and both accepted batches.
        var paymentIds = allPayments.Where(row => row.Status == AccountCashFlowStatus.Actual && !superseded.Contains(row.VersionId)
                && row.CashReceiptDate > source.PeriodStart && row.CashReceiptDate <= source.PeriodEnd
                && row.Provenance.Evidence.RetainedAtUtc <= knownAt)
            .Select(row => row.VersionId).Order(StringComparer.Ordinal).ToArray();
        var expected = source.PaymentVersionIds.Order(StringComparer.Ordinal).ToArray();
        var cashMatches = paymentIds.SequenceEqual(expected, StringComparer.Ordinal);
        var gaps = new List<string>();
        if (!source.CompleteTradePopulation)
            gaps.Add("The reviewed source does not declare a complete trade population.");
        if (!source.CompleteCashPopulation)
            gaps.Add("The reviewed source does not declare a complete cash-observation population.");
        if (!cashMatches)
            gaps.Add("Reviewed cash-population identities differ from retained active payment observations.");
        foreach (var redemption in source.TradeCash.Where(row => row.Kind == "PrincipalRedemption"))
        {
            var receipt = allPayments.SingleOrDefault(row => row.VersionId == redemption.PaymentVersionId && !superseded.Contains(row.VersionId));
            if (receipt is null || receipt.CashReceiptDate != redemption.CashDate
                || monitor is null || !monitor.Inputs.Allocations.Any(allocation => allocation.ActualVersionId == receipt.VersionId
                    && monitor.Inputs.Obligations.Any(obligation => obligation.ObligationId == allocation.ExpectedFlowId
                        && obligation.SecurityId == redemption.SecurityId && obligation.FlowType == "Principal")))
                gaps.Add("A principal-redemption face removal lacks its exact retained principal collection allocation.");
        }
        var input = new BondCalculationInput("PerformanceTransactions", selected.PopulationId, source.Revision,
            evidence.ContentHashSha256, AsOf: new DateTimeOffset(source.PeriodEnd.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            RecordedAt: selected.RecordedAtUtc);
        return new(source.CompleteTradePopulation && source.CompleteCashPopulation && cashMatches && gaps.Count == 0,
            source.TradeCash.Where(row => row.CashDate > from && row.CashDate <= through).Select(row =>
                new BondPerformanceTradeCashDto(row.SecurityId, row.CashDate, row.Amount, row.Currency, input)
                { OriginalFaceDelta = row.OriginalFaceDelta }).ToArray(), [input], gaps)
        { CashPopulationComplete = source.CompleteCashPopulation && cashMatches, PaymentVersionIds = paymentIds };
    }

    private async Task<(BondPerformanceTransactionsDocument Document, RetainedEvidenceIdentityDto Evidence)> ReadSource(
        AccountCashForecastScopeDto scope, string evidenceUri, string documentId, DateTimeOffset knownAt, CancellationToken ct)
    {
        if (!EvidenceVaultReference.TryParseCanonical(evidenceUri, out var vaultId) || string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("A canonical Evidence Vault URI and exact document identity are required.");
        var identity = await vault.TryGetVaultIdentityAsync(vaultId, scope.TenantId, scope.CompanyId, ct).ConfigureAwait(false);
        var documents = identity?.Documents.Where(row => row.DocumentId == documentId).ToArray();
        if (identity is null || identity.SubjectKind != "account" || identity.SubjectId != scope.AccountId
            || identity.TenantId != scope.TenantId || identity.Scope != scope.CompanyId || documents?.Length != 1)
            throw new InvalidOperationException("Transaction source belongs to another account, tenant or company, or is unavailable.");
        var metadata = documents[0];
        var review = metadata.ReviewerState;
        if (metadata.TenantId != scope.TenantId || metadata.Scope != scope.CompanyId || string.IsNullOrWhiteSpace(metadata.Actor)
            || review.Status != EvidenceDocumentReviewStatusDto.Accepted || string.IsNullOrWhiteSpace(review.Reviewer)
            || review.ReviewedAt is not { } reviewedAt || reviewedAt > knownAt
            || string.Equals(review.Reviewer, metadata.Actor, StringComparison.OrdinalIgnoreCase)
            || !review.ConfirmedFields.Any(field => field.FieldName == "sourceContentHashSha256"
                && Sha256Digest.FixedEquals(field.ConfirmedValue, metadata.SourceHashSha256)
                && field.ConfirmedBy == review.Reviewer && field.ConfirmedAt == reviewedAt)
            || !metadata.AuditTrail.Any(item => item.Action == "DocumentReviewRecorded" && item.Actor == review.Reviewer && item.RecordedAt == reviewedAt))
            throw new InvalidOperationException("The exact source bytes need an independent accepted review with confirmed content hash.");
        var file = await vault.TryOpenDocumentContentAsync(vaultId, documentId, scope.TenantId, scope.CompanyId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Retained transaction source bytes are unavailable.");
        await using var stream = file.Content;
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) != 0)
        {
            if (buffer.Length + count > 1048576)
                throw new ArgumentException("Reviewed transaction source JSON is limited to one MiB.");
            buffer.Write(chunk, 0, count);
        }
        var bytes = buffer.ToArray();
        if (!Sha256Digest.FixedEquals(Sha256Digest.Compute(bytes), metadata.SourceHashSha256))
            throw new InvalidDataException("Retained transaction source bytes differ from the accepted hash.");
        using var json = JsonDocument.Parse(bytes);
        RefuseDuplicates(json.RootElement);
        var source = JsonSerializer.Deserialize(bytes, BondPerformanceTransactionsJsonContext.Default.BondPerformanceTransactionsDocument)
            ?? throw new ArgumentException("Strict typed transaction source JSON is required.");
        var evidence = new RetainedEvidenceIdentityDto(documentId, evidenceUri, metadata.SourceHashSha256,
            metadata.SourceSystem, metadata.SourceReference, "Accepted", review.Reviewer, reviewedAt, source.PeriodEnd,
            source.Revision, reviewedAt, review.Reviewer, "account", scope.AccountId);
        var population = new BondPerformanceTransactionPopulationDto(BondPerformanceTransactionRules.Identity(scope, evidence),
            scope, source, evidence, _clock.GetUtcNow(), "source-validation");
        BondPerformanceTransactionRules.Validate(population);
        var accountId = Guid.Parse(scope.AccountId);
        var start = await holdings.GetHoldingsAsync(accountId, source.PeriodStart, knownAt, ct).ConfigureAwait(false);
        var end = await holdings.GetHoldingsAsync(accountId, source.PeriodEnd, knownAt, ct).ConfigureAwait(false);
        if (start is null || end is null || start.AccountId != accountId || end.AccountId != accountId
            || start.AsOfDate != source.PeriodStart || end.AsOfDate != source.PeriodEnd
            || start.BatchId != source.OpeningHoldingsBatchId || end.BatchId != source.ClosingHoldingsBatchId)
            throw new InvalidOperationException("Reviewed return coverage must bind the exact accepted settled-position snapshots at both period boundaries.");
        foreach (var securityId in source.TradeCash.Select(row => row.SecurityId).Distinct())
        {
            var security = await securities.GetRecordedByIdAsOfAsync(securityId, knownAt, ct).ConfigureAwait(false);
            if (security is null || security.SecurityId != securityId
                || !SecurityAssetClassCatalog.GetOrDefault(security.AssetClass).CarriesBondCouponStructure)
                throw new InvalidOperationException("A reviewed trade requires a resolved canonical bond identity at the retained cutoff.");
        }
        var current = await vault.TryGetVaultIdentityAsync(vaultId, scope.TenantId, scope.CompanyId, ct).ConfigureAwait(false);
        var fresh = current?.Documents.Where(row => row.DocumentId == documentId).ToArray();
        if (fresh?.Length != 1 || JsonSerializer.Serialize(fresh[0], BondPerformanceEvidenceJsonContext.Default.EvidenceDocumentDto)
            != JsonSerializer.Serialize(metadata, BondPerformanceEvidenceJsonContext.Default.EvidenceDocumentDto))
            throw new InvalidOperationException("Source review changed during transaction verification; reload the retained source.");
        return (source, evidence);
    }

    private static void RefuseDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new JsonException("Duplicate transaction source JSON property.");
                RefuseDuplicates(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var child in value.EnumerateArray())
                RefuseDuplicates(child);
    }
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(EvidenceDocumentDto))]
internal partial class BondPerformanceEvidenceJsonContext : JsonSerializerContext;
