using System.Globalization;
using System.Text.Json;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Services;

/// <summary>Reads only authorized released-run inputs, with no historical reconstruction fallback.</summary>
public sealed class ReleasedReportSecurityInputReadService(
    IReportingRunStore? runs, IReportingGovernanceRepository? authority,
    ISecurityMasterQueryService? securities, Func<ReportAccessQueryContext?> currentAccess,
    SecurityReportInputAttributionResolver? attribution = null)
    : ISecurityMasterReleasedReportImpactQuery
{
    private sealed record ReleasedInput(ReportingOutputManifest Manifest, GovernedReportingRun Run);

    public async Task<bool> CanReadRetainedRunIdsAsync(IReadOnlyList<string> runIds, SecurityMasterImpactScope scope,
        CancellationToken ct = default)
    {
        var access = currentAccess();
        if (!Bound(access) || access!.TenantId != scope.TenantId || access.CompanyId != scope.CompanyId)
            return false;
        foreach (var runId in runIds.Distinct(StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(runId))
                return false;
            var matches = await ReadAccessibleReleasesAsync(access, runId, [], ct).ConfigureAwait(false);
            if (matches.Count != 1)
                return false;
        }
        return true;
    }

    public static ReportAccessQueryContext? ResolveCurrentAccess(IServiceProvider services)
    {
        var context = services.GetService<IHttpContextAccessor>()?.HttpContext;
        return context is not null && EndpointAuthorization.HasAnyPermission(context,
            UserPermission.ViewReporting, UserPermission.ManageReporting, UserPermission.ApproveReporting,
            UserPermission.DeliverReporting, UserPermission.AdminMaintenance)
                ? ReportingDeliveryReadModelSecurity.ResolveCurrentAccessContext(services) : null;
    }

    public async Task<IReadOnlyList<SecurityReportInputLinkDto>> GetRunLinksAsync(string runId, CancellationToken ct = default)
    {
        var released = await ReadAccessibleReleasesAsync(RequireAccess(), runId, [], ct).ConfigureAwait(false);
        if (released.Count != 1)
            throw new KeyNotFoundException("The released report is unavailable in the selected scope.");
        if (released[0].Manifest.AuthoritativeSource is not { LedgerPopulation: not null } source)
            return [];
        var population = ReportingLedgerPopulationSnapshot.Decode(source);
        var visible = VisibleJournalIds(released[0].Manifest, population);
        return population.Journals.Where(record => visible.Contains(record.Entry.JournalEntryId) && record.Entry.Metadata.SecurityId.HasValue)
            .Select(record => new SecurityReportInputLinkDto(record.Entry.Metadata.SecurityId!.Value,
                record.Entry.JournalEntryId, source.SecurityInputs?.Bindings.Any(binding => binding.JournalEntryId == record.Entry.JournalEntryId) == true)).ToArray();
    }

    public async Task<SecurityReportInputCompareDto> GetComparisonsAsync(Guid securityId, string? runId = null,
        CancellationToken ct = default)
    {
        var access = RequireAccess();
        var gaps = new List<string>();
        var released = await ReadAccessibleReleasesAsync(access, runId, gaps, ct).ConfigureAwait(false);
        if (runId is not null && released.Count == 0)
            throw new KeyNotFoundException("The released report is unavailable in the selected scope.");
        var comparisons = new List<SecurityReportInputCompareRunDto>();
        foreach (var item in released)
        {
            var manifest = item.Manifest;
            var reportGaps = new List<string>();
            IReadOnlyList<SecurityRetainedReportInputDto> inputs = [];
            var mentionsSecurity = false;
            if (manifest.AuthoritativeSource is { } checkpoint && checkpoint.LedgerPopulation is not null)
            {
                var population = ReportingLedgerPopulationSnapshot.Decode(checkpoint);
                var visible = VisibleJournalIds(manifest, population);
                mentionsSecurity = population.Journals.Any(record => visible.Contains(record.Entry.JournalEntryId) && record.Entry.Metadata.SecurityId == securityId);
                if (checkpoint.SecurityInputs is { } retained)
                {
                    var projected = new List<SecurityRetainedReportInputDto>();
                    foreach (var binding in retained.Bindings.Where(binding => visible.Contains(binding.JournalEntryId) && binding.SecurityId == securityId))
                    {
                        var input = Project(binding);
                        if (attribution is not null)
                            input = input with { Attribution = await attribution.ResolveAsync(input.Security, access.TenantId!, access.CompanyId!, ct).ConfigureAwait(false) };
                        projected.Add(input);
                    }
                    inputs = projected;
                    reportGaps.AddRange(retained.Gaps.Where(gap => visible.Contains(gap.JournalEntryId) && gap.SecurityId == securityId).Select(static gap => gap.Reason));
                }
                else if (mentionsSecurity || runId is not null)
                    reportGaps.Add("Exact Security Master calculation inputs were not retained for this legacy report.");
            }
            else
            {
                if (runId is not null)
                    reportGaps.Add("This legacy report has no exact retained ledger/input population.");
                else
                    gaps.Add("Some accessible legacy reports have no retained input population; their Security Master usage is unavailable.");
            }
            if (runId is not null || mentionsSecurity || inputs.Count > 0)
                comparisons.Add(new(manifest.RunId, manifest.AsOfDate,
                    manifest.AuthoritativeSource?.CutoffUtc ?? item.Run.Snapshot.CapturedAtUtc,
                    item.Run.Release!.ReleasedAtUtc, manifest.AuthoritativeSource?.CheckpointHash, inputs, reportGaps));
        }
        var current = securities is null ? null : await securities.GetByIdAsync(securityId, ct).ConfigureAwait(false);
        if (current is null)
            gaps.Add("Current canonical Security Master terms are unavailable.");
        return new(securityId, current, current is null ? [] : Rates(current), comparisons, gaps.Distinct().ToArray());
    }

    public async Task<SecurityReleasedReportImpact> EvaluateAsync(Guid securityId, string? fundProfileId,
        DateTimeOffset effectiveFrom, SecurityMasterImpactScope scope, CancellationToken ct = default)
    {
        if (runs is null || authority is null)
            return new(SecurityImpactCompleteness.Unavailable, [], "", [new("released-report-authority-unavailable", "Released reporting authority is unavailable.")]);
        var access = currentAccess();
        if (!Bound(access) || access!.TenantId != scope.TenantId || access.CompanyId != scope.CompanyId)
            return new(SecurityImpactCompleteness.Unavailable, [], "", [new("report-input-access-unavailable", "Bound current-session report access is unavailable.")]);
        var gaps = new List<string>();
        var released = await ReadAccessibleReleasesAsync(access, null, gaps, ct).ConfigureAwait(false);
        var affected = new List<ReleasedInput>();
        foreach (var item in released)
        {
            if (fundProfileId is not null && item.Manifest.OperationalScope?.FundId != fundProfileId)
                continue;
            if (item.Manifest.AsOfDate < DateOnly.FromDateTime(effectiveFrom.UtcDateTime))
                continue;
            if (item.Manifest.AuthoritativeSource is not { LedgerPopulation: not null } checkpoint)
            {
                gaps.Add("Legacy released reports without exact retained inputs cannot be proven unaffected.");
                continue;
            }
            var population = ReportingLedgerPopulationSnapshot.Decode(checkpoint);
            var visible = VisibleJournalIds(item.Manifest, population);
            if (population.Journals.Any(record => visible.Contains(record.Entry.JournalEntryId) && record.Entry.Metadata.SecurityId == securityId))
            {
                affected.Add(item);
                if (checkpoint.SecurityInputs is null || checkpoint.SecurityInputs.Gaps.Any(gap => visible.Contains(gap.JournalEntryId) && gap.SecurityId == securityId))
                    gaps.Add("Some affected reports lack exact calculation-input provenance.");
            }
        }
        // Report release is not fenced by a Security Master approval. A scoped exhaustive read is
        // useful impact evidence but cannot promise that no report was released after that read.
        gaps.Add("Released-report impact is informational; concurrent report release is not fenced by Security Master approval.");
        var ids = affected.Select(static item => item.Manifest.RunId).Distinct().Order(StringComparer.Ordinal).ToArray();
        var fingerprint = Sha256Digest.ComputeUtf8(string.Join("\n", affected.OrderBy(static item => item.Manifest.RunId, StringComparer.Ordinal)
            .Select(static item => $"{item.Manifest.RunId}|{item.Run.Version}|{item.Manifest.AuthoritativeSource?.CheckpointHash}")));
        return new(SecurityImpactCompleteness.Partial, ids, fingerprint,
            gaps.Distinct().Select(static reason => new SecurityImpactIssue("released-report-impact-partial", reason)).ToArray());
    }

    private async Task<IReadOnlyList<ReleasedInput>> ReadAccessibleReleasesAsync(ReportAccessQueryContext access,
        string? runId, List<string> gaps, CancellationToken ct)
    {
        if (runs is null || authority is null)
        { gaps.Add("Released reporting authority is unavailable."); return []; }
        var candidates = new List<ReportingOutputManifest>();
        if (runId is not null)
        {
            var manifest = runs.GetManifest(access.TenantId!, runId);
            if (manifest is not null)
                candidates.Add(manifest);
        }
        else
        {
            if (!runs.SupportsCompleteScopedPaging)
                gaps.Add("This reporting store cannot guarantee complete scoped enumeration.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var offset = 0; ; offset = checked(offset + 200))
            {
                ct.ThrowIfCancellationRequested();
                var page = runs.ListRuns(access.TenantId!, access.CompanyId, offset, 200);
                if (page.Count == 0)
                    break;
                foreach (var snapshot in page)
                {
                    if (!seen.Add(snapshot.Manifest.RunId))
                        throw new InvalidDataException("Reporting run paging did not advance uniquely.");
                    candidates.Add(snapshot.Manifest);
                }
                if (page.Count < 200)
                    break;
            }
        }
        var released = new List<ReleasedInput>();
        foreach (var manifest in candidates)
        {
            if (manifest.OperationalScope?.TenantId != access.TenantId || manifest.OperationalScope.CompanyId != access.CompanyId
                || !ReportAccessPolicyEvaluator.Evaluate(manifest, access).IsAccessible)
            { gaps.Add("The visible report set is limited by current-session access."); continue; }
            var run = await authority.ExecuteTransactionAsync((transaction, token) =>
                transaction.GetRunAsync(access.TenantId!, manifest.RunId, token), ct).ConfigureAwait(false);
            if (run?.GovernanceState != GovernedReportingState.Released || run.Release is null)
                continue;
            if (run.Scope.TenantId != access.TenantId || run.Scope.CompanyId != access.CompanyId
                || run.Snapshot.SnapshotHash != manifest.CertifiedSnapshot?.SnapshotHash
                || run.Snapshot.SourceCheckpointHash != manifest.AuthoritativeSource?.CheckpointHash
                || run.Access.PolicyHash != manifest.ImmutableAccessScope?.PolicyHash)
                throw new InvalidDataException("Released report authority contradicts its retained source or access binding.");
            ReportingCertifiedManifestValidation.Validate(manifest);
            released.Add(new(manifest, run));
        }
        return released;
    }

    private ReportAccessQueryContext RequireAccess()
        => currentAccess() is { } access && Bound(access) ? access
            : throw new UnauthorizedAccessException("Bound current-session report access is required.");
    private static bool Bound(ReportAccessQueryContext? access)
        => access?.RequireBoundScope == true && !string.IsNullOrWhiteSpace(access.ActorPrincipalId)
            && !string.IsNullOrWhiteSpace(access.TenantId) && !string.IsNullOrWhiteSpace(access.CompanyId);

    // The private checkpoint contains whole-book history. Access to one report authorizes only
    // journal support actually represented by its certified rows or its replay-verified amounts.
    private static HashSet<Guid> VisibleJournalIds(ReportingOutputManifest manifest, ReportingLedgerPopulationSnapshot population)
    {
        var visible = new HashSet<Guid>();
        foreach (var row in manifest.CertifiedDatasetRows)
            if (row.TryGetValue("journalEntryId", out var value) && Guid.TryParse(value, out var journalId))
                visible.Add(journalId);
        if (!population.ReportAmounts.IsDefaultOrEmpty)
        {
            if (!ReportAmountBindingBuilder.MatchesRetained(population))
                throw new InvalidDataException("Retained report amount support cannot be verified.");
            visible.UnionWith(population.ReportAmounts.SelectMany(static amount => amount.JournalEntryIds));
        }
        var retainedIds = population.Journals.Select(static record => record.Entry.JournalEntryId).ToHashSet();
        if (!visible.IsSubsetOf(retainedIds) || visible.Count == 0 && !manifest.CertifiedDatasetRows.IsDefaultOrEmpty)
            throw new InvalidDataException("Exact visible report journal bindings are unavailable.");
        return visible;
    }

    private static SecurityRetainedReportInputDto Project(ReportingSecurityInputBinding binding)
    {
        using var payload = JsonDocument.Parse(binding.ReceiptPayloadJson);
        var root = payload.RootElement;
        var security = root.GetProperty("Security").Deserialize<SecurityDetailDto>()!;
        return new(binding.JournalEntryId, binding.ReceiptSha256, security, root.GetProperty("ModelVersion").GetString()!,
            root.GetProperty("Context").Clone(), root.GetProperty("Source").Clone(), Rates(security, retainedInput: true),
            "Change attribution is a supplemental audit lookup performed now against the receipt's exact security version. It does not establish what identities or approvals were visible at report time.");
    }

    public static IReadOnlyList<SecurityReportRateValueDto> Rates(SecurityDetailDto security, bool retainedInput = false)
    {
        // Only frozen receipt display may classify an old untagged document. Keep the exact
        // Security DTO returned to the auditor, and its receipt digest, over the original bytes.
        var terms = retainedInput
            ? RetainedSecurityTermsReadBoundary.ReadTerms(security.AssetClass, security.AssetSpecificTerms)
            : security.AssetSpecificTerms;
        _ = SecurityTermsEnvelope.ResolveFormat(terms);
        SecurityTermsEnvelope.EnsureReadableAsAssetSpecificTerms(security.AssetClass, terms);
        RateUnit? declared = SecurityRecordRateUnit.Read(security.AssetClass, terms);
        try
        {
            var conventions = SecurityVersionedTerms.ReadDeclaredScheduleConventions(security.AssetClass, terms);
            declared ??= SecurityRecordRateUnit.Parse(conventions.Block.CouponRateUnit);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) { declared = RateUnit.Unrecognized; }
        if (terms.ValueKind != JsonValueKind.Object)
            return [];
        return SecurityRateFieldUnits.All.Where(field => field.AssetClass == security.AssetClass && !field.FieldKey.Contains('.'))
            .Where(field => terms.TryGetProperty(field.FieldKey, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out _))
            .Select(field =>
            {
                var raw = terms.GetProperty(field.FieldKey);
                var resolved = RateUnitNormalizer.Normalize(field, raw.GetDecimal(), declared);
                if (retainedInput && resolved is RateUnitResolution.Resolved { Source: RateUnitSource.FieldDefault })
                    return new SecurityReportRateValueDto(field.FieldKey, raw.GetRawText(), null, null,
                        "A historical field-default unit was not pinned in this receipt; no current default was substituted.");
                return resolved is RateUnitResolution.Resolved rate
                    ? new SecurityReportRateValueDto(field.FieldKey, raw.GetRawText(), $"{rate.Unit} ({rate.Source})", rate.Fraction.ToString(CultureInfo.InvariantCulture), null)
                    : new SecurityReportRateValueDto(field.FieldKey, raw.GetRawText(), null, null,
                        resolved is RateUnitResolution.OutOfRange range ? range.Reason : "No supported unit is declared; no conversion was inferred.");
            }).ToArray();
    }
}
