using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Tenancy;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Meridian.Infrastructure.Reconciliation;
using Meridian.PortfolioRecords.Accounts;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Ui.Shared.Contracts.Reconciliation;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Ui;

public sealed class ReconciliationApiServiceTests
{
    private static readonly ReconciliationBreakQueueScope AlphaScope =
        new("tenant-alpha", "company-alpha");

    [Fact]
    public async Task CreateStatementRunAsync_CustodianStatement_ShouldPersistBreaksAndCases()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-reconciliation-api-{Guid.NewGuid():N}");
        var statementPath = Path.Combine(root, "custodian-statement.csv");
        var fundAccountId = Guid.NewGuid();
        var fundId = Guid.NewGuid();
        Directory.CreateDirectory(root);
        await File.WriteAllLinesAsync(statementPath,
        [
            "account,symbol,quantity,price,cashAmount,activityType,tradeDate",
            "FUND-1,SPY,10,500,0,position,2026-05-28",
            "FUND-1,,0,0,2500.25,cash,2026-05-28",
            "FUND-1,MSFT,1,15.75,0,fee,2026-05-28"
        ]);

        var services = new ServiceCollection();
        var accounts = new InMemoryFundAccountService();
        await accounts.CreateAccountAsync(new CreateAccountRequest(
            fundAccountId,
            AccountTypeDto.Custody,
            "FUND-1",
            "Fund 1 custody",
            "USD",
            DateTimeOffset.UtcNow,
            "test",
            FundId: fundId,
            Institution: "Sample Custodian",
            CustodianDetails: new CustodianAccountDetailsDto(
                "FUND-1", null, null, null, null, null, null, null)));
        services.AddLogging();
        services.AddSingleton<IAccountQueryService>(accounts);
        services.AddSingleton<IFundProfileTenancyRegistry>(
            new StaticFundProfileTenancyRegistry(
                new FundProfileOwnership(fundId.ToString("D"), AlphaScope.TenantId, AlphaScope.CompanyId)));
        services.AddSingleton<StatementReconciliationService>();
        services.AddSingleton<StatementReconciliationContextAdapter>();
        services.AddSingleton<IStatementReconciliationValidationService>(sp => sp.GetRequiredService<StatementReconciliationContextAdapter>());
        services.AddSingleton<IDataIntegrationIngestionService>(sp => sp.GetRequiredService<StatementReconciliationContextAdapter>());
        services.AddSingleton<IReconciliationCaseIntakeService>(sp => sp.GetRequiredService<StatementReconciliationContextAdapter>());
        services.AddSingleton<ICanonicalStatementStore>(_ => new JsonCanonicalStatementStore(root));
        services.AddSingleton<IReconciliationCaseStore>(_ => new JsonReconciliationCaseStore(root));
        services.AddSingleton<IReconciliationBreakStore>(_ => new JsonReconciliationBreakStore(root));
        services.AddSingleton<IStatementRunRecoveryRepository>(_ => new FileStatementRunRecoveryRepository(root));
        services.AddSingleton<IStatementRunMatchArtifactStore>(_ => new FileStatementRunMatchArtifactStore(root));
        services.AddSingleton<IStatementCaseworkCommitStore>(_ => new FileStatementCaseworkCommitStore(root));
        services.AddSingleton<IBrokerStatementService>(sp => new CsvBrokerStatementService(sp.GetRequiredService<ICanonicalStatementStore>()));
        // Reconcile against a small internal book that holds the statement's SPY position, so the
        // position matches exactly and only the cash and fee rows surface as breaks. This proves the
        // engine now compares statements to Meridian's records instead of to themselves.
        services.AddSingleton<IInternalReconciliationPopulationProvider>(
            new StubInternalPopulationProvider(new InternalReconciliationPopulations(
                [new InternalPortfolioPosition("internal-spy", "FUND-1", "SPY", new DateOnly(2026, 5, 28), 10m, 5000m, "internal:pos:spy")],
                [],
                [])));
        services.AddSingleton<IReconciliationFxRateProvider>(IdentityReconciliationFxRateProvider.Instance);
        services.AddSingleton<IStatementToleranceProfileProvider>(new InMemoryStatementToleranceProfileProvider());
        services.AddSingleton<IStatementRunWorkflowService, StatementRunWorkflowService>();
        services.AddSingleton<IReconciliationApiService, ReconciliationApiService>();

        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<IReconciliationApiService>();

