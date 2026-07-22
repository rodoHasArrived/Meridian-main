using Meridian.Domain.Reconciliation;
using Meridian.Infrastructure.Reconciliation;

namespace Meridian.FinancialOperations.Reconciliation;

public sealed class StatementRunWorkflowService(
    ICanonicalStatementStore importStore,
    IReconciliationCaseStore caseStore,
    IReconciliationBreakStore breakStore,
    IBrokerStatementService brokerStatementService,
    IStatementReconciliationValidationService validationService,
    IInternalReconciliationPopulationProvider? populationProvider = null,
    IReconciliationFxRateProvider? fxRateProvider = null,
    IStatementToleranceProfileProvider? toleranceProfileProvider = null) : IStatementRunWorkflowService
{
    // The internal book to reconcile against and the FX seam used to normalize foreign-currency
    // lines. Both default to safe, fail-closed implementations: an empty book (every row becomes a
    // break) and identity-only FX (same-currency reconciles exactly, cross-currency breaks) until a
    // deployment wires real populations and rates.
    private readonly IInternalReconciliationPopulationProvider _populationProvider =
        populationProvider ?? EmptyInternalReconciliationPopulationProvider.Instance;
    private readonly IReconciliationFxRateProvider _fxRateProvider =
        fxRateProvider ?? IdentityReconciliationFxRateProvider.Instance;

    // Resolves the tolerance thresholds for the run's selected profile. Defaults to a provider that
    // knows only the built-in default profile; a deployment registers a provider carrying its operator
    // profiles so a run configured with a non-default profile is matched with that profile's thresholds
    // rather than silently using the defaults.
    private readonly IStatementToleranceProfileProvider _toleranceProfileProvider =
        toleranceProfileProvider ?? new InMemoryStatementToleranceProfileProvider();

    public Task<IReadOnlyList<CanonicalStatementImport>> ListImportsAsync(CancellationToken cancellationToken = default)
        => importStore.ListImportsAsync(cancellationToken);

    public async Task<StatementRunWorkflowResult> CreateAsync(StatementRunRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedRequest = await NormalizeAndValidateAsync(request, cancellationToken).ConfigureAwait(false);
        // Resolve the selected tolerance profile before committing the import. An unknown profile must
        // fail the run before ImportAsync persists it; otherwise the stored import would be left with no
        // breaks or cases and the duplicate-source guard would reject a corrected retry of the same file.
        var toleranceProfile = await ResolveToleranceProfileAsync(normalizedRequest.ToleranceProfileId, cancellationToken).ConfigureAwait(false);
        var importRequest = ToImportRequest(normalizedRequest);
        var importValidation = await brokerStatementService.ValidateAsync(importRequest, cancellationToken).ConfigureAwait(false);
        if (!importValidation.IsValid)
        {
            throw new InvalidDataException($"Statement cannot be imported: {string.Join(" ", importValidation.Errors)}");
        }

        var imported = await brokerStatementService.ImportAsync(importRequest, cancellationToken).ConfigureAwait(false);

        // Reconcile the imported statement against Meridian's own book. The population provider
        // supplies the internal positions, cash, and ledger for this fund account and period; the
        // matching engine then compares statement rows to those records (not to themselves) across
        // exact, tolerance, candidate, and unmatched tiers.
        var baseCurrency = StatementRunMatcher.DefaultBaseCurrency;
        var populations = await _populationProvider
            .GetPopulationsAsync(
                new InternalReconciliationPopulationContext(
                    imported.Import.FundAccountId,
                    imported.Import.ExternalAccountId,
                    imported.Import.StatementPeriodStart,
                    imported.Import.StatementPeriodEnd,
                    baseCurrency),
                cancellationToken)
            .ConfigureAwait(false);

        var createdAtUtc = DateTimeOffset.UtcNow;
        var matchResult = StatementRunMatcher.Match(
            imported.Import,
            imported.Rows,
            populations,
            toleranceProfile,
            _fxRateProvider,
            baseCurrency,
            createdAtUtc);

        var linkedBreaks = matchResult.Breaks
            .Select(static item => item with { Record = item.Record with { EvidenceLink = BuildEvidenceLink(item) } })
            .ToArray();
        var breaks = linkedBreaks.Select(static item => item.Record).ToArray();
        await breakStore.WriteAsync(breaks, cancellationToken).ConfigureAwait(false);

        var cases = BuildStatementCases(imported.Import, linkedBreaks, normalizedRequest.ImportedBy);
        foreach (var reconciliationCase in cases)
        {
            await caseStore.SaveAsync(reconciliationCase, cancellationToken).ConfigureAwait(false);
        }

        return new StatementRunWorkflowResult(imported.Import, breaks, cases);
    }

    public async Task<StatementRunWorkflowResult?> GetAsync(string runId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return null;
        }

        var imports = await importStore.ListImportsAsync(cancellationToken).ConfigureAwait(false);
        var import = imports.FirstOrDefault(item => string.Equals(item.ImportId, runId, StringComparison.OrdinalIgnoreCase));
        if (import is null)
        {
            return null;
        }

        var breaks = await ListOpenBreaksAsync(cancellationToken).ConfigureAwait(false);
        var cases = await ListCasesAsync(cancellationToken).ConfigureAwait(false);
        return new StatementRunWorkflowResult(
            import,
            breaks.Where(item =>
                    string.Equals(item.RunId, import.ImportId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(item.ImportId, import.ImportId, StringComparison.OrdinalIgnoreCase))
                .ToArray(),
            cases.Where(item => string.Equals(item.ImportId, import.ImportId, StringComparison.OrdinalIgnoreCase)).ToArray());
    }

    public Task<IReadOnlyList<ReconciliationBreakRecord>> ListOpenBreaksAsync(CancellationToken cancellationToken = default)
        => breakStore.ListOpenAsync(cancellationToken);

    public Task<IReadOnlyList<ReconciliationCase>> ListCasesAsync(CancellationToken cancellationToken = default)
        => caseStore.ListAsync(cancellationToken);

    private async Task<StatementRunRequest> NormalizeAndValidateAsync(StatementRunRequest request, CancellationToken cancellationToken)
    {
        var sourceFileHash = string.IsNullOrWhiteSpace(request.SourceFileHash)
            ? await ComputeSourceFileHashAsync(request.SourcePath, cancellationToken).ConfigureAwait(false)
            : request.SourceFileHash.Trim().ToUpperInvariant();
        await validationService.ValidateAsync(
            new StatementReconciliationValidationRequest(request.Broker, request.SourcePath, request.MappingProfileId),
            cancellationToken).ConfigureAwait(false);
        return request with
        {
            Broker = request.Broker.Trim(),
            SourceInstitution = request.SourceInstitution.Trim(),
            FundAccountId = request.FundAccountId.Trim(),
            ExternalAccountId = request.ExternalAccountId.Trim(),
            SourcePath = request.SourcePath.Trim(),
            OriginalFileName = request.OriginalFileName.Trim(),
            MappingProfileId = request.MappingProfileId.Trim(),
            ToleranceProfileId = request.ToleranceProfileId.Trim(),
            ImportedBy = request.ImportedBy.Trim(),
            SourceFileHash = sourceFileHash
        };
    }

    private async Task<StatementToleranceProfile> ResolveToleranceProfileAsync(string profileId, CancellationToken cancellationToken)
    {
        if (_toleranceProfileProvider is null || string.IsNullOrWhiteSpace(profileId))
        {
            return StatementToleranceProfile.Default;
        }

        try
        {
            return await _toleranceProfileProvider.GetProfileAsync(profileId, cancellationToken).ConfigureAwait(false);
        }
        catch (KeyNotFoundException)
        {
            // Unknown profile ids fall back to the conservative default rather than failing the run.
            return StatementToleranceProfile.Default;
        }
    }

    private static BrokerStatementImportRequest ToImportRequest(StatementRunRequest request)
        => new(
            request.Broker,
            request.SourceInstitution,
            request.FundAccountId,
            request.ExternalAccountId,
            request.StatementPeriodStart,
            request.StatementPeriodEnd,
            request.SourcePath,
            request.OriginalFileName,
            request.MappingProfileId,
            request.ToleranceProfileId,
            request.ImportedBy,
            request.SourceFileHash);

    private static string BuildEvidenceLink(StatementRunBreak item)
    {
        var record = item.Record;
        // An internal-only break (no broker statement row) links to the retained internal record, not a
        // nonexistent statement row, so an operator investigating it gets true provenance.
        var anchor = item.StatementRow is null ? "internal" : "row";
        return $"/api/workstation/reconciliation/statement-runs/{Uri.EscapeDataString(record.ImportId)}#{anchor}-{Uri.EscapeDataString(record.SourceReference)}";
    }

    private static IReadOnlyList<ReconciliationCase> BuildStatementCases(
        CanonicalStatementImport import,
        IReadOnlyList<StatementRunBreak> breaks,
        string actor)
    {
        return breaks.Select(item =>
        {
            var breakRecord = item.Record;
            // Anchor every case, history entry, comment, and audit event to the break's own
            // creation timestamp so the run's records share one consistent instant.
            var now = breakRecord.CreatedAtUtc;
            var engineResult = item.EngineResult;
            var row = item.StatementRow;
            var isInternalOnly = row is null;
            var sourceRowHash = row?.RawChecksum ?? breakRecord.SourceReference;
            var evidenceLink = breakRecord.EvidenceLink ?? BuildEvidenceLink(item);
            // An internal-only break has no broker statement row; its source reference is the retained
            // internal evidence id. Record it as internal evidence so an operator gets true provenance
            // and a link to the internal record instead of a fabricated external statement row.
            var evidenceReferences = isInternalOnly
                ? new[] { evidenceLink, $"internal-record:{breakRecord.SourceReference}", $"internal-hash:{sourceRowHash}" }
                : new[] { evidenceLink, $"statement-row:{breakRecord.SourceReference}", $"statement-hash:{sourceRowHash}" };
            var explanation = BuildBreakExplanation(import, row, breakRecord, engineResult, evidenceReferences);
            var attachment = isInternalOnly
                ? new ReconciliationCaseAttachment(
                    AttachmentId: $"internal-record:{breakRecord.ImportId}:{breakRecord.SourceReference}",
                    EvidenceKind: "InternalReconciliationRecord",
                    SourceSystem: "meridian-internal-book",
                    SourceReference: breakRecord.SourceReference,
                    ContentHash: sourceRowHash,
                    Route: evidenceLink,
                    AttachedAtUtc: now)
                : new ReconciliationCaseAttachment(
                    AttachmentId: $"statement-row:{breakRecord.ImportId}:{breakRecord.SourceReference}",
                    EvidenceKind: "ExternalStatementRow",
                    SourceSystem: import.Broker,
                    SourceReference: breakRecord.SourceReference,
                    ContentHash: sourceRowHash,
                    Route: evidenceLink,
                    AttachedAtUtc: now);

            return new ReconciliationCase(
                CaseId: $"case:{breakRecord.BreakId}",
                ImportId: import.ImportId,
                Status: "Open",
                Reason: explanation.Summary,
                Confidence: engineResult.Confidence,
                Rationale: string.IsNullOrWhiteSpace(engineResult.Explanation) ? breakRecord.Category : engineResult.Explanation,
                CreatedAtUtc: now,
                History:
                [
                    new ReconciliationCaseHistoryEntry(now, "None", "Open", isInternalOnly ? "Case created from internal-only reconciliation break." : "Case created from external statement break.")
                    {
                        Actor = string.IsNullOrWhiteSpace(actor) ? "system" : actor.Trim(),
                        EvidenceId = evidenceLink
                    }
                ])
            {
                Owner = "fund-ops",
                Priority = breakRecord.ToleranceBreached ? "High" : "Normal",
                DueAtUtc = now.AddDays(breakRecord.ToleranceBreached ? 1 : 2),
                LastUpdatedAtUtc = now,
                LastUpdatedBy = string.IsNullOrWhiteSpace(actor) ? "system" : actor.Trim(),
                Disposition = "NeedsInvestigation",
                AgingDays = 0,
                EvidenceReferences = evidenceReferences,
                Attachments = [attachment],
                BreakExplanation = explanation,
                CommentThreads =
                [
                    new ReconciliationCaseCommentThread(
                        isInternalOnly ? "internal-intake" : "statement-intake",
                        isInternalOnly ? "Internal reconciliation intake" : "External statement intake",
                        [
                            new ReconciliationCaseComment(
                                Guid.NewGuid().ToString("N"),
                                $"{explanation.Summary} Suggested next action: {explanation.SuggestedNextAction}",
                                "system",
                                now)
                        ])
                ],
                AuditEvents =
                [
                    new ReconciliationCaseAuditEvent(
                        Guid.NewGuid().ToString("N"),
                        isInternalOnly ? "InternalReconciliationCaseCreated" : "ExternalStatementCaseCreated",
                        now,
                        "system",
                        $"Case created from {(isInternalOnly ? "internal-only reconciliation" : "statement")} break {breakRecord.BreakId}.")
                ]
            };
        }).ToArray();
    }

    private static ReconciliationBreakExplanation BuildBreakExplanation(
        CanonicalStatementImport import,
        CanonicalStatementRow? row,
        ReconciliationBreakRecord breakRecord,
        StatementMatchResult engineResult,
        IReadOnlyList<string> evidenceReferences)
    {
        var sourceSystem = string.IsNullOrWhiteSpace(import.SourceInstitution) ? import.Broker : import.SourceInstitution;
        var rowLabel = row is null ? breakRecord.SourceReference : $"row {row.SourceRowNumber}";
        var activityType = row?.ActivityType ?? breakRecord.Category;
        var side = engineResult.BrokerEvidenceReference is null ? "internal-record" : "statement";
        var amount = row is null ? breakRecord.Delta : Math.Abs(row.CashAmount) + Math.Abs(row.Quantity * row.Price);
        var descriptor = engineResult.MatchTier == StatementMatchTier.Candidate ? "candidate review" : "break";

        return new ReconciliationBreakExplanation(
            Summary: $"{activityType} {descriptor} from {sourceSystem} {rowLabel}.",
            SourceSystems: [sourceSystem, "Meridian ledger", "Meridian positions"],
            ProbableCause: string.IsNullOrWhiteSpace(engineResult.Explanation)
                ? "External statement row did not match retained Meridian ledger, position, or cash evidence."
                : engineResult.Explanation,
            LedgerImpact: $"Ledger, cash, or position balances may require review for {import.FundAccountId}; unmatched {side} exposure is {amount:G29}.",
            SuggestedNextAction: "Assign the case, compare the external statement row to retained ledger and position evidence, then attach support before disposition.",
            RequiredSignoffRole: breakRecord.ToleranceBreached ? "Fund accounting" : "Fund operations",
            EvidenceLinks: evidenceReferences);
    }

    private async Task<StatementToleranceProfile> ResolveToleranceProfileAsync(string? profileId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return StatementToleranceProfile.Default;
        }

        try
        {
            return await _toleranceProfileProvider.GetProfileAsync(profileId.Trim(), cancellationToken).ConfigureAwait(false);
        }
        catch (KeyNotFoundException ex)
        {
            // Fail closed: a run must not silently reconcile with default thresholds while recording a
            // different selected profile id, or the persisted profile would not be the profile actually
            // applied. Surface the misconfiguration so the operator registers the profile or submits a
            // known id instead.
            throw new InvalidOperationException(
                $"Statement tolerance profile '{profileId.Trim()}' is not registered; register it (reconciliation/tolerance-profiles.json) or submit the run with a known profile id.",
                ex);
        }
    }

    private static async Task<string> ComputeSourceFileHashAsync(string sourcePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(sourcePath);
        var hash = await System.Security.Cryptography.SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }
}
