using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Reporting;
using Meridian.Ui.Shared.Evidence;

namespace Meridian.Ui.Shared.Services;

/// <summary>Resolves generated amounts exclusively from the report's retained ledger population.</summary>
public sealed class ReportLedgerAmountProvenanceService(
    IReportingRunStore? runs,
    PostedLedgerAmountProvenanceService postedAmounts)
{
    public static bool IsReportSubject(string subjectId) => subjectId.StartsWith("report:", StringComparison.Ordinal);

    public IReadOnlyList<ReportLedgerAmountBindingDto> List(string runId, ReportAccessQueryContext access)
    {
        var manifest = AuthorizedManifest(runId, access)
            ?? throw new KeyNotFoundException("The retained report was not found in the selected scope.");
        if (manifest.AuthoritativeSource?.LedgerPopulation is null)
            return [];
        ReportingCertifiedManifestValidation.Validate(manifest);
        var population = ReportingLedgerPopulationSnapshot.Decode(manifest.AuthoritativeSource);
        if (!MatchesManifestPopulation(manifest, population) || !ReportAmountBindingBuilder.MatchesRetained(population))
            throw new ReportingGovernanceException("Retained generated amount bindings do not reproduce from the certified ledger population.");
        return population.ReportAmounts.Select(amount => amount with
        {
            SubjectId = $"report:{manifest.RunId}:{amount.AmountId}",
            SourceSnapshotHash = manifest.AuthoritativeSource.CheckpointHash
        }).ToArray();
    }

    public async Task<EvidencePacketDto?> GetPacketAsync(string subjectId, LedgerAmountScopeDto scope,
        ReportAccessQueryContext? access, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!TryParseSubject(subjectId, out var runId, out var amountId) || access is null)
            return null;
        var manifest = AuthorizedManifest(runId, access);
        if (manifest?.AuthoritativeSource is not { } source || source.TenantId != scope.TenantId
            || source.CompanyId != scope.CompanyId || source.FundId != scope.FundProfileId
            || source.LedgerBookId != scope.LedgerBookId.ToString("D")
            || source.AccountingPeriodId != scope.PeriodId.ToString("D"))
            return null;

        ReportingLedgerPopulationSnapshot population;
        try
        {
            ReportingCertifiedManifestValidation.Validate(manifest);
            population = ReportingLedgerPopulationSnapshot.Decode(source);
            if (!MatchesManifestPopulation(manifest, population) || !ReportAmountBindingBuilder.MatchesRetained(population))
                throw new ReportingGovernanceException("Retained amount bindings no longer reproduce from the report snapshot.");
        }
        catch (Exception exception) when (exception is ReportingGovernanceException or InvalidDataException
            or ArgumentException or LedgerValidationException or InvalidOperationException)
        {
            return Packet(subjectId, scope, "Generated report amount", 0m,
                manifest.ResolvedParameters?.PresentationCurrency ?? "", [], [],
                ["Generated amount proof is blocked: the retained ledger population or amount binding is missing, altered, or invalid."]);
        }
        var amounts = population.ReportAmounts.Where(amount => amount.AmountId == amountId).ToArray();
        if (amounts.Length != 1 || amounts[0].Scope != scope)
            return null;
        var amount = amounts[0];
        var evidence = new Dictionary<string, LedgerAmountProofEvidenceDto>(StringComparer.Ordinal);
        var nodes = new Dictionary<string, EvidenceNodeDto>(StringComparer.Ordinal);
        var edges = new List<EvidenceEdgeDto>();
        var warnings = new List<string>();
        foreach (var entryId in amount.LedgerEntryIds)
        {
            var records = population.Journals.Where(record => record.Entry.Lines.Any(line => line.EntryId == entryId)).ToArray();
            if (records.Length != 1 || !amount.JournalEntryIds.Contains(records[0].Entry.JournalEntryId))
            {
                warnings.Add("A contributing journal or line is missing or ambiguous in the retained report population.");
                continue;
            }
            var record = records[0];
            var line = record.Entry.Lines.Single(item => item.EntryId == entryId);
            var postedSubject = $"{record.Entry.JournalEntryId:D}:{line.EntryId:D}:{(line.Debit > 0m ? "debit" : "credit")}";
            // Historical balances may legitimately include earlier periods in the same book.
            // Their source association must retain the journal's own period, never the report period.
            var lineScope = scope with { PeriodId = record.PeriodId };
            var proof = await postedAmounts.GetRetainedPacketAsync(postedSubject, lineScope, record, amount.Currency, ct)
                .ConfigureAwait(false);
            var verified = proof?.LedgerAmount;
            if (verified is null || verified.Status != EvidenceStatusDto.Ready
                || !string.Equals(verified.Currency, amount.Currency, StringComparison.OrdinalIgnoreCase)
                || line.Currency is { } currency
                    && !string.Equals(currency.FunctionalCurrency, amount.Currency, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add("Verified retained source evidence is unavailable for a contributing journal line. Generated amount proof is blocked.");
                warnings.AddRange(proof?.Warnings ?? []);
                continue;
            }
            foreach (var item in verified.Evidence)
            {
                var retained = item with { SourceScope = lineScope, SourceSubjectId = postedSubject,
                    Route = RebindSourceRoute(item.Route, subjectId, scope, item.ContentHash) };
                if (!evidence.TryAdd(item.EvidenceId, retained))
                    warnings.Add("Contributing lines retain ambiguous supporting-evidence identities. Generated amount proof is blocked.");
            }
            foreach (var node in proof!.Nodes)
                nodes.TryAdd(node.EvidenceId, node with
                {
                    ArtifactRefs = node.ArtifactRefs.Select(artifact => artifact with
                    {
                        Route = RebindSourceRoute(artifact.Route, subjectId, scope, artifact.Hash)
                    }).ToArray()
                });
            edges.AddRange(proof.Edges);
        }
        if (amount.LedgerEntryIds.Count == 0)
            warnings.Add("The generated amount has no retained contributing ledger lines.");
        // Any incomplete contributor blocks the complete report amount. Do not expose routes from
        // an otherwise valid subset as if that subset proved the retained total.
        if (warnings.Count != 0)
            return Packet(subjectId, scope, amount.Label, amount.Amount, amount.Currency, [], [], warnings.Distinct().ToArray());
        return Packet(subjectId, scope, amount.Label, amount.Amount, amount.Currency,
            evidence.Values.ToArray(), nodes.Values.ToArray(), [], edges);
    }

    private ReportingOutputManifest? AuthorizedManifest(string runId, ReportAccessQueryContext access)
    {
        if (runs is null || !access.RequireBoundScope || string.IsNullOrWhiteSpace(access.ActorPrincipalId)
            || string.IsNullOrWhiteSpace(access.TenantId) || string.IsNullOrWhiteSpace(access.CompanyId))
            return null;
        var manifest = runs.GetManifest(access.TenantId, runId);
        return manifest is not null && manifest.Status != ReportingRunStatus.Failed
            && ReportAccessPolicyEvaluator.Evaluate(manifest, access).IsAccessible ? manifest : null;
    }

    private static bool MatchesManifestPopulation(ReportingOutputManifest manifest, ReportingLedgerPopulationSnapshot population)
        => Sha256Digest.FixedEquals(ReportingCertifiedManifestValidation.ComputeCertifiedRowsHash(manifest.CertifiedDatasetRows),
            ReportingCertifiedManifestValidation.ComputeCertifiedRowsHash(population.DatasetRows));

    private static string? RebindSourceRoute(string? route, string subjectId, LedgerAmountScopeDto scope, string? hash)
    {
        if (route is null)
            return null;
        var query = route.IndexOf('?');
        return (query < 0 ? route : route[..query])
            + $"?ledgerAmountSubjectId={Uri.EscapeDataString(subjectId)}&ledgerBookId={scope.LedgerBookId:D}"
            + $"&periodId={scope.PeriodId:D}&fundProfileId={Uri.EscapeDataString(scope.FundProfileId)}&expectedContentHash={hash}";
    }

    private static bool TryParseSubject(string subjectId, out string runId, out string amountId)
    {
        runId = amountId = "";
        if (!IsReportSubject(subjectId))
            return false;
        var split = subjectId.LastIndexOf(':');
        if (split <= "report:".Length || !Sha256Digest.IsCanonical(subjectId[(split + 1)..]))
            return false;
        runId = subjectId["report:".Length..split];
        amountId = subjectId[(split + 1)..];
        return true;
    }

    private static EvidencePacketDto Packet(string subjectId, LedgerAmountScopeDto scope, string label,
        decimal amount, string currency, IReadOnlyList<LedgerAmountProofEvidenceDto> evidence,
        IReadOnlyList<EvidenceNodeDto> nodes, IReadOnlyList<string> warnings,
        IReadOnlyList<EvidenceEdgeDto>? sourceEdges = null)
    {
        var status = warnings.Count == 0 ? EvidenceStatusDto.Ready : EvidenceStatusDto.Blocked;
        var subject = new EvidenceSubjectDto(subjectId, EvidenceSubjectResolver.LedgerAmountKind,
            label, "Reporting", null, "ReportRunGovernance", scope.LedgerBookId);
        var ids = evidence.Select(item => item.EvidenceId).ToArray();
        string[] missing = status == EvidenceStatusDto.Ready ? [] : ["retained-report-amount-support"];
        var completeness = new EvidenceCompletenessDto(status == EvidenceStatusDto.Ready ? 100 : 0, status,
            ids.Concat(missing).ToArray(), ids, missing, [], [])
        {
            BlockingIssueCount = warnings.Count,
            ValidationIssues = warnings.Select(warning => new EvidenceValidationIssueDto("report-amount-proof",
                EvidenceValidationSeverityDto.Critical, warning)).ToArray()
        };
        var ledgerIds = evidence.Where(item => item.Kind == "ledger-record").Select(item => item.EvidenceId).ToArray();
        var sourceIds = evidence.Where(item => item.Kind != "ledger-record").Select(item => item.EvidenceId).ToArray();
        return new EvidencePacketDto(subject, DateTimeOffset.UtcNow, nodes, sourceEdges ?? [], completeness, [], warnings)
        {
            LedgerAmount = new LedgerAmountProofDto(subjectId, scope, amount, currency, status, evidence, warnings),
            ProofChain = new EvidenceProofChainDto(completeness.Score, status, status == EvidenceStatusDto.Ready ? 2 : 0, 2,
                [new(EvidenceProofChainLayerKindDto.Source, "Verified retained sources", status,
                    completeness.Score, sourceIds, sourceIds, sourceIds, [], missing, ["source-document"],
                    "Every contributing line requires exact retained accounting scope and verified source bytes."),
                 new(EvidenceProofChainLayerKindDto.Ledger, "Report snapshot journal lines", status,
                    completeness.Score, ledgerIds, ledgerIds, ledgerIds, [], missing, ["ledger-record"],
                    "The amount reproduces from the retained report population, including prior-period balances.")],
                status == EvidenceStatusDto.Ready ? "Verified retained evidence supports this generated amount." : "Generated amount proof is blocked.")
        };
    }
}