        var created = await service.CreateStatementRunAsync(
            new StatementRunCreateDto(
                Broker: "custodian",
                SourceInstitution: "Sample Custodian",
                FundAccountId: fundAccountId.ToString("D"),
                ExternalAccountId: "FUND-1",
                StatementPeriodStart: new DateOnly(2026, 5, 1),
                StatementPeriodEnd: new DateOnly(2026, 5, 31),
                SourcePath: statementPath,
                OriginalFileName: "custodian-statement.csv",
                MappingProfileId: "canonical-csv-v1",
                ToleranceProfileId: "statement-default",
                ImportedBy: "ops-user"),
            AlphaScope,
            CancellationToken.None);

        created.Should().NotBeNull();
        created!.Status.Should().Be(StatementRunStatus.ReviewRequired);
        // The SPY position matches the internal book; cash and fee have no internal counterparts and
        // therefore remain honest unmatched breaks.
        created.MatchSummary!.StatementItemCount.Should().Be(3);
        created.MatchSummary.BreakCount.Should().Be(2);
        created.Breaks.Should().HaveCount(2);
        created.Cases.Should().HaveCount(2);
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

        var openCases = await service.ListOpenCasesAsync(AlphaScope, CancellationToken.None);
        openCases.Should().HaveCount(2);
        openCases.Should().OnlyContain(item =>
            item.Assignee == "fund-ops" &&
            item.SlaState == "OnTrack" &&
            item.Version > 0);

        var exceptions = await service.ListOpenExceptionsAsync(AlphaScope, CancellationToken.None);
        exceptions.Should().HaveCount(2);
        exceptions.Should().OnlyContain(item => item.ImportId == created.ImportId);

        var summaries = await service.ListStatementRunsAsync(AlphaScope, CancellationToken.None);
        var summary = summaries.Should().ContainSingle(item => item.RunId == created.RunId).Subject;
        summary.Status.Should().Be(StatementRunStatus.ReviewRequired);
        summary.OpenExceptionCount.Should().Be(2);
        // PositionMatches carries matched-item count (rows minus open exceptions); the SPY position
        // matches the retained internal book while the cash and fee rows remain unmatched.
        summary.PositionMatches.Should().Be(1);
        summary.CompletedAtUtc.Should().BeNull();

        var queueStatuses = await service.ListQueueStatusAsync(AlphaScope, CancellationToken.None);
        var queueStatus = queueStatuses.Should().ContainSingle(item => item.AccountId == fundAccountId).Subject;
        queueStatus.AccountCode.Should().Be(fundAccountId.ToString("D"));
        queueStatus.QueueState.Should().Be("Blocked");
        queueStatus.UnresolvedBreakCount.Should().Be(2);
        queueStatus.SignOffReady.Should().BeFalse();
        queueStatus.BlockerReason.Should().Be("Tolerance-breached breaks remain unresolved.");
        queueStatus.EvidenceLinks.Should().OnlyContain(link =>
            link.StartsWith("/api/workstation/reconciliation/break-queue/", StringComparison.OrdinalIgnoreCase));

        const string caseAction = "Assign the case, compare the external statement row to retained ledger and position evidence, then attach support before disposition.";
        var openBreaks = await service.ListOpenStatementBreaksAsync(AlphaScope, CancellationToken.None);
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

        var reloaded = await service.GetStatementRunAsync(created.RunId!, AlphaScope, CancellationToken.None);
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
        var fundAccountId = Guid.NewGuid();
        var fundId = Guid.NewGuid();
        var import = StatementImport("statement-run-1", fundAccountId);
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
        var accounts = await CreateAccountsAsync((fundAccountId, fundId, "ACCOUNT-1"));
        var service = new ReconciliationApiService(
            new StubStatementRunWorkflowService(
                Imports: [import],
                Breaks: [breakRecord],
                Cases: [retainedCase]),
            accounts,
            new StaticFundProfileTenancyRegistry(
                new FundProfileOwnership(fundId.ToString("D"), AlphaScope.TenantId, AlphaScope.CompanyId)));

        var breaks = await service.ListOpenStatementBreaksAsync(AlphaScope, CancellationToken.None);

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

