using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Integrations;
using Meridian.Contracts.Ledger;
using Meridian.PortfolioRecords.Accounts;

namespace Meridian.Application.Integrations;

/// <summary>Reviewed staging to observed account statements; never creates lots or journals.</summary>
public sealed class BondHoldingImportService(IProviderIntegrationManifestStore manifests,
    IAccountManagementService management, IAccountQueryService accounts)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CustodianStatementBatchDto> AcceptAsync(string tenantId, string actor,
        BondHoldingImportRequest request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ArgumentNullException.ThrowIfNull(request);
        // A shared unscoped manifest store cannot establish tenant source authority.
        if (manifests is not IProviderIntegrationTenantManifestStoreFactory factory)
            throw new NotSupportedException("Tenant-scoped retained integration evidence is required.");
        var store = factory.ForTenant(tenantId);
        _ = await accounts.GetAccountAsync(request.AccountId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Account was not found in the authenticated scope.");
        _ = await store.GetConnectionAsync(request.ConnectionId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Integration connection was not found in the authenticated scope.");
        var staged = await store.ListStagingRecordsAsync(request.SyncRunId, ct).ConfigureAwait(false);
        var handoffs = await store.ListReconciliationHandoffRecordsAsync(request.ConnectionId, ct).ConfigureAwait(false);
        var reviewed = handoffs.Where(h => h.SyncRunId == request.SyncRunId && h.ConnectionId == request.ConnectionId
            && Guid.TryParse(h.InternalAccountId, out var id) && id == request.AccountId).ToArray();
        var rows = staged.Where(s => s.ConnectionId == request.ConnectionId
            && s.Capability is ProviderCapabilityKindDto.Holdings or ProviderCapabilityKindDto.Positions
            && reviewed.Any(h => h.StagingRecordId == s.StagingRecordId)).OrderBy(s => s.StagingRecordId, StringComparer.Ordinal).ToArray();
        if (rows.Length == 0)
            throw new InvalidOperationException("No reviewed holding population is retained for this account and run.");
        var unreviewed = staged.Where(s => s.ConnectionId == request.ConnectionId
            && s.Capability is ProviderCapabilityKindDto.Holdings or ProviderCapabilityKindDto.Positions)
            .Where(s => Guid.TryParse(Read(s.MappedRecord, "accountId"), out var targetAccount) && targetAccount == request.AccountId
                && !rows.Any(r => r.StagingRecordId == s.StagingRecordId));
        if (unreviewed.Any())
            throw new InvalidOperationException("A full snapshot cannot omit unreviewed rows for this account.");
        var lines = new List<CustodianPositionLineDto>();
        var evidence = new List<string>();
        foreach (var row in rows)
        {
            var decisions = handoffs.Where(h => h.SyncRunId == request.SyncRunId && h.ConnectionId == request.ConnectionId
                && h.StagingRecordId == row.StagingRecordId).OrderByDescending(h => h.RequestedAt).ToArray();
            var decision = decisions[0];
            if (decisions.Length > 1 && decisions[1].RequestedAt == decision.RequestedAt
                || string.IsNullOrWhiteSpace(decision.ApprovalEvidenceId)
                || !Guid.TryParse(decision.InternalAccountId, out var decidedAccount) || decidedAccount != request.AccountId
                || decision.PromotionTarget != "reconciliation-staging"
                || decision.Issues.Any(i => i.Severity == ProviderIntegrationIssueSeverityDto.Critical)
                || row.ValidationWarnings.Any(i => i.Severity == ProviderIntegrationIssueSeverityDto.Critical)
                || row.Status is not (ProviderIntegrationProcessingStatusDto.Loaded or ProviderIntegrationProcessingStatusDto.Published)
                || !Guid.TryParse(decision.InternalSecurityId, out var securityId))
                throw new InvalidOperationException("A holding row lacks unique, valid retained review and identity evidence.");
            _ = await store.GetRawPayloadAsync(row.SyncRunId, row.RawPayloadId, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Original provider payload is unavailable.");
            var source = row.MappedRecord;
            if (Read(source, "valuationBasis") == "CostDerived")
                throw new InvalidOperationException("Cost-derived values cannot establish observed bond valuations.");
            if (!DateOnly.TryParseExact(Read(source, "asOfDate"), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var sourceDate) || sourceDate != request.AsOfDate)
                throw new InvalidOperationException("Statement date must match each retained holding observation.");
            var bond = source.TryGetProperty("bond", out var input) ? input.Deserialize<BondHoldingEconomicsDto>(JsonOptions) : null;
            if (bond is null)
                throw new InvalidOperationException("Explicit normalized bond economics are required.");
            bond = bond with { SourceEvidenceId = decision.ApprovalEvidenceId, OriginalSourceJson = source.GetRawText() };
            var currency = Read(source, "marketValue.currency") ?? Read(source, "currency")
                ?? throw new InvalidOperationException("An explicit holding currency is required.");
            if (Read(source, "price.currency") is { } priceCurrency && priceCurrency != currency)
                throw new InvalidOperationException("Bond quote and market-value currencies disagree.");
            var normalized = BondHoldingNormalization.Calculate(bond, currency);
            if (Number(source, "marketValue.amount") is not { } marketValue
                || LedgerCurrencyRounding.RoundCurrency(marketValue, currency) != LedgerCurrencyRounding.RoundCurrency(normalized.Dirty, currency))
                throw new InvalidOperationException("Reported market value does not match normalized dirty cash.");
            var lineIdentity = Sha256Digest.ComputeBytesUtf8($"{tenantId}/{request.BatchId}/{row.StagingRecordId}");
            lines.Add(new(new Guid(lineIdentity.AsSpan(0, 16)), request.BatchId, request.AccountId, request.AsOfDate,
                Read(source, "identifier") ?? securityId.ToString(), Read(source, "identifierType") ?? "SecurityId",
                bond.OriginalFace, marketValue, currency, Read(source, "securityName"), "Bond", bond.OriginalFace < 0,
                Number(source, "costBasis.amount"), securityId, bond));
            evidence.Add($"{row.StagingRecordId}/{row.RawPayloadId}/{decision.ApprovalEvidenceId}/{source.GetRawText()}");
        }
        var sourceHash = Sha256Digest.ComputeUtf8(string.Join("\n", evidence));
        var authority = new CustodianStatementAuthorityDto(request.Revision,
            $"integration:{request.ConnectionId}:{request.SyncRunId}", sourceHash, SupersedesBatchId: request.SupersedesBatchId);
        return await management.IngestCustodianStatementAsync(new(request.BatchId, request.AccountId,
            request.AsOfDate, request.ConnectionId, "reviewed-integration-bond-v1", null, lines, actor, Authority: authority), ct).ConfigureAwait(false);
    }

    private static string? Read(JsonElement root, string path)
    {
        foreach (var part in path.Split('.'))
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(part, out root))
                return null;
        return root.ValueKind == JsonValueKind.String ? root.GetString() : null;
    }

    private static decimal? Number(JsonElement root, string path)
    {
        foreach (var part in path.Split('.'))
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(part, out root))
                return null;
        return root.ValueKind == JsonValueKind.Number && root.TryGetDecimal(out var value) ? value :
            decimal.TryParse(root.ValueKind == JsonValueKind.String ? root.GetString() : null,
                NumberStyles.Number, CultureInfo.InvariantCulture, out value) ? value : null;
    }
}
