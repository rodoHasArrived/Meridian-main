using System.Text.Json;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Ledger;
using Meridian.Storage.SecurityMaster;
using static Meridian.Application.SecurityMaster.FileSecurityMasterImpactPreviewStore;

namespace Meridian.Application.SecurityMaster;

public sealed class SecurityMasterImpactPreviewService(
    ISecurityMasterRevisionStore revisions,
    ISecurityMasterStore securities,
    ISecurityMasterWorkbenchQueryService workbench,
    IAffectedLedgerBookResolver books,
    ISecurityMasterCashFlowService cashFlows,
    ISecurityMasterImpactPreviewStore store,
    IFundProfileTenancyRegistry ownership,
    ILedgerJournalStore? ledger = null,
    Meridian.ReferenceData.SecurityMaster.ISecurityAssetProfileCatalog? profiles = null,
    ISecurityMasterReleasedReportImpactQuery? reports = null,
    TimeProvider? clock = null) : ISecurityMasterImpactPreviewService
{
    public async Task<SecurityMasterImpactPreviewReceipt> CreateAsync(Guid securityId, Guid revisionId,
        SecurityMasterImpactScope scope, DateTimeOffset asOf, CancellationToken ct = default)
    {
        if (asOf == default || asOf.Offset != TimeSpan.Zero)
            throw new ArgumentException("An explicit UTC economic date is required.");
        var revision = await revisions.GetAsync(revisionId, ct).ConfigureAwait(false)
            ?? throw Refusal("RevisionMissing", "The revision does not exist.");
        if (revision.SecurityId != securityId)
            throw Refusal("RevisionMismatch", "The revision is not an active proposal for this security.");
        await EnsureScopeAsync(scope, revision, ct).ConfigureAwait(false);
        if (revision.State == SecurityMasterRevisionStateDto.Approved)
            throw Refusal("ImpactApprovalImmutable", "An approved revision's impact receipt cannot be replaced. Create a fresh governed draft and approval workflow.");
        if (revision.State is not (SecurityMasterRevisionStateDto.Draft or SecurityMasterRevisionStateDto.Submitted))
            throw Refusal("RevisionMismatch", "The revision is not an active proposal for this security.");
        var projection = await securities.GetProjectionAsync(securityId, ct).ConfigureAwait(false)
            ?? throw Refusal("SecurityMissing", "The canonical security is unavailable.");
        if (!revision.FieldValueRecorded)
            throw Refusal("LegacyDraftUnavailable", "This revision has no retained proposed value; stage a new draft.");
        if (revision.FieldBaseRecorded && revision.FieldPath is { } path
            && SecurityAssetTermsFieldSnapshot.Capture(projection.AssetClass, projection.AssetSpecificTerms, path) != revision.FieldBaseSnapshot)
            throw Refusal("StaleProposal", "The proposed field changed since drafting; discard and stage it against the current value.");
        var previous = SecurityMasterMapping.ToDetail(projection);
        var proposed = previous with
        {
            AssetSpecificTerms = new SecurityMasterProposedTermsBuilder(profiles).Build(projection, revision)
        };
        if (TermsHash(previous) != TermsHash(proposed))
            proposed = proposed with { EffectiveFrom = revision.FieldEffectiveFrom ?? previous.EffectiveFrom };
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        var receipt = await EvaluateAsync(revision, scope, previous, proposed, asOf, now, ct).ConfigureAwait(false);
        receipt = receipt with { ContentSha256 = ReceiptHash(receipt) };
        await store.RetainAsync(receipt, ct).ConfigureAwait(false);
        return receipt;
    }

    public async Task<SecurityMasterImpactPreviewReceipt?> GetAsync(SecurityMasterImpactScope scope, string receiptId, CancellationToken ct = default)
    {
        var receipt = await store.GetAsync(scope, receiptId, ct).ConfigureAwait(false);
        if (receipt is null)
            return null;
        var revision = await revisions.GetAsync(receipt.RevisionId, ct).ConfigureAwait(false)
            ?? throw Refusal("ImpactRevisionUnavailable", "The receipt's governed revision is unavailable.");
        await EnsureScopeAsync(scope, revision, ct).ConfigureAwait(false);
        if (receipt.Reports.RunIds.Count > 0)
        {
            if (reports is null || !await reports.CanReadRetainedRunIdsAsync(receipt.Reports.RunIds, scope, ct).ConfigureAwait(false))
                throw Refusal("ImpactEvidenceAccessDenied", "The retained reporting references cannot be authorized for this session. The immutable receipt has not been changed.");
        }
        return receipt;
    }

    public async Task<SecurityMasterImpactPreviewReceipt> ValidateAsync(SecurityMasterRevisionRecord revision,
        SecurityMasterImpactScope scope, string receiptId, bool canonicalMergeApplied = false, CancellationToken ct = default)
    {
        await EnsureScopeAsync(scope, revision, ct).ConfigureAwait(false);
        var retained = await store.GetAsync(scope, receiptId, ct).ConfigureAwait(false)
            ?? throw Refusal("ImpactReceiptMissing", "The reviewed impact receipt is unavailable in this tenant scope.");
        if (retained.RevisionId != revision.RevisionId || retained.SecurityId != revision.SecurityId
            || retained.RevisionFingerprint != RevisionFingerprint(revision))
            throw Refusal("ImpactRevisionChanged", "Impact evidence belongs to another proposal or the proposal changed.");
        var current = await securities.GetProjectionAsync(revision.SecurityId, ct).ConfigureAwait(false)
            ?? throw Refusal("SecurityMissing", "The canonical security is unavailable.");
        var detail = SecurityMasterMapping.ToDetail(current);
        var expected = canonicalMergeApplied ? retained.ProposedTerms : retained.PreviousTerms;
        var changesTerms = TermsHash(retained.PreviousTerms) != TermsHash(retained.ProposedTerms);
        var expectedVersion = canonicalMergeApplied && changesTerms ? retained.CanonicalVersion + 1 : retained.CanonicalVersion;
        var ownMerge = !canonicalMergeApplied || !changesTerms
            || current.Provenance.ValueKind == JsonValueKind.Object
                && current.Provenance.TryGetProperty("sourceSystem", out var sourceSystem)
                && sourceSystem.ValueKind == JsonValueKind.String
                && sourceSystem.GetString() == SecurityMasterWriteAuthority.WorkbenchSourceSystem
                && current.Provenance.TryGetProperty("sourceRecordId", out var sourceRecord)
                && sourceRecord.ValueKind == JsonValueKind.String
                && Guid.TryParse(sourceRecord.GetString(), out var sourceRevision) && sourceRevision == revision.RevisionId;
        if (TermsHash(detail) != TermsHash(expected)
            || current.Version != expectedVersion || !ownMerge)
            throw Refusal("ImpactSecurityChanged", "Canonical terms changed after the preview. For an approved revision, create a fresh governed draft and approval workflow; the prior approval remains immutable.");
        var recomputed = await EvaluateAsync(revision, scope, retained.PreviousTerms, retained.ProposedTerms,
            retained.AsOf, retained.CapturedAt, ct).ConfigureAwait(false);
        if (recomputed.DependencyFingerprint != retained.DependencyFingerprint)
            throw Refusal("ImpactDependenciesChanged", "Dependencies changed after review. Create fresh impact evidence before approval; an already-approved revision requires a new governed draft and approval workflow.");
        return retained;
    }

    public async Task<IAsyncDisposable> AcquireFinalizationLeaseAsync(SecurityMasterRevisionRecord revision,
        SecurityMasterImpactScope scope, CancellationToken ct = default)
    {
        await EnsureScopeAsync(scope, revision, ct).ConfigureAwait(false);
        if (ledger is not IImpactDependencyGuard guard || string.IsNullOrWhiteSpace(revision.FundProfileId))
            throw Refusal("ImpactGuardUnavailable", "A tenant-bound authoritative ledger dependency guard is required to finalize reviewed changes.");
        if (revisions is not ISecurityMasterMutationParticipant revisionDatabase
            || securities is not ISecurityMasterImpactGuardParticipant securityDatabase
            || cashFlows is not SecurityMasterCashFlowService { ImpactGuardOptions: { } sourceDatabase }
            || !PostgresSecurityMasterMutation.SameDatabase(revisionDatabase.MutationOptions, securityDatabase.ImpactGuardOptions)
            || !PostgresSecurityMasterMutation.SameDatabase(revisionDatabase.MutationOptions, sourceDatabase))
            throw Refusal("ImpactDatabaseMismatch", "Revision, canonical terms and cash-flow source authorities must share the guarded Security Master database.");
        try
        { return await guard.AcquireImpactDependencyLeaseAsync(scope.TenantId, revision.FundProfileId, ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { throw Refusal("ImpactGuardUnavailable", "Authoritative dependency locks could not be acquired; retry finalization. " + ex.GetType().Name); }
    }

    private async Task<SecurityMasterImpactPreviewReceipt> EvaluateAsync(SecurityMasterRevisionRecord revision,
        SecurityMasterImpactScope scope, SecurityDetailDto previous, SecurityDetailDto proposed,
        DateTimeOffset asOf, DateTimeOffset capturedAt, CancellationToken ct)
    {
        var issues = new List<SecurityImpactIssue>();
        var snapshot = await workbench.GetTrustSnapshotAsync(revision.SecurityId, revision.FundProfileId, ct).ConfigureAwait(false);
        var scopeImpact = snapshot?.DownstreamImpact ?? new SecurityMasterDownstreamImpactDto(revision.FundProfileId,
            !string.IsNullOrWhiteSpace(revision.FundProfileId), SecurityMasterImpactSeverity.Unknown,
            "Scoped display exposure is unavailable.", "", "", "", "", 0, 0, 0, 0, 0, []);
        var bookResult = await books.ResolveWithEvidenceAsync(scopeImpact, ct).ConfigureAwait(false)
            ?? new(SecurityImpactCompleteness.Unavailable, [], "The resolver returned no coverage evidence.", [new("BookCoverageUnknown", "Book impact is unavailable.")]);
        var periods = new List<SecurityImpactPeriod>();
        var journalInputs = new List<object>();
        if (ledger is not null)
        {
            foreach (var bookId in bookResult.BookIds.Order())
            {
                try
                {
                    var book = await ledger.GetLedgerBookAsync(bookId, ct).ConfigureAwait(false);
                    var retainedPeriods = await ledger.ListPeriodsAsync(ledgerBookId: bookId, ct: ct).ConfigureAwait(false);
                    // All retained periods at/after the edit are conservative coverage. No single-date
                    // lock check is represented as proof of the whole economic impact interval.
                    periods.AddRange(retainedPeriods.Where(p => p.EndDate >= DateOnly.FromDateTime((revision.FieldEffectiveFrom ?? asOf).UtcDateTime))
                        .Select(p => new SecurityImpactPeriod(bookId, p.PeriodId, p.Version, p.StartDate, p.EndDate, p.Status)));
                    var journals = await ledger.QueryAsync(new LedgerJournalEntryQuery(LedgerBookId: bookId), ct).ConfigureAwait(false);
                    journalInputs.Add(new { book, Journals = journals.OrderBy(j => j.GlobalSequence).ToArray() });
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { issues.Add(new("LedgerDependencyUnavailable", $"Ledger dependencies could not be verified for book {bookId:D}.")); }
            }
        }
        issues.Add(new("LotImpactUnverified", "Exact current lot and cross-fund impact counts have not been established."));
        var reportResult = reports is null
            ? new SecurityReleasedReportImpact(SecurityImpactCompleteness.Unavailable, [], "unavailable", [new("ReleasedReportLookupUnavailable", "Canonical released-report lineage could not be queried.")])
            : await reports.EvaluateAsync(revision.SecurityId, revision.FundProfileId, revision.FieldEffectiveFrom ?? asOf, scope, ct).ConfigureAwait(false);
        var source = await cashFlows.GetCashFlowSourceAsync(revision.SecurityId, ct).ConfigureAwait(false);
        SecurityCashFlowCalculationReceipt? before = null, after = null;
        if (source is null)
            issues.Add(new("CashFlowSourceMissing", "No cash-flow source is assigned; comparisons are unavailable."));
        else
        {
            before = await CalculateAsync(previous, "Before").ConfigureAwait(false);
            after = await CalculateAsync(proposed, "After").ConfigureAwait(false);
            if (before is not null && after is not null && before.ForwardCurves.Any(oldCurve =>
                after.ForwardCurves.Any(newCurve => string.Equals(oldCurve.IndexName, newCurve.IndexName, StringComparison.OrdinalIgnoreCase)
                    && Hash(oldCurve) != Hash(newCurve))))
                issues.Add(new("MarketInputsDiffer", "A shared index resolved to different market inputs between calculations; no economic delta can be inferred. Refresh using pinned inputs."));
        }
        var orderedPeriods = periods.OrderBy(p => p.BookId).ThenBy(p => p.PeriodId).ToArray();
        var fingerprint = Hash(new
        {
            Books = bookResult,
            Periods = orderedPeriods,
            Journals = journalInputs,
            Reports = reportResult,
            Source = source,
            Before = before?.ContentSha256,
            After = after?.ContentSha256,
            Issues = issues
        });
        return new("security-master-impact-v1", scope, revision.SecurityId, revision.RevisionId, previous.Version,
            RevisionFingerprint(revision), TermsHash(previous), previous, proposed, asOf, capturedAt, bookResult,
            orderedPeriods, reportResult, before, after, issues, fingerprint, string.Empty)
        { FundProfileId = revision.FundProfileId };

        async Task<SecurityCashFlowCalculationReceipt?> CalculateAsync(SecurityDetailDto detail, string side)
        {
            try
            {
                var calculation = await cashFlows.EvaluateProjectionAsync(detail, source!, StructuredCashFlowScenario.Base,
                    new BondCalculationContext(asOf, KnownAt: capturedAt, SecurityVersion: detail.Version), ct).ConfigureAwait(false);
                if (calculation.Projection.BlockedReason is { } reason)
                    issues.Add(new(side + "CashFlowBlocked", reason));
                return calculation;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
            { issues.Add(new(side + "CashFlowUnavailable", "The selected terms cannot be calculated: " + ex.Message)); return null; }
        }
    }

    private async Task EnsureScopeAsync(SecurityMasterImpactScope scope, SecurityMasterRevisionRecord revision, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.TenantId);
        if (!string.IsNullOrWhiteSpace(revision.FundProfileId))
        {
            var owner = await ownership.ResolveAsync(revision.FundProfileId, ct).ConfigureAwait(false);
            if (owner is null || !owner.IsHeldBy(scope.TenantId)
                || !string.Equals(owner.CompanyId?.Trim(), scope.CompanyId?.Trim(), StringComparison.OrdinalIgnoreCase))
                throw Refusal("ImpactScopeDenied", "The proposal's fund has no matching authenticated tenant and company ownership.");
        }
    }

    private static string TermsHash(SecurityDetailDto detail) => Hash(new
    {
        detail.AssetClass,
        detail.Currency,
        detail.Status,
        detail.EffectiveFrom,
        detail.EffectiveTo,
        Common = CanonicalJson(detail.CommonTerms),
        Asset = CanonicalJson(detail.AssetSpecificTerms)
    });
    private static string CanonicalJson(JsonElement element)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            Write(element, writer);
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        static void Write(JsonElement value, Utf8JsonWriter writer)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                { writer.WritePropertyName(property.Name); Write(property.Value, writer); }
                writer.WriteEndObject();
            }
            else if (value.ValueKind == JsonValueKind.Array)
            { writer.WriteStartArray(); foreach (var item in value.EnumerateArray()) Write(item, writer); writer.WriteEndArray(); }
            else if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
                writer.WriteRawValue(number.ToString("G29", System.Globalization.CultureInfo.InvariantCulture));
            else
                value.WriteTo(writer);
        }
    }
    private static string RevisionFingerprint(SecurityMasterRevisionRecord revision) => Hash(new
    {
        revision.RevisionId,
        revision.SecurityId,
        revision.FieldPath,
        revision.FieldValue,
        revision.FieldValueRecorded,
        revision.FieldEffectiveFrom,
        revision.FundProfileId,
        revision.BaseVersion,
        revision.FieldBaseSnapshot,
        revision.DraftedBy,
        revision.RepairBatchId,
        revision.RepairEvidenceId
    });
    private static SecurityMasterImpactPreviewException Refusal(string code, string reason) => new(code, reason);
}
