using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Reporting;

namespace Meridian.Ui.Shared.Services;

/// <summary>Tenant-scoped comparisons retained through the existing immutable artifact store.</summary>
public sealed class ReportingIncomeComparisonService(
    IReportingRunStore runs,
    IReportingArtifactStore artifacts,
    IReportingGovernanceRepository governance)
{
    private static readonly JsonSerializerOptions RetentionJson = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<ReportingIncomeComparisonRunDto>> ListCandidatesAsync(
        ReportAccessQueryContext access, CancellationToken ct = default)
    {
        RequireBound(access);
        var candidates = new List<ReportingOutputManifest>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var offset = 0; ; offset += 200)
        {
            var page = runs.ListRuns(access.TenantId!, access.CompanyId, offset, limit: 200);
            foreach (var snapshot in page)
            {
                var manifest = snapshot.Manifest;
                if (!seen.Add(manifest.RunId))
                    throw new InvalidDataException("Retained run pagination returned duplicate identities.");
                if (!manifest.RenderedReportWriterGrids.IsDefaultOrEmpty && ReportAccessPolicyEvaluator.Evaluate(manifest, access).IsAccessible)
                    candidates.Add(manifest);
            }
            if (page.Count < 200)
                break;
        }
        var result = new List<ReportingIncomeComparisonRunDto>();
        foreach (var manifest in candidates)
        {
            var governed = await GovernedAsync(access.TenantId!, manifest.RunId, ct).ConfigureAwait(false);
            result.Add(ReportingIncomeComparisonEngine.Describe(manifest, governed));
        }
        return result;
    }

    public async Task<ReportingIncomeComparisonDto> CreateAsync(
        ReportingIncomeComparisonRequestDto request, ReportAccessQueryContext access, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireBound(access);
        var baseline = AuthorizedManifest(request.BaselineRunId, access);
        var current = AuthorizedManifest(request.CurrentRunId, access);
        var baselineState = await GovernedAsync(access.TenantId!, baseline.RunId, ct).ConfigureAwait(false);
        var currentState = await GovernedAsync(access.TenantId!, current.RunId, ct).ConfigureAwait(false);
        var retained = ReportingIncomeComparisonEngine.Compare(baseline, current, request.GridId, request.MetricColumn,
            ReportingIncomeComparisonEngine.Describe(baseline, baselineState),
            ReportingIncomeComparisonEngine.Describe(current, currentState));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(retained, RetentionJson);
        var written = await artifacts.StoreAsync(new(access.TenantId!, bytes), ct).ConfigureAwait(false);
        var expectedHash = Sha256Digest.Compute(bytes);
        if (written.Identity.TenantId != access.TenantId || written.ByteSize != bytes.Length
            || !Sha256Digest.FixedEquals(expectedHash, written.Identity.ContentHashSha256))
            throw new InvalidDataException("The comparison artifact receipt does not match the retained input bytes.");
        // Never return a retained receipt until the exact immutable bytes have been re-read.
        var verified = await ReadAsync(expectedHash, access, ct).ConfigureAwait(false);
        return verified.Comparison;
    }

    public async Task<ReportingIncomeComparisonDto> GetAsync(
        string comparisonId, ReportAccessQueryContext access, CancellationToken ct = default) =>
        (await ReadAsync(comparisonId, access, ct).ConfigureAwait(false)).Comparison;

    public async Task<ReportingIncomeContributionSupportDto> GetSupportAsync(
        string comparisonId, string contributionId, ReportAccessQueryContext access, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contributionId);
        var retained = await ReadAsync(comparisonId, access, ct).ConfigureAwait(false);
        return retained.Support.SingleOrDefault(s => s.Contribution.ContributionId == contributionId)
            ?? throw new KeyNotFoundException("The retained comparison contribution was not found.");
    }

    private async Task<RetainedReportingIncomeComparison> ReadAsync(
        string comparisonId, ReportAccessQueryContext access, CancellationToken ct)
    {
        RequireBound(access);
        if (!Sha256Digest.IsCanonical(comparisonId))
            throw new ArgumentException("A canonical retained comparison identity is required.");
        var artifact = await artifacts.ReadAsync(new(access.TenantId!, comparisonId), ct).ConfigureAwait(false);
        if (artifact.Identity.TenantId != access.TenantId || artifact.Identity.ContentHashSha256 != comparisonId
            || artifact.ByteSize != artifact.Content.LongLength
            || !Sha256Digest.FixedEquals(comparisonId, Sha256Digest.Compute(artifact.Content)))
            throw new InvalidDataException("The retained comparison failed content integrity verification.");
        RetainedReportingIncomeComparison retained;
        try
        {
            retained = JsonSerializer.Deserialize<RetainedReportingIncomeComparison>(artifact.Content, RetentionJson)
                ?? throw new JsonException("Empty comparison.");
        }
        catch (JsonException)
        {
            throw new KeyNotFoundException("The artifact is not a retained income comparison.");
        }
        if (retained.Format != ReportingIncomeComparisonEngine.ExplanationVersion || retained.Comparison is null
            || retained.BaselineManifest is null || retained.CurrentManifest is null || retained.Support is null
            || retained.Comparison.Baseline is null || retained.Comparison.Current is null
            || retained.Comparison.ExplanationVersion != retained.Format
            || retained.Comparison.Baseline.RunId != retained.BaselineManifest.RunId
            || retained.Comparison.Current.RunId != retained.CurrentManifest.RunId
            || retained.Comparison.Contributions is null
            || retained.Support.Any(s => s is null || s.Contribution is null || s.BaselineRecords is null || s.CurrentRecords is null
                || s.BaselineRunId != retained.BaselineManifest.RunId || s.CurrentRunId != retained.CurrentManifest.RunId)
            || retained.Comparison.Contributions.Any(c => c is null)
            || retained.Support.Count != retained.Comparison.Contributions.Count
            || retained.Support.Select(s => s.Contribution.ContributionId).Distinct(StringComparer.Ordinal).Count() != retained.Support.Count
            || retained.Support.Any(s => !retained.Comparison.Contributions.Contains(s.Contribution)))
            throw new KeyNotFoundException("The artifact is not a supported retained income comparison.");
        Authorize(retained.BaselineManifest, access);
        Authorize(retained.CurrentManifest, access);
        return retained with
        {
            Comparison = retained.Comparison with { ComparisonId = comparisonId },
            Support = retained.Support.Select(s => s with { ComparisonId = comparisonId }).ToArray()
        };
    }

    private ReportingOutputManifest AuthorizedManifest(string runId, ReportAccessQueryContext access)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        var manifest = runs.GetManifest(access.TenantId!, runId.Trim())
            ?? throw new KeyNotFoundException("The retained report run was not found.");
        Authorize(manifest, access);
        return manifest;
    }

    private static void Authorize(ReportingOutputManifest manifest, ReportAccessQueryContext access)
    {
        var evaluation = ReportAccessPolicyEvaluator.Evaluate(manifest, access);
        if (!evaluation.IsAccessible)
            throw new UnauthorizedAccessException(evaluation.Reason);
    }

    private ValueTask<GovernedReportingRun?> GovernedAsync(string tenant, string runId, CancellationToken ct) =>
        governance.ExecuteTransactionAsync((transaction, token) => transaction.GetRunAsync(tenant, runId, token), ct);

    private static void RequireBound(ReportAccessQueryContext access)
    {
        ArgumentNullException.ThrowIfNull(access);
        if (!access.RequireBoundScope || string.IsNullOrWhiteSpace(access.TenantId)
            || string.IsNullOrWhiteSpace(access.CompanyId) || string.IsNullOrWhiteSpace(access.ActorPrincipalId))
            throw new UnauthorizedAccessException("Income comparisons require authenticated tenant, company, and actor scope.");
    }
}