    [Fact]
    public async Task ScopedReadsAndMutations_ShouldExcludeForeignAndUnownedStatementRuns()
    {
        var alphaAccountId = Guid.NewGuid();
        var alphaFundId = Guid.NewGuid();
        var betaAccountId = Guid.NewGuid();
        var betaFundId = Guid.NewGuid();
        var unownedAccountId = Guid.NewGuid();
        var unownedFundId = Guid.NewGuid();
        var imports = new[]
        {
            StatementImport("run-alpha", alphaAccountId),
            StatementImport("run-beta", betaAccountId),
            StatementImport("run-unowned", unownedAccountId)
        };
        var mismatchedBreak = new ReconciliationBreakRecord(
            BreakId: "break-mismatched",
            RunId: "run-alpha",
            ImportId: "run-beta",
            SourceReference: "statement-row:foreign",
            BreakCode: "cash-tolerance-breach",
            Category: "Cash",
            Delta: 100m,
            Tolerance: 1m,
            ToleranceBreached: true,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            Status: "Open");
        var workflow = new StubStatementRunWorkflowService(
            Imports: imports,
            Breaks: [mismatchedBreak]);
        var accounts = await CreateAccountsAsync(
            (alphaAccountId, alphaFundId, "ALPHA"),
            (betaAccountId, betaFundId, "BETA"),
            (unownedAccountId, unownedFundId, "UNOWNED"));
        var service = new ReconciliationApiService(
            workflow,
            accounts,
            new StaticFundProfileTenancyRegistry(
                new FundProfileOwnership(alphaFundId.ToString("D"), "tenant-alpha", "company-alpha"),
                new FundProfileOwnership(betaFundId.ToString("D"), "tenant-beta", "company-beta")));

        var alphaRuns = await service.ListStatementRunsAsync(AlphaScope);
        var betaDetail = await service.GetStatementRunAsync("run-beta", AlphaScope);
        var betaOwned = await service.OwnsStatementRunAsync("run-beta", AlphaScope);
        var betaReconcile = await service.ReconcileStatementRunAsync(
            "run-beta",
            new StatementRunReconcileRequestDto("alpha-operator"),
            AlphaScope);
        var betaCreate = await service.CreateStatementRunAsync(
            new StatementRunCreateDto(
                Broker: "custodian",
                SourceInstitution: "Beta Custodian",
                FundAccountId: betaAccountId.ToString("D"),
                ExternalAccountId: "BETA",
                StatementPeriodStart: new DateOnly(2026, 5, 1),
                StatementPeriodEnd: new DateOnly(2026, 5, 31),
                SourcePath: "/retained/run-beta.csv",
                OriginalFileName: "run-beta.csv",
                MappingProfileId: "mapping-v1",
                ToleranceProfileId: "tolerance-v1",
                ImportedBy: "alpha-operator"),
            AlphaScope);
        var companyMismatchRuns = await service.ListStatementRunsAsync(
            new ReconciliationBreakQueueScope("tenant-alpha", "company-other"));
        var companyMismatchCases = await service.ListOpenCasesAsync(
            new ReconciliationBreakQueueScope("tenant-alpha", "company-other"));

        alphaRuns.Should().ContainSingle(item => item.RunId == "run-alpha");
        alphaRuns.Should().NotContain(
            item => item.RunId == "run-beta" || item.RunId == "run-unowned");
        betaDetail.Should().BeNull();
        betaOwned.Should().BeFalse();
        betaReconcile.Should().BeNull();
        betaCreate.Should().BeNull();
        companyMismatchRuns.Should().BeEmpty();
        companyMismatchCases.Should().BeEmpty();
        workflow.GetCallCount.Should().Be(0);
        workflow.CreateCallCount.Should().Be(0);
        workflow.ListOpenBreaksCallCount.Should().Be(1);
        workflow.ListCasesCallCount.Should().Be(0);

        var mismatchedDetail = await service.GetStatementRunAsync("run-alpha", AlphaScope);

        mismatchedDetail.Should().BeNull();
        workflow.GetCallCount.Should().Be(1);
    }

    private sealed class StubInternalPopulationProvider(InternalReconciliationPopulations populations)
        : IInternalReconciliationPopulationProvider
    {
        public Task<InternalReconciliationPopulations> GetPopulationsAsync(
            InternalReconciliationPopulationContext context,
            CancellationToken ct = default)
            => Task.FromResult(populations);
    }

    private static async Task<InMemoryFundAccountService> CreateAccountsAsync(
        params (Guid AccountId, Guid FundId, string AccountCode)[] definitions)
    {
        var accounts = new InMemoryFundAccountService();
        foreach (var definition in definitions)
        {
            await accounts.CreateAccountAsync(new CreateAccountRequest(
                definition.AccountId,
                AccountTypeDto.Custody,
                definition.AccountCode,
                $"{definition.AccountCode} custody",
                "USD",
                DateTimeOffset.UtcNow,
                "test",
                FundId: definition.FundId));
        }

        return accounts;
    }

