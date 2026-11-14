using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Onboarding.OpeningBalances;

namespace Meridian.FinancialOperations.Onboarding;

/// <summary>
/// Shadow-mode onboarding (W11-SHADOW-001). The shadow book runs beside the incumbent administrator or GL;
/// every period is reconciled, every difference is either resolved or explained, and only an independent
/// operator decision that attaches a verified parallel-run certificate makes the book authoritative.
/// </summary>
public sealed partial class OnboardingWorkspaceService
{
    public const string ParallelRunCertificateStatement =
        "Meridian ran in parallel with the incumbent books for every required period. Each difference listed is " +
        "resolved within tolerance or explained with retained evidence, and no unexplained difference remains.";

    public const string OpeningProviderPrefix = "opening:";

    public async Task<OnboardingWorkspaceDto> DesignateShadowAsync(
        string tenantId, string companyId, string workspaceId,
        DesignateShadowBookRequestDto request, string actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var workspace = await RequireAsync(tenantId, companyId, workspaceId, request.ExpectedVersion, ct);
        RequireOwner(workspace, actorId);
        Required(request.Rationale, "Shadow designation rationale");
        if (workspace.ShadowDesignation is not null)
            throw new OnboardingValidationException("This workspace already carries a shadow-book designation.");
        var others = await store.ListByLedgerBookAsync(tenantId, null, workspace.Scope.LedgerBookId, ct);
        if (others.Any(other => other.WorkspaceId != workspace.WorkspaceId
                && other.ShadowDesignation?.State is OnboardingShadowStates.Shadow or OnboardingShadowStates.Promoted))
            throw new OnboardingValidationException("The ledger book is already in shadow mode or promoted by another onboarding workspace.");
        var now = _clock.GetUtcNow();
        var designation = new OnboardingShadowDesignationDto(OnboardingShadowStates.Shadow, workspace.Scope.LedgerBookId,
            actorId.Trim(), now, request.Rationale.Trim());
        var next = Advance(workspace, true) with { ShadowDesignation = designation };
        next = AppendAudit(next, "ShadowDesignated", actorId, workspace.Scope.LedgerBookId.ToString("D"), request.Rationale.Trim(), now);
        return await SaveAsync(next, workspace.Version, ct);
    }

