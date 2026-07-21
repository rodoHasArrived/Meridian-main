using FluentAssertions;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Meridian.Infrastructure.Reconciliation;
using Meridian.Ui.Shared.Contracts.Reconciliation;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Ui;

public sealed class ReconciliationApiServiceTests
{
    [Fact]
    public async Task CreateStatementRunAsync_CustodianStatement_ShouldPersistBreaksAndCases()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-reconciliation-api-{Guid.NewGuid():N}");
        var statementPath = Path.Combine(root, "custodian-statement.csv");
        var fundAccountId = Guid.NewGuid();
        Directory.CreateDirectory(root);
        await File.WriteAllLinesAsync(statementPath,
        [
            "account,symbol,quantity,price,cashAmount,activityType,tradeDate",
            "FUND-1,SPY,10,500,0,position,2026-05-28",
            "FUND-1,,0,0,2500.25,cash,2026-05-28",
            "FUND-1,MSFT,1,15.75,0,fee,2026-05-28"
        ]);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<StatementReconciliationService>();
        services.AddSingleton<StatementReconciliationContextAdapter>();
        services.AddSingleton<IStatementReconciliationValidationService>(sp => sp.GetRequiredService<StatementReconciliationContextAdapter>());
        services.AddSingleton<IDataIntegrationIngestionService>(sp => sp.GetRequiredService<StatementReconciliationContextAdapter>());
        services.AddSingleton<IReconciliationCaseIntakeService>(sp => sp.GetRequiredService<StatementReconciliationContextAdapter>());
        services.AddSingleton<ICanonicalStatementStore>(_ => new JsonCanonicalStatementStore(root));
        services.AddSingleton<IReconciliationCaseStore>(_ => new JsonReconciliationCaseStore(root));
        services.AddSingleton<IReconciliationBreakStore>(_ => new JsonReconciliationBreakStore(root));
        services.AddSingleton<IBrokerStatementService>(sp => new CsvBrokerStatementService(sp.GetRequiredService<ICanonicalStatementStore>()));
        services.AddSingleton<IStatementRunWorkflowService, StatementRunWorkflowService>();
        services.AddSingleton<IReconciliationApiService, ReconciliationApiService>();

        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<IReconciliationApiService>();

        var created = await service.CreateStatementRunAsync(
            new StatementRunCreateDto(
                Broker: "custodian",
                SourceInstitution: "Sample Custodian",
                FundAccountId: fundAccountId.ToString("D"),
                ExternalAccountId: "external-account-1",
                StatementPeriodStart: new DateOnly(2026, 5, 1),
                StatementPeriodEnd: new DateOnly(2026, 5, 31),
                SourcePath: statementPath,
                OriginalFileName: "custodian-statement.csv",
                MappingProfileId: "canonical-csv-v1",
                ToleranceProfileId: "statement-default",
                ImportedBy: "ops-user"),
            CancellationToken.None);