    private static CanonicalStatementImport StatementImport(string importId, Guid fundAccountId)
        => new(
            importId,
            "custodian",
            new DateOnly(2026, 5, 31),
            new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero),
            $"/retained/{importId}.csv",
            new string('A', 64),
            RawRowCount: 1,
            NormalizedRowCount: 1)
        {
            SourceInstitution = "Sample Custodian",
            FundAccountId = fundAccountId.ToString("D"),
            ExternalAccountId = fundAccountId.ToString("D"),
            StatementPeriodStart = new DateOnly(2026, 5, 1),
            StatementPeriodEnd = new DateOnly(2026, 5, 31),
            OriginalFileName = $"{importId}.csv",
            SourceFileHash = new string('A', 64),
            MappingProfileId = "mapping-v1",
            ToleranceProfileId = "tolerance-v1",
            ImportedBy = "test"
        };

    private sealed class StaticFundProfileTenancyRegistry(params FundProfileOwnership[] ownerships)
        : IFundProfileTenancyRegistry
    {
        private readonly IReadOnlyDictionary<string, FundProfileOwnership> _ownerships =
            ownerships.ToDictionary(static item => item.FundProfileId, StringComparer.OrdinalIgnoreCase);

        public Task<FundProfileOwnership> BindAsync(
            string fundProfileId,
            string tenantId,
            string? companyId = null,
            CancellationToken ct = default)
            => Task.FromResult(
                _ownerships.TryGetValue(fundProfileId, out var owner)
                    ? owner
                    : new FundProfileOwnership(fundProfileId, tenantId, companyId));

        public Task<FundProfileOwnership?> ResolveAsync(
            string fundProfileId,
            CancellationToken ct = default)
            => Task.FromResult<FundProfileOwnership?>(
                _ownerships.TryGetValue(fundProfileId, out var owner)
                    ? owner
                    : null);

        public async Task<bool> IsAccessibleAsync(
            string fundProfileId,
            string tenantId,
            string? companyId = null,
            CancellationToken ct = default)
        {
            var owner = await ResolveAsync(fundProfileId, ct);
            return owner is not null
                && owner.IsHeldBy(tenantId)
                && !string.IsNullOrWhiteSpace(owner.CompanyId)
                && !string.IsNullOrWhiteSpace(companyId)
                && string.Equals(owner.CompanyId, companyId, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class StubStatementRunWorkflowService(
        IReadOnlyList<CanonicalStatementImport>? Imports = null,
        IReadOnlyList<ReconciliationBreakRecord>? Breaks = null,
        IReadOnlyList<ReconciliationCase>? Cases = null) : IStatementRunWorkflowService
    {
        public int GetCallCount { get; private set; }
        public int CreateCallCount { get; private set; }
        public int ListOpenBreaksCallCount { get; private set; }
        public int ListCasesCallCount { get; private set; }

        public Task<IReadOnlyList<CanonicalStatementImport>> ListImportsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CanonicalStatementImport>>(Imports ?? []);

        public Task<StatementRunWorkflowResult> CreateAsync(
            StatementRunRequest request,
            CancellationToken cancellationToken = default)
        {
            CreateCallCount++;
            throw new NotSupportedException();
        }

        public Task<StatementRunWorkflowResult?> GetAsync(
            string runId,
            CancellationToken cancellationToken = default)
        {
            GetCallCount++;
            var import = Imports?.FirstOrDefault(item =>
                string.Equals(item.ImportId, runId, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(
                import is null
                    ? null
                    : new StatementRunWorkflowResult(
                        import,
                        (Breaks ?? [])
                            .Where(item =>
                                string.Equals(item.ImportId, runId, StringComparison.OrdinalIgnoreCase)
                                || string.Equals(item.RunId, runId, StringComparison.OrdinalIgnoreCase))
                            .ToArray(),
                        (Cases ?? [])
                            .Where(item => string.Equals(item.ImportId, runId, StringComparison.OrdinalIgnoreCase))
                            .ToArray()));
        }

        public Task<IReadOnlyList<ReconciliationBreakRecord>> ListOpenBreaksAsync(CancellationToken cancellationToken = default)
        {
            ListOpenBreaksCallCount++;
            return Task.FromResult<IReadOnlyList<ReconciliationBreakRecord>>(Breaks ?? []);
        }

        public Task<IReadOnlyList<ReconciliationCase>> ListCasesAsync(CancellationToken cancellationToken = default)
        {
            ListCasesCallCount++;
            return Task.FromResult<IReadOnlyList<ReconciliationCase>>(Cases ?? []);
        }
    }
}