    public async Task<OnboardingWorkspaceDto> ImportOpeningBalancesAsync(
        string tenantId, string companyId, string workspaceId,
        ImportOnboardingOpeningBalancesRequestDto request, string actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var workspace = await RequireAsync(tenantId, companyId, workspaceId, request.ExpectedVersion, ct);
        RequireOwner(workspace, actorId);
        RequireShadow(workspace);
        if (openingSources is null || openingState is null)
            throw new OnboardingValidationException("Opening-balance import is unavailable in this host.");
        if (!OnboardingOpeningSourceKinds.All.Contains(request.SourceKind, StringComparer.Ordinal))
            throw new OnboardingValidationException("Select a supported opening-balance source kind.");
        Required(request.MappingProfileId, "Mapping profile identity");
        Required(request.MappingVersion, "Exact mapping version");
        var mappings = ValidateMappings(workspace, request.AccountMappings);

        var document = await openingSources.ReadAsync(Copy(workspace), request, ct);
        var state = Copy(await openingState.CaptureOpeningAsync(Copy(workspace), ct));
        var asOf = workspace.Scope.StartDate;
        var incumbentSnapshot = new OnboardingSourceSnapshotDto(
            $"incumbent-opening:{Guid.NewGuid():N}", "IncumbentOpening", document.SourceId, document.Version,
            HashSourcePayload(document.PayloadJson), _clock.GetUtcNow(), asOf, request.MappingVersion,
            document.PayloadJson, document.EvidenceIds.ToArray());

        // Map incumbent rows onto the selected financial accounts. Rows are never netted or collapsed.
        var incumbent = new Dictionary<string, (OnboardingObservationDto Key, decimal? Amount)>(StringComparer.Ordinal);
        var unmapped = new List<string>();
        foreach (var record in document.Records)
        {
            if (!mappings.TryGetValue(record.ExternalAccount, out var accountId))
            {
                if (workspace.Scope.AccountIds.Contains(record.ExternalAccount, StringComparer.Ordinal))
                    accountId = record.ExternalAccount;
                else
                {
                    if (document.RequireAllRecordsMapped)
                        unmapped.Add(record.ExternalAccount);
                    continue;
                }
            }
            var key = new OnboardingObservationDto(record.Kind, accountId, record.Currency, record.InstrumentId, null, null, [], []);
            if (!incumbent.TryAdd(DifferenceKey(key), (key, record.Amount)))
                throw new OnboardingValidationException(
                    $"The incumbent opening source repeats {record.Kind}/{record.ExternalAccount}/{record.Currency}/{record.InstrumentId}; duplicate rows cannot be netted.");
        }
        if (unmapped.Count > 0)
            throw new OnboardingValidationException(
                $"Map every incumbent account to a selected financial account: {string.Join(", ", unmapped.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(10))}.");
        if (incumbent.Count == 0)
            throw new OnboardingValidationException("No incumbent opening rows map to the selected financial accounts.");

        var snapshots = state.Snapshots.Select(snapshot => snapshot with { MappingVersion = request.MappingVersion })
            .Append(incumbentSnapshot).ToArray();
        var meridian = new Dictionary<string, OnboardingObservationDto>(StringComparer.Ordinal);
        foreach (var row in state.Observations)
        {
            if (!meridian.TryAdd(DifferenceKey(row), row))
                throw new OnboardingValidationException(
                    $"Meridian's opening records repeat {row.Kind}/{row.AccountId}/{row.Currency}/{row.InstrumentId}; duplicate rows cannot be netted.");
        }
        var observations = new List<OnboardingObservationDto>();
        foreach (var key in meridian.Keys.Union(incumbent.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            meridian.TryGetValue(key, out var ours);
            var hasTheirs = incumbent.TryGetValue(key, out var theirs);
            var template = ours ?? theirs.Key;
            var ourAmount = ours?.MeridianAmount;
            var theirAmount = hasTheirs ? theirs.Amount : null;
            var reason = ours is null ? "Meridian has no opening record for this incumbent row."
                : ours.MissingReason is not null ? ours.MissingReason
                : !hasTheirs ? "The incumbent opening source has no matching row."
                : theirAmount is null ? "The incumbent opening row carries no amount; it is not read as zero."
                : ourAmount is null ? "The Meridian opening amount is unavailable."
                : null;
            observations.Add(new(template.Kind, template.AccountId, template.Currency, template.InstrumentId,
                ourAmount, theirAmount,
                (ours?.SnapshotIds ?? []).Concat(hasTheirs ? [incumbentSnapshot.SnapshotId] : [])
                    .DefaultIfEmpty(incumbentSnapshot.SnapshotId).Distinct(StringComparer.Ordinal).ToArray(),
                (ours?.EvidenceIds ?? []).Concat(document.EvidenceIds).Distinct(StringComparer.Ordinal).ToArray(),
                reason));
        }

        var capture = new OnboardingSourceCaptureDto(snapshots, observations, state.MissingSources, null);
        var compareRequest = new CaptureOnboardingComparisonRequestDto(request.ExpectedVersion, asOf,
            OpeningProviderPrefix + request.SourceKind, document.SourceId, request.MappingProfileId, request.MappingVersion, request.Notes);
        capture = ValidateCapture(workspace, compareRequest, capture);
        var next = Advance(workspace, true);
        var comparison = BuildComparison(workspace, compareRequest, capture, actorId, Guid.NewGuid().ToString("N"),
            _clock.GetUtcNow(), next.DataRevision, isOpening: true, openingSourceKind: request.SourceKind);
        next = next with { Comparisons = [.. workspace.Comparisons, comparison] };
        next = AppendAudit(next, "OpeningBalancesImported", actorId, comparison.ComparisonId,
            $"{request.SourceKind} {incumbentSnapshot.ContentHash}", _clock.GetUtcNow());
        return await SaveAsync(next, workspace.Version, ct);
    }

    public async Task<OnboardingWorkspaceDto> ExplainDifferenceAsync(
        string tenantId, string companyId, string workspaceId, string differenceKey,
        ExplainOnboardingDifferenceRequestDto request, string actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var workspace = await RequireAsync(tenantId, companyId, workspaceId, request.ExpectedVersion, ct);
        RequireOwner(workspace, actorId);
        Required(differenceKey, "Difference identity");
        Required(request.Notes, "Explanation");
        ValidateEvidence(request.EvidenceIds, true);
        if (!OnboardingDifferenceClassifications.All.Contains(request.Classification, StringComparer.Ordinal))
            throw new OnboardingValidationException("Classify the difference as Timing, Pricing, Treatment, SourceData or MeridianError.");
        var run = workspace.Comparisons.SingleOrDefault(item => item.ComparisonId == request.ComparisonId)
            ?? throw new OnboardingValidationException("The comparison does not belong to this workspace.");
        var row = run.Differences.SingleOrDefault(item => item.DifferenceKey == differenceKey)
            ?? throw new OnboardingValidationException("The difference does not belong to the selected comparison.");
        if (row.Status == "Resolved")
            throw new OnboardingValidationException("A resolved difference needs no explanation.");
        if (row.Difference is null)
            throw new OnboardingValidationException("A missing amount is a coverage gap; supply the source instead of explaining it.");
        if (request.Classification == OnboardingDifferenceClassifications.Timing)
        {
            if (request.ExpectedReversalDate is null || request.ExpectedReversalDate < run.AsOfDate)
                throw new OnboardingValidationException("A timing difference requires an expected reversal date on or after the comparison date.");
        }
        else if (request.ExpectedReversalDate is not null)
            throw new OnboardingValidationException("Only timing differences carry an expected reversal date.");
        var now = _clock.GetUtcNow();
        var explanation = new OnboardingDifferenceExplanationDto(Guid.NewGuid().ToString("N"), run.ComparisonId, differenceKey,
            request.Classification, request.Notes.Trim(), request.EvidenceIds.ToArray(), request.ExpectedReversalDate,
            row.MeridianAmount, row.ExternalAmount, actorId.Trim(), now, workspace.DataRevision + 1);
        var next = Advance(workspace, true) with { Explanations = [.. workspace.Explanations ?? [], explanation] };
        next = AppendAudit(next, "DifferenceExplained", actorId, explanation.ExplanationId,
            $"{request.Classification} {run.AsOfDate:yyyy-MM-dd} {differenceKey}", now);
        return await SaveAsync(next, workspace.Version, ct);
    }

    public async Task<OnboardingParallelRunCertificateDto> IssueCertificateAsync(
        string tenantId, string companyId, string workspaceId,
        IssueParallelRunCertificateRequestDto request, string actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var workspace = await RequireAsync(tenantId, companyId, workspaceId, request.ExpectedVersion, ct);
        RequireOwner(workspace, actorId);
        RequireShadow(workspace);
        var readiness = CalculateReadiness(workspace);
        if (!readiness.IsReady)
            throw new OnboardingValidationException(
                $"The parallel run is not ready for certification: {string.Join(" ", readiness.Blockers)}");
        var opening = readiness.Opening!;
        var periods = readiness.Periods ?? [];
        var clean = periods.Where(period => period.IsClean).Select(period => period.AsOfDate).Order().ToArray();
        if (clean.Length == 0 || clean.Length != periods.Count)
            throw new OnboardingValidationException("Every required period must reconcile with zero unexplained differences.");
        var now = _clock.GetUtcNow();
        var packetContent = Copy(new OnboardingPacketContentDto(
            workspace.WorkspaceId, workspace.Name, workspace.OwnerId, workspace.Scope, workspace.Version,
            workspace.DataRevision, workspace.Criteria, workspace.CriteriaHistory, workspace.Comparisons,
            CurrentDifferences(workspace), workspace.Assignments, workspace.Reviews, readiness,
            ReadOnlyAuthorityPosture, NullIfEmpty(workspace.Explanations), workspace.ShadowDesignation));
        var packet = new OnboardingReadinessPacketDto(Guid.NewGuid().ToString("N"), now, actorId,
            packetContent, HashPacketContent(packetContent), "SHA-256");
        var approving = workspace.Reviews.Where(review => review.DataRevision == workspace.DataRevision && review.Decision == "Approved")
            .ToArray();
        var content = Copy(new OnboardingParallelRunCertificateContentDto(workspace.WorkspaceId, workspace.Name,
            workspace.Scope, workspace.DataRevision, opening, periods, clean, workspace.Explanations ?? [], approving,
            packet.PacketId, packet.ContentHash, ParallelRunCertificateStatement));
        var certificate = new OnboardingParallelRunCertificateDto(Guid.NewGuid().ToString("N"), now, actorId.Trim(),
            content, HashCertificateContent(content), "SHA-256");
        var next = Advance(workspace, false) with
        {
            Packets = [.. workspace.Packets, packet],
            Certificates = [.. workspace.Certificates ?? [], certificate]
        };
        next = AppendAudit(next, "CertificateIssued", actorId, certificate.CertificateId, certificate.ContentHash, now);
        await SaveAsync(next, workspace.Version, ct);
        return Copy(certificate);
    }

    public async Task<OnboardingParallelRunCertificateDto?> GetCertificateAsync(
        string tenantId, string companyId, string workspaceId, string certificateId, CancellationToken ct = default)
    {
        var workspace = await GetAsync(tenantId, companyId, workspaceId, ct);
        var certificate = workspace?.Certificates?.SingleOrDefault(item => item.CertificateId == certificateId);
        if (certificate is not null && certificate.ContentHash != HashCertificateContent(certificate.Content))
            throw new InvalidOperationException("The retained parallel-run certificate failed its content-integrity check.");
        return certificate is null ? null : Copy(certificate);
    }

    public async Task<OnboardingOpeningBalanceReportDto?> GetOpeningReportAsync(
        string tenantId, string companyId, string workspaceId, CancellationToken ct = default)
    {
        var workspace = await GetAsync(tenantId, companyId, workspaceId, ct)
            ?? throw new KeyNotFoundException("Onboarding workspace was not found in the selected enterprise scope.");
        return BuildOpeningReport(workspace);
    }

    /// <summary>
    /// The cutover decision. An independent designated reviewer promotes the shadow book only by attaching a
    /// certificate that still verifies against unchanged workspace data.
    /// </summary>
    public async Task<OnboardingWorkspaceDto> PromoteAsync(
        string tenantId, string companyId, string workspaceId,
        PromoteShadowBookRequestDto request, string actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var workspace = await RequireAsync(tenantId, companyId, workspaceId, request.ExpectedVersion, ct);
        Required(actorId, "Actor");
        if (SameActor(actorId, workspace.OwnerId) || !workspace.Criteria.RequiredReviewerIds.Any(reviewer => SameActor(reviewer, actorId)))
            throw new UnauthorizedAccessException("Promotion requires a designated reviewer independent of the onboarding owner.");
        RequireShadow(workspace);
        Required(request.Rationale, "Promotion rationale");
        ValidateEvidence(request.EvidenceIds, false);
        var certificate = workspace.Certificates?.SingleOrDefault(item => item.CertificateId == request.CertificateId)
            ?? throw new OnboardingValidationException("Attach a parallel-run certificate issued by this workspace.");
        if (certificate.ContentHash != HashCertificateContent(certificate.Content))
            throw new InvalidOperationException("The parallel-run certificate failed its content-integrity check.");
        if (certificate.Content.DataRevision != workspace.DataRevision)
            throw new OnboardingValidationException("The workspace changed after the certificate was issued. Issue a new certificate.");
        var readiness = CalculateReadiness(workspace);
        if (!readiness.IsReady)
            throw new OnboardingValidationException($"The parallel run is no longer ready: {string.Join(" ", readiness.Blockers)}");
        var now = _clock.GetUtcNow();
        var decision = new ShadowBookPromotionDecisionDto(Guid.NewGuid().ToString("N"), certificate.CertificateId,
            certificate.ContentHash, actorId.Trim(), now, request.Rationale.Trim(), request.EvidenceIds.ToArray(), workspace.DataRevision);
        var next = Advance(workspace, false) with
        {
            ShadowDesignation = workspace.ShadowDesignation! with
            {
                State = OnboardingShadowStates.Promoted,
                ResolvedBy = actorId.Trim(),
                ResolvedAtUtc = now,
                PromotionDecisionId = decision.DecisionId
            },
            Promotion = decision
        };
        next = AppendAudit(next, "ShadowPromoted", actorId, decision.DecisionId,
            $"certificate {certificate.CertificateId} {certificate.ContentHash}", now);
        return await SaveAsync(next, workspace.Version, ct);
    }

    public async Task<OnboardingWorkspaceDto> WithdrawShadowAsync(
        string tenantId, string companyId, string workspaceId,
        WithdrawShadowBookRequestDto request, string actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var workspace = await RequireAsync(tenantId, companyId, workspaceId, request.ExpectedVersion, ct);
        RequireOwner(workspace, actorId);
        RequireShadow(workspace);
        Required(request.Reason, "Withdrawal reason");
        var now = _clock.GetUtcNow();
        var next = Advance(workspace, false) with
        {
            ShadowDesignation = workspace.ShadowDesignation! with
            {
                State = OnboardingShadowStates.Withdrawn,
                ResolvedBy = actorId.Trim(),
                ResolvedAtUtc = now,
                WithdrawalReason = request.Reason.Trim()
            }
        };
        next = AppendAudit(next, "ShadowWithdrawn", actorId, workspace.Scope.LedgerBookId.ToString("D"), request.Reason.Trim(), now);
        return await SaveAsync(next, workspace.Version, ct);
    }

    public static string HashCertificateContent(OnboardingParallelRunCertificateContentDto content) => Hash(content);

    public static bool VerifyShadowAudit(IReadOnlyList<OnboardingShadowAuditEntryDto>? entries)
    {
        string? previous = null;
        var sequence = 1;
        foreach (var entry in entries ?? [])
        {
            if (entry.Sequence != sequence++ || entry.PreviousHash != previous || entry.EntryHash != Hash(entry with { EntryHash = "" }))
                return false;
            previous = entry.EntryHash;
        }
        return true;
    }

    private static OnboardingWorkspaceDto AppendAudit(
        OnboardingWorkspaceDto workspace, string action, string actorId, string subjectId, string detail, DateTimeOffset now)
    {
        var audit = workspace.ShadowAudit ?? [];
        var entry = new OnboardingShadowAuditEntryDto(audit.Count + 1, action, actorId.Trim(), now, subjectId, detail,
            audit.LastOrDefault()?.EntryHash, "");
        return workspace with { ShadowAudit = [.. audit, entry with { EntryHash = Hash(entry) }] };
    }

    private static void RequireShadow(OnboardingWorkspaceDto workspace)
    {
        if (workspace.ShadowDesignation?.State != OnboardingShadowStates.Shadow)
            throw new OnboardingValidationException("Designate the ledger book as a shadow book first.");
    }

    private static Dictionary<string, string> ValidateMappings(
        OnboardingWorkspaceDto workspace, IReadOnlyList<OnboardingOpeningAccountMappingDto>? mappings)
    {
        if (mappings is null)
            throw new OnboardingValidationException("Provide the incumbent-to-Meridian account mapping (it may be empty).");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var mapping in mappings)
        {
            Required(mapping?.ExternalAccount, "Incumbent account");
            Required(mapping!.AccountId, "Mapped financial account");
            var accountId = Guid.TryParse(mapping.AccountId, out var parsed) ? parsed.ToString("D") : mapping.AccountId;
            if (!workspace.Scope.AccountIds.Contains(accountId, StringComparer.Ordinal))
                throw new OnboardingValidationException("Every mapped account must belong to the selected financial account population.");
            if (!result.TryAdd(mapping.ExternalAccount.Trim(), accountId))
                throw new OnboardingValidationException("Each incumbent account may be mapped only once.");
        }
        return result;
    }