        created.Should().NotBeNull();
        created!.Status.Should().Be(StatementRunStatus.ReviewRequired);
        // Sided reconciliation against an unprovisioned (empty) internal book: the position, cash,
        // and fee rows each lack an internal counterpart, so all three are honest unmatched breaks
        // (the retired shim fabricated a position self-match, leaving only two breaks).
        created.MatchSummary!.StatementItemCount.Should().Be(3);
        created.MatchSummary.BreakCount.Should().Be(3);
        created.Breaks.Should().HaveCount(3);
        created.Cases.Should().HaveCount(3);
        created.Cases.Should().OnlyContain(item =>
            item.Owner == "fund-ops" &&
            item.Priority == "High" &&
            item.Disposition == "NeedsInvestigation" &&
            item.AgingDays == 0 &&
            item.DueAtUtc.HasValue &&
            item.EvidenceLink!.Contains("/api/workstation/reconciliation/statement-runs/", StringComparison.OrdinalIgnoreCase));
        foreach (var item in created.Cases)
        {
            item.CommentThreads.Should().Contain(thread =>
                thread.Subject == "External statement intake" &&
                thread.Comments!.Any(comment =>
                    comment.Actor == "system" &&
                    comment.Body!.Contains("Suggested next action:", StringComparison.OrdinalIgnoreCase)));
            item.Attachments.Should().Contain(attachment =>
                attachment.EvidenceKind == "ExternalStatementRow" &&
                attachment.SourceSystem == "custodian");
            item.BreakExplanation.Should().NotBeNull();
            item.BreakExplanation!.SourceSystems.Should().Contain("Sample Custodian");
            item.BreakExplanation.SourceSystems.Should().Contain("Meridian ledger");
            item.BreakExplanation.ProbableCause.Should().NotBeNullOrWhiteSpace();
            item.BreakExplanation.LedgerImpact.Should().NotBeNullOrWhiteSpace();
            item.BreakExplanation.SuggestedNextAction.Should().NotBeNullOrWhiteSpace();
            item.AuditEvents.Should().Contain(audit => audit.EventType == "ExternalStatementCaseCreated");
        }

        var openCases = await service.ListOpenCasesAsync(CancellationToken.None);
        openCases.Should().HaveCount(3);
        openCases.Should().OnlyContain(item =>
            item.Assignee == "fund-ops" &&
            item.SlaState == "OnTrack" &&
            item.Version > 0);

        var exceptions = await service.ListOpenExceptionsAsync(CancellationToken.None);
        exceptions.Should().HaveCount(3);
        exceptions.Should().OnlyContain(item => item.ImportId == created.ImportId);

        var summaries = await service.ListStatementRunsAsync(CancellationToken.None);
        var summary = summaries.Should().ContainSingle(item => item.RunId == created.RunId).Subject;
        summary.Status.Should().Be(StatementRunStatus.ReviewRequired);
        summary.OpenExceptionCount.Should().Be(3);
        // PositionMatches carries matched-item count (rows minus open exceptions); with all three
        // rows unmatched against the empty internal book, none are matched.
        summary.PositionMatches.Should().Be(0);
        summary.CompletedAtUtc.Should().BeNull();

        var queueStatuses = await service.ListQueueStatusAsync(CancellationToken.None);
        var queueStatus = queueStatuses.Should().ContainSingle(item => item.AccountId == fundAccountId).Subject;
        queueStatus.AccountCode.Should().Be(fundAccountId.ToString("D"));
        queueStatus.QueueState.Should().Be("Blocked");
        queueStatus.UnresolvedBreakCount.Should().Be(3);
        queueStatus.SignOffReady.Should().BeFalse();
        queueStatus.BlockerReason.Should().Be("Tolerance-breached breaks remain unresolved.");
        queueStatus.EvidenceLinks.Should().OnlyContain(link =>
            link.StartsWith("/api/workstation/reconciliation/break-queue/", StringComparison.OrdinalIgnoreCase));

        const string caseAction = "Assign the case, compare the external statement row to retained ledger and position evidence, then attach support before disposition.";
        var openBreaks = await service.ListOpenStatementBreaksAsync(CancellationToken.None);
        openBreaks.Should().HaveCount(2);
        openBreaks.Should().OnlyContain(item =>
            item.Owner == "fund-ops" &&
            item.SlaDueAtUtc.HasValue &&
            item.SlaWarningAtUtc.HasValue &&
            item.SlaState == "OnTrack" &&
            item.EscalationLabel == "Assigned" &&
            item.EscalationReason!.Contains("fund-ops", StringComparison.OrdinalIgnoreCase) &&
            item.RecommendedAction == caseAction &&
            item.EvidenceLink!.StartsWith("/api/workstation/reconciliation/statement-runs/", StringComparison.OrdinalIgnoreCase) &&
            item.LastObservedAtUtc.HasValue &&
            item.CreatedAtUtc.HasValue &&
            item.LastObservedAtUtc.Value >= item.CreatedAtUtc.Value);

