using Meridian.Contracts.Integrity;
using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Storage.Ledger;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

public sealed record SecurityRateRepairActor(string TenantId, string? CompanyId, string Actor, bool CanModify);

public sealed partial class SecurityRateRepairService(
    ISecurityRateRepairStore store, ISecurityMasterQueryService query, ISecurityMasterStore securities,
    ISecurityMasterRevisionStore revisions, ISecurityMasterWorkbenchCommandService workbench,
    IOperationsContinuityWorkflowService workflows, ISecurityRateRepairEvidenceResolver evidence,
    ISecurityMasterImpactPreviewService impacts, ILedgerJournalStore ledger,
    ISecurityFieldProvenanceStore? fieldProvenance = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<SecurityRateRepairDiscoveryDto> DiscoverAsync(SecurityRateRepairActor actor, CancellationToken ct = default)
        => DiscoverPageAsync(actor, 0, 200, ct);

    public async Task<SecurityRateRepairDiscoveryDto> DiscoverPageAsync(SecurityRateRepairActor actor, int skip, int pageSize, CancellationToken ct = default)
    {
        RequireActor(actor, false);
        if (skip < 0 || pageSize is < 1 or > 500 || skip > int.MaxValue - pageSize)
            throw new ArgumentException("Discovery pages require a representable nonnegative skip and a page size from 1 to 500.");
        var summaries = await query.SearchAsync(new SecuritySearchRequest(string.Empty, Take: pageSize, Skip: skip), ct).ConfigureAwait(false);
        var candidates = new List<SecurityRateRepairCandidateDto>();
        var issues = new List<SecurityRateRepairScanIssueDto>();
        foreach (var summary in summaries)
        {
            var record = await securities.GetProjectionAsync(summary.SecurityId, ct).ConfigureAwait(false);
            if (record is null)
            {
                issues.Add(new(summary.SecurityId, "ProjectionUnavailable", "A listed Security Master record has no readable canonical projection."));
                candidates.Add(new(summary.SecurityId, summary.DisplayName, summary.AssetClass, summary.Version,
                    "Unknown", "", null, null, null, null, [], false, "ProjectionUnavailable",
                    "Canonical source evidence is unavailable; this record cannot join a repair cohort.",
                    $"/accounting/security-master?securityId={summary.SecurityId:D}"));
                continue;
            }
            var candidate = await CandidateAsync(record, ct).ConfigureAwait(false);
            if (candidate.RawValue is not null && (candidate.CurrentUnit is null || !candidate.Eligible))
                candidates.Add(candidate);
        }
        var canCreate = actor.CanModify && !string.IsNullOrWhiteSpace(actor.CompanyId);
        return new(DateTimeOffset.UtcNow, candidates,
            [new("Create", canCreate, canCreate ? null : "Modify Security Master permission and authenticated Evidence Vault company scope are required.")])
        {
            Skip = skip,
            PageSize = pageSize,
            ScannedCount = summaries.Count,
            NextSkip = summaries.Count == pageSize ? checked(skip + summaries.Count) : null,
            IsComplete = summaries.Count < pageSize && issues.Count == 0,
            Issues = issues
        };
    }

    public async Task<SecurityRateRepairBatchDto> CreateAsync(CreateSecurityRateRepairBatchRequest request,
        SecurityRateRepairActor actor, CancellationToken ct = default)
    {
        RequireActor(actor, true);
        ValidateRequest(request);
        var fingerprint = Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(request, Json));
        var retained = await store.GetAsync(request.RequestId, actor.TenantId, ct).ConfigureAwait(false);
        if (retained is not null)
        {
            if (retained.Manifest.CommandSha256 != fingerprint || retained.Manifest.Maker != actor.Actor
                || retained.Manifest.CompanyId != actor.CompanyId)
                throw new SecurityRateRepairConflictException("The request identity already names another immutable repair command.");
            return await ToDtoAsync(retained, actor, ct).ConfigureAwait(false);
        }
        await ValidateAccountingScopeAsync(request.Scope, ct).ConfigureAwait(false);
        var frozen = new List<SecurityRateRepairFrozenItem>();
        foreach (var selection in request.Items)
        {
            var record = await securities.GetProjectionAsync(selection.SecurityId, ct).ConfigureAwait(false)
                ?? throw new ArgumentException("A selected security is unavailable.");
            var candidate = await CandidateAsync(record, ct).ConfigureAwait(false);
            if (candidate.Version != selection.ExpectedVersion)
                throw new SecurityRateRepairConflictException("A selected record changed; reload the cohort.");
            if (!candidate.Eligible || candidate.CurrentUnit is not null || !candidate.AllowedUnits.Contains(request.Unit, StringComparer.Ordinal)
                || candidate.SourceSystem != request.Cohort.SourceSystem || candidate.SourceField != request.Cohort.SourceField
                || candidate.RateRole != request.Cohort.RateRole
                || (candidate.SourceSchemaVersion is not null && candidate.SourceSchemaVersion != request.Cohort.SourceSchemaVersion))
                throw new ArgumentException("The selection is not one evidence-compatible, undeclared scalar-rate cohort.");
            frozen.Add(new(record.SecurityId, record.Version, record.DisplayName, record.AssetClass, candidate.RawValue));
        }
        if (frozen.Select(static item => item.AssetClass).Distinct(StringComparer.Ordinal).Count() != 1)
            throw new ArgumentException("Split different asset classes into separately reviewed repair cohorts.");
        var source = await evidence.ResolveAsync(request.EvidenceId, actor.TenantId, ct).ConfigureAwait(false);
        if (source.ContentSha256.Length != 64)
            throw new InvalidOperationException("Evidence has no retained content identity.");
        var manifest = new SecurityRateRepairManifest(actor.TenantId, actor.CompanyId, actor.Actor,
            DateTimeOffset.UtcNow, fingerprint, source.ContentSha256, request, frozen);
        return await ToDtoAsync(await store.CreateAsync(manifest, ct).ConfigureAwait(false), actor, ct).ConfigureAwait(false);
    }

    public async Task<SecurityRateRepairBatchDto?> GetAsync(Guid batchId, SecurityRateRepairActor actor, CancellationToken ct = default)
    {
        RequireActor(actor, false);
        var batch = await store.GetAsync(batchId, actor.TenantId, ct).ConfigureAwait(false);
        return batch is null || batch.Manifest.CompanyId != actor.CompanyId ? null : await ToDtoAsync(batch, actor, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SecurityRateRepairBatchDto>> ListAsync(SecurityRateRepairActor actor, CancellationToken ct = default)
    {
        RequireActor(actor, false);
        var results = new List<SecurityRateRepairBatchDto>();
        foreach (var batch in await store.ListAsync(actor.TenantId, ct).ConfigureAwait(false))
            if (batch.Manifest.CompanyId == actor.CompanyId)
                results.Add(ToDto(batch, actor, currentAuthority: false));
        return results;
    }

    private async Task ValidateAccountingScopeAsync(SecurityRateRepairScopeDto scope, CancellationToken ct)
    {
        var book = await ledger.GetLedgerBookAsync(scope.LedgerBookId, ct).ConfigureAwait(false);
        if (book is null || book.FundProfileId != scope.FundProfileId || scope.FundAccountId == Guid.Empty
            || !Guid.TryParse(scope.PeriodId, out var periodId))
            throw new ArgumentException("Select an authoritative book, owning fund profile, account and real accounting period identity.");
        var period = await ledger.GetPeriodAsync(periodId, ct).ConfigureAwait(false);
        if (period?.LedgerBookId != scope.LedgerBookId)
            throw new ArgumentException("The accounting period does not belong to the selected book.");
    }

    private static void ValidateRequest(CreateSecurityRateRepairBatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RequestId == Guid.Empty || request.Scope is null || request.Cohort is null
            || request.Items is null || request.Items.Count is < 1 or > 500
            || request.Items.Select(static item => item.SecurityId).Distinct().Count() != request.Items.Count
            || request.Items.Any(static item => item.SecurityId == Guid.Empty || item.ExpectedVersion < 1)
            || request.EffectiveFrom == default || request.EffectiveFrom.Offset != TimeSpan.Zero
            || string.IsNullOrWhiteSpace(request.Justification) || request.Justification.Length > 4000
            || string.IsNullOrWhiteSpace(request.EvidenceId) || !request.EvidenceAppliesToAllRecords
            || string.IsNullOrWhiteSpace(request.Cohort.SourceSystem) || string.IsNullOrWhiteSpace(request.Cohort.SourceField)
            || string.IsNullOrWhiteSpace(request.Cohort.SourceSchemaVersion) || request.Cohort.RateRole != "AnnualRate"
            || request.Cohort.SourceSystem.Length > 256 || request.Cohort.SourceField.Length > 256 || request.Cohort.SourceSchemaVersion.Length > 256
            || SecurityRecordRateUnit.Parse(request.Unit) is not { } unit || unit == RateUnit.Unrecognized)
            throw new ArgumentException("Provide a bounded, versioned roster, exact source/schema/role, supported unit, UTC effective date and applicable retained evidence.");
    }

    private static void RequireActor(SecurityRateRepairActor actor, bool modify)
    {
        if (string.IsNullOrWhiteSpace(actor.TenantId) || string.IsNullOrWhiteSpace(actor.Actor)
            || (modify && (!actor.CanModify || string.IsNullOrWhiteSpace(actor.CompanyId))))
            throw new UnauthorizedAccessException("A tenant/company-scoped, authorized operator is required.");
    }

}