    private static IEnumerable<OnboardingComparisonDto> PeriodComparisons(OnboardingWorkspaceDto workspace)
        => workspace.Comparisons.Where(run => !run.IsOpening);

    /// <summary>The latest explanation recorded against these exact amounts, if it still clears the difference.</summary>
    private static OnboardingDifferenceExplanationDto? ApplicableExplanation(
        OnboardingWorkspaceDto workspace, OnboardingComparisonDto run, OnboardingDifferenceDto row)
    {
        if (row.Status == "Resolved" || row.Difference is null || workspace.Explanations is not { Count: > 0 } explanations)
            return null;
        var openingRuns = workspace.Comparisons.Where(item => item.IsOpening).Select(item => item.ComparisonId)
            .ToHashSet(StringComparer.Ordinal);
        var latest = explanations.LastOrDefault(item => item.DifferenceKey == row.DifferenceKey
            && openingRuns.Contains(item.ComparisonId) == run.IsOpening
            && item.MeridianAmount == row.MeridianAmount && item.ExternalAmount == row.ExternalAmount);
        if (latest is null || latest.Classification == OnboardingDifferenceClassifications.MeridianError)
            return null;
        if (latest.Classification == OnboardingDifferenceClassifications.Timing
            && (latest.ExpectedReversalDate is null || run.AsOfDate > latest.ExpectedReversalDate))
            return null;
        return latest;
    }

