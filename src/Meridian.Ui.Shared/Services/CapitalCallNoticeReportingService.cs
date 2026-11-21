using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Documents;
using Meridian.Ledger;
using Meridian.Reporting;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>Deployment-owned investor-to-recipient mapping; investor IDs are never assumed to be user IDs.</summary>
public sealed record CapitalCallNoticeRecipientBinding(
    string TenantId,
    string CompanyId,
    string FundProfileId,
    string InvestorId,
    string RecipientPrincipalId,
    string? ReviewGroupPrincipalId = null);

/// <summary>Resolves notice inputs from retained authority, including the independently posted issuance.</summary>
public sealed class CapitalCallNoticeSourceValidator(
    IPrivateCapitalStore capitalStore,
    IEnumerable<CapitalCallNoticeRecipientBinding>? recipientBindings = null)
{
    private readonly CapitalCallNoticeRecipientBinding[] _recipients = (recipientBindings ?? []).ToArray();

    public async Task<(PrivateCapitalCallDto Call, PrivateCapitalCallLineDto Line,
        PrivateCapitalCommitmentDto Commitment, string Recipient, string ReviewGroup)> ResolveAsync(
        PrivateCapitalScopeDto scope, string callId, string investorId, string? commitmentId = null,
        CancellationToken ct = default)
    {
        if (commitmentId is not null)
            commitmentId = PrivateCapitalAuthorityRules.Id(commitmentId, nameof(commitmentId));
        var call = await capitalStore.GetCallAsync(scope, callId, ct).ConfigureAwait(false)
            ?? throw Blocked("The retained capital call was not found in the requested scope.");
        if (call.Scope != scope)
            throw Blocked("The retained capital call scope does not match the notice scope.");
        var lines = call.Lines.Where(line => line.InvestorId == investorId
            && (commitmentId is null || line.CommitmentId == commitmentId)).ToArray();
        if (lines.Length != 1)
            throw Blocked("A notice requires one exact retained commitment installment for its investor; select a commitment when the call has multiple installments.");
        var line = lines[0];
        if (line.IssuanceJournalEntryId is null || line.IssuanceJournalEntryId == Guid.Empty
            || line.Amount <= 0 || line.IssuedAmount != line.Amount)
            throw Blocked("The investor installment has no complete posted issuance; an unposted plan cannot produce a notice.");
        var commitment = await capitalStore.GetCommitmentAsync(scope, line.CommitmentId,
            line.CommitmentRevision, ct).ConfigureAwait(false)
            ?? throw Blocked("The exact retained commitment revision was not found.");
        if (commitment.Scope != scope || commitment.InvestorId != investorId
            || commitment.CapitalAccountId != line.CapitalAccountId
            || commitment.Revision != line.CommitmentRevision
            || commitment.ApprovalStatus != PrivateCapitalCommitmentApprovalStatus.Approved)
            throw Blocked("The notice commitment revision or investor authority does not match its call installment.");
        var bindings = _recipients.Where(binding => binding.TenantId == scope.TenantId
            && binding.CompanyId == scope.CompanyId && binding.FundProfileId == scope.FundProfileId
            && binding.InvestorId == investorId).ToArray();
        if (bindings.Length != 1 || string.IsNullOrWhiteSpace(bindings[0].RecipientPrincipalId)
            || string.IsNullOrWhiteSpace(bindings[0].ReviewGroupPrincipalId))
            throw Blocked("Exactly one explicit scoped notice recipient binding and internal review group must be configured for this investor.");
        return (call, line, commitment, bindings[0].RecipientPrincipalId.Trim(), bindings[0].ReviewGroupPrincipalId!.Trim());
    }

    public async Task ValidateAsync(ReportingRunParametersDto parameters, ReportAccessQueryContext access,
        CancellationToken ct = default)
    {
        var scope = Scope(parameters, access);
        var resolved = await ResolveAsync(scope, Required(parameters, "capitalCallId"),
            parameters.Scope.InvestorId!, Required(parameters, "commitmentId"), ct).ConfigureAwait(false);
        var expected = CapitalCallNoticeReportingService.BuildTemplateParameters(
            resolved.Call, resolved.Line, resolved.Commitment, resolved.Recipient, resolved.ReviewGroup);
        if (parameters.Scope.EntityScopeKind != ReportingEntityScopeKindDto.Investor
            || parameters.ConsolidationLevel != ReportingConsolidationLevelDto.Investor
            || parameters.OutputFormat != ReportingOutputFormatDto.ClientPackage
            || parameters.Finality != ReportingFinalityDto.Draft
            || !parameters.IncludeEvidenceAppendix
            || parameters.Scope.Dimensions is not { } dimensions
            || dimensions.EntityId != scope.EntityId
            || dimensions.InvestorId != resolved.Line.InvestorId
            || dimensions.CapitalAccountId != resolved.Line.CapitalAccountId
            || !HasCommitmentFilter(dimensions.ExternalGlDimensions, resolved.Line.CommitmentId,
                allowLegacy: resolved.Call.Lines.Count(line => line.InvestorId == resolved.Line.InvestorId) == 1)
            || !dimensions.ExternalGlDimensions.TryGetValue("capitalCallId", out var callId) || callId != resolved.Call.CallId
            || !dimensions.ExternalGlDimensions.TryGetValue("capitalActivityKind", out var kind) || kind != "CapitalCallIssuance"
            || parameters.AsOfDate != resolved.Call.NoticeDate || parameters.PeriodId != resolved.Call.PeriodId
            || expected.Any(pair => !parameters.TemplateParameters.TryGetValue(pair.Key, out var value) || value != pair.Value))
            throw Blocked("The notice parameters are not bound to the exact retained investor installment and posted issuance.");
    }

    public static void ValidateRows(ReportingRunParametersDto parameters,
        ImmutableArray<IReadOnlyDictionary<string, string>> rows)
    {
        if (rows.IsDefaultOrEmpty || rows.Any(row =>
                !Matches(row, "investorId", parameters.Scope.InvestorId!)
                || !Matches(row, "capitalAccountId", Required(parameters, "capitalAccountId"))
                || !Matches(row, "journalEntryId", Required(parameters, "issuanceJournalEntryId"))
                || !Matches(row, "externalGl.capitalCallId", Required(parameters, "capitalCallId"))
                || !Matches(row, "externalGl.capitalActivityKind", "CapitalCallIssuance")
                || !Matches(row, "externalGl.commitmentId", Required(parameters, "commitmentId"))
                || !Matches(row, "externalGl.commitmentRevision", Required(parameters, "commitmentRevision"))))
            throw Blocked("The certified notice dataset contains missing, unposted, or foreign investor issuance rows.");
        var amount = decimal.Parse(Required(parameters, "callAmount"), NumberStyles.Float, CultureInfo.InvariantCulture);
        if (rows.Sum(row => decimal.Parse(row["debit"], NumberStyles.Float, CultureInfo.InvariantCulture)) != amount
            || rows.Sum(row => decimal.Parse(row["credit"], NumberStyles.Float, CultureInfo.InvariantCulture)) != amount)
            throw Blocked("The certified issuance does not reproduce the retained investor call amount.");
    }

    internal static string Required(ReportingRunParametersDto parameters, string key) =>
        parameters.TemplateParameters.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value : throw Blocked($"The notice is missing retained binding '{key}'.");

    internal static bool HasCommitmentFilter(IReadOnlyDictionary<string, string> dimensions,
        string commitmentId, bool allowLegacy) =>
        dimensions.Count == 3 && dimensions.TryGetValue("commitmentId", out var selected) && selected == commitmentId
        || allowLegacy && dimensions.Count == 2;

    private static PrivateCapitalScopeDto Scope(ReportingRunParametersDto parameters, ReportAccessQueryContext access)
    {
        if (!access.RequireBoundScope || string.IsNullOrWhiteSpace(access.TenantId)
            || string.IsNullOrWhiteSpace(access.CompanyId) || string.IsNullOrWhiteSpace(parameters.Scope.InvestorId)
            || parameters.LedgerBook.LedgerBookId is not { } book || book == Guid.Empty)
            throw Blocked("A notice requires authenticated tenant/company and exact investor/book scope.");
        return new(access.TenantId, access.CompanyId, Required(parameters, "entityId"),
            parameters.Scope.FundProfileId, book, parameters.PresentationCurrency);
    }

    private static bool Matches(IReadOnlyDictionary<string, string> row, string key, string expected) =>
        row.TryGetValue(key, out var value) && value == expected;

    private static ReportingAuthoritativeSourceUnavailableException Blocked(string message) => new(message);
}

