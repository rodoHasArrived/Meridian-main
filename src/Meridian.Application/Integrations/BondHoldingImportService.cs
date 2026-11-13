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
        var run = await store.GetSyncRunAsync(request.SyncRunId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("A complete retained source run is required for a full snapshot.");
        if (run.SyncRunId != request.SyncRunId || run.ConnectionId != request.ConnectionId
            || run.Capability is not (ProviderCapabilityKindDto.Holdings or ProviderCapabilityKindDto.Positions)
            || run.CompletedAt is null || run.Status is not (ProviderIntegrationProcessingStatusDto.Validated
                or ProviderIntegrationProcessingStatusDto.Quarantined or ProviderIntegrationProcessingStatusDto.Loaded
                or ProviderIntegrationProcessingStatusDto.Published))
            throw new InvalidOperationException("A completed holding source run in the requested scope is required.");
        // Quarantine replay contains selected rejects, not the original statement's complete population.
        if (run.SourceSyncRunId is not null)
            throw new InvalidOperationException("A quarantine replay subset cannot establish a full snapshot; ingest the complete corrected statement in a new source run.");
        var staged = await store.ListStagingRecordsAsync(request.SyncRunId, ct).ConfigureAwait(false);
        var quarantined = await store.ListQuarantinedRecordsAsync(request.SyncRunId, ct).ConfigureAwait(false);
        if (run.RecordsReceived != (long)run.RecordsAccepted + run.RecordsQuarantined
            || staged.Any(s => s.SyncRunId != request.SyncRunId || s.ConnectionId != request.ConnectionId || s.Capability != run.Capability)
            || quarantined.Any(q => q.SyncRunId != request.SyncRunId || q.ConnectionId != request.ConnectionId || q.Capability != run.Capability)
            || staged.Count != run.RecordsAccepted || quarantined.Count != run.RecordsQuarantined)
            throw new InvalidOperationException("Retained staging and quarantine do not reproduce the completed source population.");
        var unresolved = new List<QuarantinedRecordDto>();
        var otherAccountQuarantine = new List<QuarantinedRecordDto>();
        foreach (var row in quarantined)
        {
            if (await ProvesDifferentAccountAsync(row.MappedRecord, row.ValidationErrors, request.AccountId, ct).ConfigureAwait(false))
                otherAccountQuarantine.Add(row);
            else
                unresolved.Add(row);
        }
        if (run.Issues.Where(issue => issue.Severity == ProviderIntegrationIssueSeverityDto.Critical)
            .Except(otherAccountQuarantine.SelectMany(row => row.ValidationErrors)).Any())
        {
            // Source/page errors need not create a rejected row. They cannot prove a complete statement.
            if (unresolved.Count == 0)
                throw new InvalidOperationException("A full snapshot requires a complete source fetch without unresolved critical run issues.");
        }
        if (unresolved.Count != 0)
        {
            var decisions = await store.ListQuarantineDecisionsAsync(request.SyncRunId, ct).ConfigureAwait(false);
            var quarantineReviewed = unresolved.All(q => decisions.Any(d => d.SyncRunId == request.SyncRunId
                && d.ConnectionId == request.ConnectionId && d.QuarantineRecordId == q.QuarantineRecordId
                && !string.IsNullOrWhiteSpace(d.ReviewedBy)));
            // Review/ignore/cash/replay-request decisions classify rejected source rows; they do not
            // supply the missing accepted position economics or merge a replay with the original run.
            throw new InvalidOperationException(quarantineReviewed
                ? "Quarantine review decisions do not establish complete holdings; ingest the complete corrected statement in a new source run."
                : "A full snapshot cannot omit unresolved quarantined holdings for this account or rows with unknown account identity.");
        }
        var handoffs = await store.ListReconciliationHandoffRecordsAsync(request.ConnectionId, ct).ConfigureAwait(false);
        var reviewed = handoffs.Where(h => h.SyncRunId == request.SyncRunId && h.ConnectionId == request.ConnectionId
            && Guid.TryParse(h.InternalAccountId, out var id) && id == request.AccountId).ToArray();
        var rows = staged.Where(s => s.ConnectionId == request.ConnectionId
            && s.Capability is ProviderCapabilityKindDto.Holdings or ProviderCapabilityKindDto.Positions
            && reviewed.Any(h => h.StagingRecordId == s.StagingRecordId)).OrderBy(s => s.StagingRecordId, StringComparer.Ordinal).ToArray();
        if (rows.Length == 0)
            throw new InvalidOperationException("No reviewed holding population is retained for this account and run.");
        foreach (var row in staged.Where(s => !rows.Any(r => r.StagingRecordId == s.StagingRecordId)))
            if (!await ProvesDifferentAccountAsync(row.MappedRecord, row.ValidationWarnings, request.AccountId, ct).ConfigureAwait(false))
                throw new InvalidOperationException("A full snapshot cannot omit unreviewed rows for this account or rows with unknown account identity.");
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
                || row.Status is not (ProviderIntegrationProcessingStatusDto.Validated
                    or ProviderIntegrationProcessingStatusDto.Loaded or ProviderIntegrationProcessingStatusDto.Published)
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

    private async Task<bool> ProvesDifferentAccountAsync(JsonElement? source, IReadOnlyList<ValidationIssueDto> issues,
        Guid accountId, CancellationToken ct)
    {
        if (source is not { } mapped)
            return false;
        var declarations = new HashSet<Guid>();
        foreach (var path in new[] { "accountId", "internalAccountId", "account.internalAccountId" })
        {
            var value = mapped;
            var present = true;
            foreach (var part in path.Split('.'))
            {
                if (value.ValueKind != JsonValueKind.Object)
                    return false;
                if (!value.TryGetProperty(part, out value))
                {
                    present = false;
                    break;
                }
            }
            if (!present)
                continue;
            if (value.ValueKind != JsonValueKind.String || !Guid.TryParse(value.GetString(), out var declared))
                return false;
            declarations.Add(declared);
        }
        if (declarations.Count != 1 || declarations.Contains(accountId)
            || issues.Any(issue => issue.Severity == ProviderIntegrationIssueSeverityDto.Critical
                && issue.TargetField is "accountId" or "internalAccountId" or "account.internalAccountId" or "account"))
            return false;
        var otherAccountId = declarations.Single();
        var retained = await accounts.GetAccountAsync(otherAccountId, ct).ConfigureAwait(false);
        return retained?.AccountId == otherAccountId;
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