    private static OnboardingPeriodSummaryDto SummarizePeriod(OnboardingWorkspaceDto workspace, OnboardingComparisonDto run)
    {
        var breaks = run.Differences.Where(row => row.Status != "Resolved").ToArray();
        var explained = breaks.Select(row => ApplicableExplanation(workspace, run, row)).Where(item => item is not null).ToArray();
        var breakKeys = breaks.Select(row => row.DifferenceKey).ToHashSet(StringComparer.Ordinal);
        var matched = run.Inputs.Observations.Count(row => !breakKeys.Contains(DifferenceKey(row)));
        var navs = run.Inputs.Observations.Where(row => row.Kind == "Nav").ToArray();
        var navCurrency = navs.Select(row => row.Currency).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1 ? navs[0].Currency : null;
        var navComplete = navCurrency is not null && navs.All(row => row.MeridianAmount.HasValue && row.ExternalAmount.HasValue);
        decimal? meridianNav = navComplete ? navs.Sum(row => row.MeridianAmount!.Value) : null;
        decimal? externalNav = navComplete ? navs.Sum(row => row.ExternalAmount!.Value) : null;
        var unexplained = breaks.Length - explained.Length;
        var clean = unexplained == 0 && run.Inputs.MissingSources.Count == 0
            && run.CoveragePercent >= workspace.Criteria.MinimumCoveragePercent
            && Hash(run.Criteria) == Hash(workspace.Criteria)
            && (!workspace.Criteria.RequireCloseReadiness || run.Inputs.CloseReadiness is { IsComplete: true, IsReadyToClose: true });
        return new(run.AsOfDate, run.ComparisonId, navComplete ? navCurrency : null, meridianNav, externalNav,
            meridianNav - externalNav, run.Inputs.Observations.Count, matched, explained.Length, unexplained,
            run.Inputs.MissingSources.Count,
            explained.GroupBy(item => item!.Classification, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new OnboardingClassificationCountDto(group.Key, group.Count())).ToArray(),
            clean);
    }