/// <summary>Creates a governed Draft notice; approval, release, grants, and delivery use existing Reporting controls.</summary>
public sealed class CapitalCallNoticeReportingService(
    CapitalCallNoticeSourceValidator source,
    ILedgerJournalStore journals,
    ReportingRunCommandService reporting,
    IReportingRunStore? runStore = null,
    IReportingGovernanceRepository? governance = null,
    IReportingDeliveryStore? deliveries = null,
    IReportingAuthoritativeSource? authoritativeSource = null,
    IReportingReconciliationEvidenceRetentionStore? reconciliationRetention = null)
{
    public const string TemplateId = "capital-call-notice";
    internal const string OperationalCompletionPrefix = "capital-call-issuance-reconciled-";

    /// <summary>Projects retained notice associations; callers cannot attach an arbitrary reporting run to a call.</summary>
    public async Task<IReadOnlyList<PrivateCapitalNoticeSummaryDto>> ReadNoticeSummariesAsync(
        PrivateCapitalScopeDto scope, ReportAccessQueryContext access, CancellationToken ct = default)
    {
        if (!access.RequireBoundScope || access.TenantId != scope.TenantId || access.CompanyId != scope.CompanyId)
            throw new UnauthorizedAccessException("Notice history requires the authenticated tenant and company scope.");
        if (runStore is null || governance is null)
            return [];
        var results = new List<PrivateCapitalNoticeSummaryDto>();
        for (var offset = 0; ; offset += 200)
        {
            ct.ThrowIfCancellationRequested();
            var page = runStore.ListRuns(scope.TenantId, scope.CompanyId, offset, 200);
            var candidates = page.Select(snapshot => snapshot.Manifest).Where(manifest =>
                manifest.TemplateId == TemplateId && manifest.OperationalScope?.FundId == scope.FundProfileId
                && manifest.OperationalScope?.BookId == scope.LedgerBookId.ToString("D")
                && manifest.ResolvedParameters?.PresentationCurrency == scope.Currency
                && manifest.ResolvedParameters.TemplateParameters.TryGetValue("entityId", out var entity) && entity == scope.EntityId
                && ReportPackRunReadService.HasCertifiedImmutableGovernanceBinding(manifest)
                && ReportAccessPolicyEvaluator.Evaluate(manifest, access).IsAccessible).ToArray();
            if (candidates.Length > 0)
            {
                var retainedRuns = await governance.ExecuteTransactionAsync((transaction, token) =>
                    transaction.GetRunsAsync(scope.TenantId, candidates.Select(manifest => manifest.RunId).ToArray(), token), ct)
                    .ConfigureAwait(false);
                foreach (var manifest in candidates)
                {
                    var retained = retainedRuns.SingleOrDefault(run => run.RunId == manifest.RunId);
                    if (retained is null || retained.Scope != manifest.OperationalScope
                        || retained.Snapshot.SnapshotHash != manifest.CertifiedSnapshot!.SnapshotHash
                        || retained.Access.PolicyHash != manifest.ImmutableAccessScope!.PolicyHash)
                        continue;
                    // This also rejects a retained association with foreign LP rows or a broad audience.
                    _ = CapitalCallNoticeDocument.Build(manifest);
                    var parameters = manifest.ResolvedParameters!;
                    var packageId = ReportingArtifactPackageIdentity.Create(retained);
                    var recipient = CapitalCallNoticeSourceValidator.Required(parameters, "recipientPrincipalId");
                    var jobs = deliveries is null ? null
                        : await deliveries.ListByPackageAsync(scope.TenantId, packageId, ct).ConfigureAwait(false);
                    var latestDelivery = jobs?.Where(job => job.TenantId == scope.TenantId && job.PackageId == packageId
                        && job.ReleaseAuthorization.RunId == retained.RunId
                        && job.Payload.RecipientKind == ReportingAccessPrincipalKind.User && job.Payload.Recipient == recipient)
                        .OrderByDescending(job => job.UpdatedAtUtc).FirstOrDefault();
                    results.Add(new PrivateCapitalNoticeSummaryDto(retained.RunId,
                        CapitalCallNoticeSourceValidator.Required(parameters, "capitalCallId"), parameters.Scope.InvestorId!,
                        retained.GovernanceState.ToString(), packageId,
                        latestDelivery?.State.ToString() ?? (deliveries is null ? "Unavailable" : "NotQueued"),
                        $"/reporting/runs/detail?runId={Uri.EscapeDataString(retained.RunId)}",
                        CapitalCallNoticeSourceValidator.Required(parameters, "commitmentId")));
                }
            }
            if (page.Count < 200)
                break;
        }
        return results;
    }

    public async Task<ReportingRunResultDto> GenerateAsync(PrivateCapitalScopeDto scope,
        string callId, string investorId, Guid operationId, string actor, ReportAccessQueryContext access,
        ReportingGovernanceCallerContext caller, string? commitmentId = null, CancellationToken ct = default)
    {
        scope = PrivateCapitalAuthorityRules.NormalizeScope(scope);
        callId = PrivateCapitalAuthorityRules.Id(callId, nameof(callId));
        investorId = PrivateCapitalAuthorityRules.Id(investorId, nameof(investorId));
        if (commitmentId is not null)
            commitmentId = PrivateCapitalAuthorityRules.Id(commitmentId, nameof(commitmentId));
        if (!access.RequireBoundScope || access.TenantId != scope.TenantId || access.CompanyId != scope.CompanyId)
            throw new UnauthorizedAccessException("The notice scope must match the authenticated tenant and company.");
        var operation = BuildOperation(scope, callId, investorId, operationId, actor, commitmentId);
        if (await reporting.TryRecoverOperationAsync(TemplateId, operation, actor, access, caller, ct)
                .ConfigureAwait(false) is { } recovered)
            return recovered;
        var resolved = await source.ResolveAsync(scope, callId, investorId, commitmentId, ct).ConfigureAwait(false);
        var book = await journals.GetLedgerBookAsync(scope.LedgerBookId, ct).ConfigureAwait(false)
            ?? throw new ReportingAuthoritativeSourceUnavailableException("The authoritative notice book is unavailable.");
        var basis = book.AccountingBasis switch
        {
            AccountingBasisKindDto.Gaap => ReportingAccountingBasisDto.Gaap,
            AccountingBasisKindDto.Tax => ReportingAccountingBasisDto.Tax,
            AccountingBasisKindDto.Cash => ReportingAccountingBasisDto.Cash,
            AccountingBasisKindDto.Statutory => ReportingAccountingBasisDto.Statutory,
            _ => ReportingAccountingBasisDto.Management
        };
        var dimensions = new LedgerDimensionSetDto(EntityId: scope.EntityId, InvestorId: investorId,
            CapitalAccountId: resolved.Line.CapitalAccountId, ExternalGlDimensions: new Dictionary<string, string>
            {
                ["capitalCallId"] = callId,
                ["capitalActivityKind"] = "CapitalCallIssuance",
                ["commitmentId"] = resolved.Line.CommitmentId
            });
        var parameters = new ReportingRunParametersDto(
            new ReportingRunScopeDto(scope.FundProfileId, ReportingEntityScopeKindDto.Investor,
                InvestorId: investorId, Dimensions: dimensions), resolved.Call.PeriodId,
            resolved.Call.NoticeDate, new ReportingLedgerBookSelectionDto(scope.LedgerBookId), basis,
            scope.Currency, ReportingConsolidationLevelDto.Investor, ReportingOutputFormatDto.ClientPackage,
            ReportingFinalityDto.Draft, true, true,
            BuildTemplateParameters(resolved.Call, resolved.Line, resolved.Commitment, resolved.Recipient, resolved.ReviewGroup));
        await ReconcilePostedIssuanceAsync(parameters, access, ct).ConfigureAwait(false);
        return await reporting.RunOperationAsync(new ReportingRunRequestDto(TemplateId, Parameters: parameters),
            operation, actor, access, caller, ct).ConfigureAwait(false);
    }

    internal static ReportingRunOperation BuildOperation(PrivateCapitalScopeDto scope, string callId,
        string investorId, Guid operationId, string actor, string? commitmentId = null)
    {
        if (operationId == Guid.Empty)
            throw new ArgumentException("A nonempty notice operation ID is required.", nameof(operationId));
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        scope = PrivateCapitalAuthorityRules.NormalizeScope(scope);
        // Preserve the original request bytes for retained UUIDs whose body omitted the selector.
        var requestIdentity = commitmentId is null ? JsonSerializer.Serialize(new
        {
            TemplateId,
            Scope = scope,
            CallId = PrivateCapitalAuthorityRules.Id(callId, nameof(callId)),
            InvestorId = PrivateCapitalAuthorityRules.Id(investorId, nameof(investorId)),
            Actor = actor.Trim()
        }) : JsonSerializer.Serialize(new
        {
            TemplateId,
            Scope = scope,
            CallId = PrivateCapitalAuthorityRules.Id(callId, nameof(callId)),
            InvestorId = PrivateCapitalAuthorityRules.Id(investorId, nameof(investorId)),
            Actor = actor.Trim(),
            CommitmentId = PrivateCapitalAuthorityRules.Id(commitmentId, nameof(commitmentId))
        });
        return new ReportingRunOperation($"capital-call-notice-op-{operationId:N}", Sha256Digest.ComputeUtf8(requestIdentity));
    }

    /// <summary>
    /// Retains an actual reconciliation of the frozen call allocation to its posted journal.
    /// This is operational issuance evidence, carries no committed-close authority, and cannot
    /// satisfy the Final financial-report gate.
    /// </summary>
    internal async Task ReconcilePostedIssuanceAsync(ReportingRunParametersDto parameters,
        ReportAccessQueryContext access, CancellationToken ct = default)
    {
        if (authoritativeSource is null || reconciliationRetention is null)
            throw new ReportingAuthoritativeSourceUnavailableException(
                "Durable notice reconciliation and authoritative source are required.");
        await source.ValidateAsync(parameters, access, ct).ConfigureAwait(false);
        var capture = await authoritativeSource.CaptureAsync(parameters, access,
            new ReportingAuthoritativeSourceCaptureIntent(TemplateId), ct).ConfigureAwait(false);
        ReportingRunCertificationService.ValidateCapture(capture, parameters, access);
        CapitalCallNoticeSourceValidator.ValidateRows(parameters, capture.DatasetRows);
        var completedAt = capture.DatasetRows.Select(row => DateTimeOffset.Parse(row["recordedAtUtc"],
            CultureInfo.InvariantCulture)).Max().ToUniversalTime();
        var evidence = OperationalEvidence(parameters);
        var completionHash = OperationalCompletionHash(capture.Checkpoint.CheckpointHash, evidence);
        var completion = new ReportingReconciliationCompletionEvidence(
            $"{OperationalCompletionPrefix}{completionHash}", completionHash, completedAt, false, evidence, []);
        var receipt = ReportingReconciliationEvidenceValidation.CreateReceipt(capture.Checkpoint, completion);
        await reconciliationRetention.RetainAsync(receipt, ct).ConfigureAwait(false);
    }

    internal static bool MatchesOperationalReceipt(ReportingRunParametersDto parameters,
        ReportingReconciliationEvidenceReceipt receipt)
    {
        var dimensions = parameters.Scope.Dimensions;
        if (parameters.Finality != ReportingFinalityDto.Draft || parameters.OutputFormat != ReportingOutputFormatDto.ClientPackage
            || parameters.Scope.EntityScopeKind != ReportingEntityScopeKindDto.Investor
            || parameters.ConsolidationLevel != ReportingConsolidationLevelDto.Investor
            || string.IsNullOrWhiteSpace(parameters.Scope.InvestorId) || dimensions is null
            || dimensions.InvestorId != parameters.Scope.InvestorId || receipt.HasOpenBreaks
            || receipt.CloseWorkflowCompletion is not null || !receipt.BreakEvidence.IsDefaultOrEmpty
            || !parameters.TemplateParameters.TryGetValue("operationalNoticeTemplateId", out var template) || template != TemplateId
            || !parameters.TemplateParameters.TryGetValue("capitalCallId", out var callId)
            || !parameters.TemplateParameters.TryGetValue("capitalAccountId", out var capitalAccount)
            || !parameters.TemplateParameters.TryGetValue("commitmentId", out var commitmentId)
            || !parameters.TemplateParameters.TryGetValue("entityId", out var entityId)
            || dimensions.EntityId != entityId || dimensions.CapitalAccountId != capitalAccount
            || !CapitalCallNoticeSourceValidator.HasCommitmentFilter(dimensions.ExternalGlDimensions, commitmentId, allowLegacy: true)
            || dimensions.ExternalGlDimensions.GetValueOrDefault("capitalCallId") != callId
            || dimensions.ExternalGlDimensions.GetValueOrDefault("capitalActivityKind") != "CapitalCallIssuance")
            return false;
        try
        {
            var evidence = OperationalEvidence(parameters);
            var hash = OperationalCompletionHash(receipt.SourceCheckpointHash, evidence);
            return receipt.CompletionCheckpointHash == hash && receipt.CompletionCheckpointId == OperationalCompletionPrefix + hash
                && evidence.All(item => receipt.EvidenceIds.Contains(item, StringComparer.Ordinal));
        }
        catch (ReportingAuthoritativeSourceUnavailableException) { return false; }
    }

    private static ImmutableArray<string> OperationalEvidence(ReportingRunParametersDto parameters)
    {
        string P(string key) => CapitalCallNoticeSourceValidator.Required(parameters, key);
        return [$"capital-call-plan:{P("capitalCallId")}:{P("planHash")}",
            $"posted-capital-call:{P("issuanceJournalEntryId")}",
            $"capital-call-investor:{parameters.Scope.InvestorId}:{P("capitalAccountId")}",
            $"capital-call-commitment:{P("commitmentId")}:{P("commitmentRevision")}"];
    }

    private static string OperationalCompletionHash(string sourceHash, ImmutableArray<string> evidence) =>
        Sha256Digest.ComputeUtf8(sourceHash + "\n" + string.Join("\n", evidence));

    internal static IReadOnlyDictionary<string, string> BuildTemplateParameters(PrivateCapitalCallDto call,
        PrivateCapitalCallLineDto line, PrivateCapitalCommitmentDto commitment, string recipient, string reviewGroup) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["operationalNoticeTemplateId"] = TemplateId,
            ["capitalCallId"] = call.CallId,
            ["planHash"] = call.PlanHash,
            ["entityId"] = call.Scope.EntityId,
            ["installmentId"] = line.InstallmentId,
            ["commitmentId"] = line.CommitmentId,
            ["commitmentRevision"] = line.CommitmentRevision.ToString(CultureInfo.InvariantCulture),
            ["capitalAccountId"] = line.CapitalAccountId,
            ["issuanceJournalEntryId"] = line.IssuanceJournalEntryId!.Value.ToString("D"),
            ["recipientPrincipalId"] = recipient,
            ["reviewGroupPrincipalId"] = reviewGroup,
            ["noticeDate"] = call.NoticeDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["dueDate"] = call.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["callAmount"] = line.Amount.ToString("G29", CultureInfo.InvariantCulture),
            ["totalCommitment"] = commitment.TotalCommitment.ToString("G29", CultureInfo.InvariantCulture),
            ["uncalledBefore"] = line.UncalledBefore.ToString("G29", CultureInfo.InvariantCulture),
            ["uncalledAfter"] = line.UncalledAfter.ToString("G29", CultureInfo.InvariantCulture),
            ["purpose"] = call.Purpose?.Trim() ?? string.Empty
        };
}