        var reloaded = await service.GetStatementRunAsync(created.RunId!, CancellationToken.None);
        reloaded.Should().NotBeNull();
        reloaded!.Cases.Should().HaveCount(2);
        reloaded.Cases.Should().OnlyContain(item =>
            item.Attachments != null &&
            item.Attachments.Count > 0 &&
            item.CommentThreads != null &&
            item.CommentThreads.Count > 0 &&
            item.BreakExplanation != null &&
                item.AuditEvents != null &&
                item.AuditEvents.Count > 0);
        reloaded.Breaks.Should().HaveCount(2);
        reloaded.Breaks.Should().OnlyContain(item =>
            item.Owner == "fund-ops" &&
            item.SlaState == "OnTrack" &&
            item.EscalationLabel == "Assigned");
    }

    [Fact]
    public async Task ListOpenStatementBreaksAsync_WithBreachedRetainedCase_ProjectsEscalationPosture()
    {
        var detectedAt = new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero);
        var dueAt = detectedAt.AddHours(8);
        var breachedAt = dueAt.AddMinutes(30);
        var breakRecord = new ReconciliationBreakRecord(
            BreakId: "break-cash-1",
            RunId: "statement-run-1",
            ImportId: "statement-run-1",
            SourceReference: "statement-row:1",
            BreakCode: "cash-tolerance-breach",
            Category: "Cash",
            Delta: 2500.25m,
            Tolerance: 10m,
            ToleranceBreached: true,
            CreatedAtUtc: detectedAt,
            Status: "Open");
        var retainedCase = new ReconciliationCase(
            CaseId: "case:break-cash-1",
            ImportId: "statement-run-1",
            Status: "Open",
            Reason: "Cash break past SLA",
            Confidence: 0.35m,
            Rationale: "Custodian cash variance still needs close support.",
            CreatedAtUtc: detectedAt,
            History: [])
        {
            Owner = "fund-ops",
            Priority = "High",
            DueAtUtc = dueAt,
            SlaBreachedAtUtc = breachedAt,
            LastUpdatedAtUtc = breachedAt,
            EvidenceReferences = ["/api/workstation/reconciliation/statement-runs/statement-run-1"]
        };
        var service = new ReconciliationApiService(
            new StubStatementRunWorkflowService(
                Breaks: [breakRecord],
                Cases: [retainedCase]));

        var breaks = await service.ListOpenStatementBreaksAsync(CancellationToken.None);

        var projected = breaks.Should().ContainSingle().Subject;
        projected.Owner.Should().Be("fund-ops");
        projected.SlaDueAtUtc.Should().Be(dueAt);
        projected.SlaWarningAtUtc.Should().Be(dueAt.AddHours(-12));
        projected.SlaBreachedAtUtc.Should().Be(breachedAt);
        projected.SlaState.Should().Be("Breached");
        projected.EscalationLabel.Should().Be("Escalate");
        projected.EscalationReason.Should().Contain("breached SLA");
        projected.EvidenceLink.Should().Be("/api/workstation/reconciliation/statement-runs/statement-run-1");
    }

    private sealed class StubStatementRunWorkflowService(
        IReadOnlyList<ReconciliationBreakRecord>? Breaks = null,
        IReadOnlyList<ReconciliationCase>? Cases = null) : IStatementRunWorkflowService
    {
        public Task<IReadOnlyList<CanonicalStatementImport>> ListImportsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CanonicalStatementImport>>([]);

        public Task<StatementRunWorkflowResult> CreateAsync(
            StatementRunRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<StatementRunWorkflowResult?> GetAsync(
            string runId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ReconciliationBreakRecord>> ListOpenBreaksAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ReconciliationBreakRecord>>(Breaks ?? []);

        public Task<IReadOnlyList<ReconciliationCase>> ListCasesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ReconciliationCase>>(Cases ?? []);
    }
}