    private static OnboardingOpeningBalanceReportDto? BuildOpeningReport(OnboardingWorkspaceDto workspace)
    {
        var run = workspace.Comparisons.Where(item => item.IsOpening).MaxBy(item => item.Sequence);
        if (run is null)
            return null;
        var differences = run.Differences.ToDictionary(row => row.DifferenceKey, StringComparer.Ordinal);
        var lines = new List<OnboardingOpeningBalanceLineDto>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in run.Inputs.Observations)
        {
            var key = DifferenceKey(row);
            seen.Add(key);
            differences.TryGetValue(key, out var difference);
            lines.Add(OpeningLine(workspace, run, key, row.Kind, row.AccountId, row.Currency, row.InstrumentId,
                row.MeridianAmount, row.ExternalAmount, difference, row.MissingReason));
        }
        foreach (var row in run.Differences.Where(item => !seen.Contains(item.DifferenceKey)))
            lines.Add(OpeningLine(workspace, run, row.DifferenceKey, row.Kind, row.AccountId, row.Currency, row.InstrumentId,
                row.MeridianAmount, row.ExternalAmount, row, row.MissingReason));
        lines = lines.OrderBy(line => line.Kind, StringComparer.Ordinal).ThenBy(line => line.AccountId, StringComparer.Ordinal)
            .ThenBy(line => line.InstrumentId, StringComparer.Ordinal).ThenBy(line => line.Currency, StringComparer.Ordinal).ToList();
        var incumbent = run.Inputs.Snapshots.LastOrDefault(snapshot => snapshot.SourceKind == "IncumbentOpening");
        var matched = lines.Count(line => line.Status == "Matched");
        var explained = lines.Count(line => line.Status == "Explained");
        var breaks = lines.Count(line => line.Status == "Break");
        var missing = lines.Count(line => line.Status == "Missing");
        var complete = run.Inputs.MissingSources.Count == 0
            && Coverage(workspace, run.Inputs) >= workspace.Criteria.MinimumCoveragePercent
            && Hash(run.Criteria) == Hash(workspace.Criteria);
        return new(run.ComparisonId, run.OpeningSourceKind ?? "", run.AsOfDate, incumbent?.ContentHash ?? "",
            matched, explained, breaks, missing, complete && lines.Count > 0 && breaks == 0 && missing == 0, lines,
            run.Inputs.Snapshots.SelectMany(snapshot => snapshot.EvidenceIds.Append(snapshot.SnapshotId))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }

    private static OnboardingOpeningBalanceLineDto OpeningLine(
        OnboardingWorkspaceDto workspace, OnboardingComparisonDto run, string key, string kind, string accountId,
        string currency, string? instrumentId, decimal? meridian, decimal? incumbent,
        OnboardingDifferenceDto? difference, string? missingReason)
    {
        var tolerance = Tolerance(run.Criteria, kind);
        var explanation = difference is null ? null : ApplicableExplanation(workspace, run, difference);
        var status = !string.IsNullOrWhiteSpace(missingReason) || meridian is null || incumbent is null ? "Missing"
            : difference is null || difference.Status == "Resolved" ? "Matched"
            : explanation is not null ? "Explained" : "Break";
        return new(key, kind, accountId, currency, instrumentId, meridian, incumbent,
            meridian.HasValue && incumbent.HasValue ? meridian - incumbent : null, tolerance, status,
            explanation?.Classification, status == "Missing" ? missingReason ?? "A comparison amount is missing." : null);
    }

    private static IReadOnlyList<T>? NullIfEmpty<T>(IReadOnlyList<T>? values) => values is { Count: > 0 } ? values : null;
}