internal static class CapitalCallNoticeDocument
{
    internal static ReportDocumentModel Build(ReportingOutputManifest manifest)
    {
        var parameters = manifest.ResolvedParameters
            ?? throw new ReportingGovernanceException("The notice has no certified parameters.");
        CapitalCallNoticeSourceValidator.ValidateRows(parameters, manifest.CertifiedDatasetRows);
        var recipient = CapitalCallNoticeSourceValidator.Required(parameters, "recipientPrincipalId");
        var reviewGroup = CapitalCallNoticeSourceValidator.Required(parameters, "reviewGroupPrincipalId");
        var access = manifest.ImmutableAccessScope;
        if (access is null || access.Mode != ReportingGovernanceAccessMode.Restricted
            || access.Principals.Length != 2
            || !access.Principals.Contains(new ReportingAccessPrincipalScope(ReportingAccessPrincipalKind.User, recipient))
            || !access.Principals.Contains(new ReportingAccessPrincipalScope(ReportingAccessPrincipalKind.Group, reviewGroup)))
            throw new ReportingGovernanceException("An investor notice requires its exact restricted recipient audience.");
        string P(string key) => CapitalCallNoticeSourceValidator.Required(parameters, key);
        return new ReportDocumentModel("Capital Call Notice",
            [new("Fund", parameters.Scope.FundProfileId), new("Investor", parameters.Scope.InvestorId!),
             new("Call", P("capitalCallId")), new("Notice date", P("noticeDate")), new("Due date", P("dueDate")),
             new("Currency", parameters.PresentationCurrency), new("Commitment", P("commitmentId")),
             new("Commitment revision", P("commitmentRevision")), new("Posted issuance journal", P("issuanceJournalEntryId"))],
            [new ReportDocumentTable("Capital call", ["Description", "Amount"],
                [new[] { "Amount due", P("callAmount") }, new[] { "Total commitment", P("totalCommitment") },
                 new[] { "Uncalled before this call", P("uncalledBefore") }, new[] { "Uncalled after this call", P("uncalledAfter") }])],
            Subtitle: "Governed notice — release and delivery remain separate from issuance",
            FooterNote: $"Retained call plan {P("planHash")} · run {manifest.RunId}");
    }
}
