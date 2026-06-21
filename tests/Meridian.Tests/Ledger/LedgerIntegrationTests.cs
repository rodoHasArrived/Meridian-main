using FluentAssertions;
using FsCheck.Xunit;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

public sealed class LedgerIntegrationTests
{
    [Fact]
    public void Post_WithUnbalancedJournal_ThrowsLedgerValidationException()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var journalId = Guid.NewGuid();
        var timestamp = DateTimeOffset.UtcNow;
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue", LedgerAccountType.Revenue);

        var entry = new JournalEntry(
            journalId,
            timestamp,
            "bad-entry",
            new[]
            {
                new LedgerEntry(Guid.NewGuid(), journalId, timestamp, cash, 100m, 0m, "bad-entry"),
                new LedgerEntry(Guid.NewGuid(), journalId, timestamp, revenue, 0m, 50m, "bad-entry"),
            });

        var action = () => ledger.Post(entry);

        action.Should().Throw<LedgerValidationException>();
    }

    [Fact]
    public void TrialBalance_UsesDelegatedNetBalanceRules()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var timestamp = DateTimeOffset.UtcNow;
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue", LedgerAccountType.Revenue);

        ledger.PostLines(timestamp, "sale", new[]
        {
            (cash, 100m, 0m),
            (revenue, 0m, 100m),
        });

        var trialBalance = ledger.TrialBalance();

        trialBalance[cash].Should().Be(100m);
        trialBalance[revenue].Should().Be(100m);
    }

    [Fact]
    public void ProjectLedgerBook_CanTrackParallelLedgersPerProject()
    {
        var projectLedgers = new ProjectLedgerBook("project-alpha");
        var actualKey = new LedgerBookKey("project-alpha", "core", LedgerViewKind.Actual);
        var historicalKey = new LedgerBookKey("project-alpha", "core", LedgerViewKind.Historical);
        var securityMasterKey = new LedgerBookKey("project-alpha", "cashflows", LedgerViewKind.SecurityMaster, "baseline");

        var actual = projectLedgers.GetOrCreate(actualKey);
        var historical = projectLedgers.GetOrCreate(historicalKey);
        var securityMaster = projectLedgers.GetOrCreate(securityMasterKey);

        actual.Should().NotBeSameAs(historical);
        actual.Should().NotBeSameAs(securityMaster);
        projectLedgers.LedgerKeys.Should().HaveCount(3);
        projectLedgers.TryGetLedger(actualKey, out var sameActual).Should().BeTrue();
        sameActual.Should().BeSameAs(actual);
    }

    [Fact]
    public void ProjectLedgerBook_CanBuildConsolidatedTrialBalanceAcrossFilteredLedgers()
    {
        var projectLedgers = new ProjectLedgerBook("project-alpha");
        var actualKey = new LedgerBookKey("project-alpha", "core", LedgerViewKind.Actual);
        var historicalKey = new LedgerBookKey("project-alpha", "core", LedgerViewKind.Historical, "baseline");
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue", LedgerAccountType.Revenue);

        projectLedgers.GetOrCreate(actualKey).PostLines(
            DateTimeOffset.UtcNow,
            "actual-sale",
            new[]
            {
                (cash, 100m, 0m),
                (revenue, 0m, 100m),
            });

        projectLedgers.GetOrCreate(historicalKey).PostLines(
            DateTimeOffset.UtcNow,
            "historical-sale",
            new[]
            {
                (cash, 20m, 0m),
                (revenue, 0m, 20m),
            });

        var allBalances = projectLedgers.ConsolidatedTrialBalance();
        var actualOnlyBalances = projectLedgers.ConsolidatedTrialBalance(ledgerView: LedgerViewKind.Actual);
        var baselineOnlyBalances = projectLedgers.ConsolidatedTrialBalance(
            ledgerBook: "core",
            scenarioId: "baseline");

        allBalances[cash].Should().Be(120m);
        allBalances[revenue].Should().Be(120m);
        actualOnlyBalances[cash].Should().Be(100m);
        actualOnlyBalances[revenue].Should().Be(100m);
        baselineOnlyBalances[cash].Should().Be(20m);
        baselineOnlyBalances[revenue].Should().Be(20m);
    }

    [Fact]
    public void ProjectLedgerBook_CanBuildConsolidatedSnapshotAsOfTimestamp()
    {
        var projectLedgers = new ProjectLedgerBook("project-alpha");
        var actualKey = new LedgerBookKey("project-alpha", "core", LedgerViewKind.Actual);
        var historicalKey = new LedgerBookKey("project-alpha", "core", LedgerViewKind.Historical);
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue", LedgerAccountType.Revenue);
        var t0 = new DateTimeOffset(2025, 01, 01, 0, 0, 0, TimeSpan.Zero);
        var t1 = t0.AddHours(1);
        var t2 = t1.AddHours(1);

        projectLedgers.GetOrCreate(actualKey).PostLines(
            t1,
            "actual-sale",
            new[]
            {
                (cash, 100m, 0m),
                (revenue, 0m, 100m),
            });

        projectLedgers.GetOrCreate(historicalKey).PostLines(
            t2,
            "historical-sale",
            new[]
            {
                (cash, 20m, 0m),
                (revenue, 0m, 20m),
            });

        var snapshotAtT1 = projectLedgers.ConsolidatedSnapshotAsOf(t1);
        var snapshotAtT2 = projectLedgers.ConsolidatedSnapshotAsOf(t2);

        snapshotAtT1.Balances[cash].Should().Be(100m);
        snapshotAtT1.Balances[revenue].Should().Be(100m);
        snapshotAtT1.JournalEntryCount.Should().Be(1);
        snapshotAtT1.LedgerEntryCount.Should().Be(2);

        snapshotAtT2.Balances[cash].Should().Be(120m);
        snapshotAtT2.Balances[revenue].Should().Be(120m);
        snapshotAtT2.JournalEntryCount.Should().Be(2);
        snapshotAtT2.LedgerEntryCount.Should().Be(4);
    }

    [Fact]
    public void ProjectLedgerBook_ConsolidatedReads_ShouldFilterByLineDimensionsAcrossBooks()
    {
        var projectLedgers = new ProjectLedgerBook("fund-alpha");
        var fundBook = projectLedgers.GetOrCreate(new LedgerBookKey("fund-alpha", "Fund", LedgerViewKind.Actual));
        var sleeveBook = projectLedgers.GetOrCreate(new LedgerBookKey("fund-alpha", "Sleeve:credit", LedgerViewKind.Actual));
        var cash = new LedgerAccount("Assets:Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue:Fees", LedgerAccountType.Revenue);
        var t1 = DateTimeOffset.Parse("2026-05-28T09:00:00Z");
        var t2 = t1.AddHours(1);
        var alphaDimensions = new LedgerLineDimensionSet(
            FundId: "fund-alpha",
            EntityId: "entity-alpha",
            SleeveId: "sleeve-credit",
            ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Department"] = "Investments"
            });
        var betaDimensions = alphaDimensions with
        {
            EntityId = "entity-beta",
            ExternalGlDimensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Department"] = "Administration"
            }
        };

        fundBook.PostLines(
            t1,
            "alpha fund fee",
            new[]
            {
                (cash, 100m, 0m, (LedgerLineDimensionSet?)alphaDimensions),
                (revenue, 0m, 100m, (LedgerLineDimensionSet?)alphaDimensions),
            });
        sleeveBook.PostLines(
            t2,
            "alpha sleeve fee",
            new[]
            {
                (cash, 25m, 0m, (LedgerLineDimensionSet?)alphaDimensions),
                (revenue, 0m, 25m, (LedgerLineDimensionSet?)alphaDimensions),
            });
        sleeveBook.PostLines(
            t2,
            "beta sleeve fee",
            new[]
            {
                (cash, 50m, 0m, (LedgerLineDimensionSet?)betaDimensions),
                (revenue, 0m, 50m, (LedgerLineDimensionSet?)betaDimensions),
            });

        var scope = new LedgerLineDimensionSet(
            FundId: "fund-alpha",
            EntityId: "entity-alpha",
            ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["department"] = "investments"
            });

        var trialBalance = projectLedgers.ConsolidatedTrialBalance(lineDimensions: scope);
        var snapshot = projectLedgers.ConsolidatedSnapshotAsOf(t2, lineDimensions: scope);
        var summaries = projectLedgers.ConsolidatedAccountSummaries(lineDimensions: scope);
        var journals = projectLedgers.ConsolidatedJournalEntries(new LedgerQuery(LineDimensions: scope));

        trialBalance[cash].Should().Be(125m);
        trialBalance[revenue].Should().Be(125m);
        snapshot.Balances[cash].Should().Be(125m);
        snapshot.JournalEntryCount.Should().Be(2);
        snapshot.LedgerEntryCount.Should().Be(4);
        summaries.Single(summary => summary.Account == cash).Balance.Should().Be(125m);
        journals.Select(entry => entry.Description).Should().Equal("alpha fund fee", "alpha sleeve fee");
    }

    [Fact]
    public void ProjectLedgerBook_FilteredSnapshot_FiltersByBookViewAndScenario()
    {
        var projectLedgers = new ProjectLedgerBook("project-alpha");
        var actualKey = new LedgerBookKey("project-alpha", "core", LedgerViewKind.Actual);
        var historicalKey = new LedgerBookKey("project-alpha", "core", LedgerViewKind.Historical, "baseline");
        var replayKey = new LedgerBookKey("project-alpha", "cashflows", LedgerViewKind.Historical, "stress");

        projectLedgers.GetOrCreate(actualKey);
        projectLedgers.GetOrCreate(historicalKey);
        projectLedgers.GetOrCreate(replayKey);

        var filtered = projectLedgers.FilteredSnapshot(
            ledgerBook: "core",
            ledgerView: LedgerViewKind.Historical,
            scenarioId: "baseline");

        filtered.Should().HaveCount(1);
        filtered.Keys.Should().ContainSingle(key =>
            key.LedgerBook == "core" &&
            key.LedgerView == LedgerViewKind.Historical &&
            key.ScenarioId == "baseline");
    }

    [Fact]
    public void ProjectLedgerBook_FilteredLedgerKeys_ReturnsSortedFilteredKeys()
    {
        var projectLedgers = new ProjectLedgerBook("project-alpha");
        var keyA = new LedgerBookKey("project-alpha", "core", LedgerViewKind.Historical, "baseline");
        var keyB = new LedgerBookKey("project-alpha", "core", LedgerViewKind.Actual);
        var keyC = new LedgerBookKey("project-alpha", "cashflows", LedgerViewKind.Actual);

        projectLedgers.GetOrCreate(keyA);
        projectLedgers.GetOrCreate(keyB);
        projectLedgers.GetOrCreate(keyC);

        var filtered = projectLedgers.FilteredLedgerKeys(ledgerBook: "core");

        filtered.Should().HaveCount(2);
        filtered[0].Should().Be(keyB.Normalize());
        filtered[1].Should().Be(keyA.Normalize());
    }

    [Fact]
    public void ProjectLedgerBook_CanQueryConsolidatedJournalEntriesAcrossLedgers()
    {
        var projectLedgers = new ProjectLedgerBook("project-alpha");
        var coreActual = projectLedgers.GetOrCreate(new LedgerBookKey("project-alpha", "core", LedgerViewKind.Actual));
        var coreHistorical = projectLedgers.GetOrCreate(new LedgerBookKey("project-alpha", "core", LedgerViewKind.Historical));
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue", LedgerAccountType.Revenue);
        var timestamp = DateTimeOffset.UtcNow;

        coreActual.PostLines(
            timestamp,
            "core actual trade",
            new[] { (cash, 100m, 0m), (revenue, 0m, 100m) },
            new JournalEntryMetadata(ProjectId: "project-alpha", LedgerBook: "core", LedgerView: LedgerViewKind.Actual, ActivityType: "Trade"));
        coreHistorical.PostLines(
            timestamp.AddMinutes(1),
            "core historical trade",
            new[] { (cash, 20m, 0m), (revenue, 0m, 20m) },
            new JournalEntryMetadata(ProjectId: "project-alpha", LedgerBook: "core", LedgerView: LedgerViewKind.Historical, ActivityType: "Trade"));

        var consolidated = projectLedgers.ConsolidatedJournalEntries(
            new LedgerQuery(ActivityType: "Trade", ProjectId: "project-alpha", LedgerBook: "core"),
            ledgerBook: "core");

        consolidated.Should().HaveCount(2);
        consolidated[0].Description.Should().Be("core actual trade");
        consolidated[1].Description.Should().Be("core historical trade");
    }

    [Fact]
    public void LedgerQuery_CanFilterPrivateCapitalTreasuryMetadata()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var capital = new LedgerAccount("Capital Contributions", LedgerAccountType.Equity);
        var effectiveDate = new DateOnly(2026, 6, 30);

        ledger.PostLines(
            DateTimeOffset.Parse("2026-06-30T20:00:00Z"),
            "capital call",
            new[] { (cash, 100m, 0m), (capital, 0m, 100m) },
            new JournalEntryMetadata(
                ActivityType: "CapitalCall",
                EffectiveDate: effectiveDate,
                IdempotencyKey: "capital-call:fund-alpha:20260630",
                FundEventId: "fund-event:fund-alpha:capital-call:20260630",
                FundEventType: "CapitalCall",
                CapitalAccountId: "capital-account:fund-alpha:lp-1",
                InvestorId: "investor:lp-1",
                PaymentIntentId: "payment:fund-alpha:capital-call:20260630",
                SettlementReference: "settlement:fund-alpha:capital-call:20260630"));

        var filtered = ledger.GetJournalEntries(new LedgerQuery(
            EffectiveDate: effectiveDate,
            IdempotencyKey: "capital-call:fund-alpha:20260630",
            FundEventId: "fund-event:fund-alpha:capital-call:20260630",
            CapitalAccountId: "capital-account:fund-alpha:lp-1",
            PaymentIntentId: "payment:fund-alpha:capital-call:20260630"));

        filtered.Should().ContainSingle();
        filtered[0].Metadata.FundEventType.Should().Be("CapitalCall");
        filtered[0].Metadata.SettlementReference.Should().Be("settlement:fund-alpha:capital-call:20260630");
    }

    [Fact]
    public void AutomatedJournalDraftProjector_PreservesEventAccountingMetadataAndTypedEvidence()
    {
        var evidence = new JournalEvidenceReference(
            "evidence-dividend-1",
            "evidence://dividend/notice-1",
            "Source",
            "Custodian",
            DateTimeOffset.Parse("2026-05-30T12:00:00Z"),
            "fund-controller");

        var draft = AutomatedJournalDraftProjector.Project(new AutomatedJournalEvent(
            AutomatedJournalEventKind.DividendDeclared,
            " aapl ",
            42.50m,
            DateTimeOffset.Parse("2026-05-31T21:00:00Z"),
            FinancialAccountId: " broker-1 ",
            SourceEventId: "source-event-dividend-1",
            EffectiveDate: new DateOnly(2026, 5, 31),
            IdempotencyKey: "dividend:aapl:20260531",
            EvidenceReferences: [evidence]));

        draft.Metadata.Symbol.Should().Be("AAPL");
        draft.Metadata.FinancialAccountId.Should().Be("broker-1");
        draft.Metadata.EffectiveDate.Should().Be(new DateOnly(2026, 5, 31));
        draft.Metadata.IdempotencyKey.Should().Be("dividend:aapl:20260531");
        draft.Metadata.Tags.Should().ContainKey("sourceEventId").WhoseValue.Should().Be("source-event-dividend-1");
        draft.Metadata.EvidenceReferences.Should().ContainSingle(item =>
            item.EvidenceId == "evidence-dividend-1" &&
            item.Uri == "evidence://dividend/notice-1" &&
            item.Kind == "Source");
    }
    [Fact]
    public void PrivateCapitalFundEventLedgerProjector_ProjectsPostedEventWithLedgerCapitalEvidenceApprovalAndReportOutput()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var postedAt = DateTimeOffset.Parse("2026-06-30T20:00:00Z");
        var periodStart = DateTimeOffset.Parse("2026-06-01T00:00:00Z");
        var periodEnd = DateTimeOffset.Parse("2026-06-30T23:59:59Z");
        var approvalId = Guid.Parse("461A7BF7-BC7B-41CC-8E92-4DB732694C83");

        ledger.PostLines(
            postedAt,
            "capital call - LP 1",
            new[]
            {
                (LedgerAccounts.CashAccount("fund-alpha-operating"), 250_000m, 0m),
                (LedgerAccounts.InvestorCapitalFor("investor:lp-1"), 0m, 250_000m),
            },
            new JournalEntryMetadata(
                ActivityType: "CapitalCall",
                ProjectId: "fund-alpha",
                EffectiveDate: new DateOnly(2026, 6, 30),
                IdempotencyKey: "capital-call:fund-alpha:lp-1:20260630",
                FundEventId: "fund-event:fund-alpha:capital-call:20260630:lp-1",
                FundEventType: "CapitalCall",
                CapitalAccountId: "capital-account:fund-alpha:lp-1",
                InvestorId: "investor:lp-1",
                PaymentIntentId: "payment:fund-alpha:capital-call:lp-1:20260630",
                SettlementReference: "settlement:fund-alpha:capital-call:lp-1:20260630",
                Tags: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["evidenceLinks"] = "source:capital-call-notice:20260630|settlement:wire-confirmation:20260630",
                    ["automatedJournalApprovalId"] = approvalId.ToString("D"),
                    ["automatedJournalStatus"] = "Approved",
                    ["approvedBy"] = "controller"
                }));

        var reportPack = LedgerReportPackBuilder.Build(
                ledger,
                new LedgerReportPackRequest(
                    "lrp-private-capital-2026-06",
                    "fund-alpha",
                    "2026-06",
                    periodStart,
                    periodEnd,
                    periodEnd,
                    "usd",
                    "controller",
                    DateTimeOffset.Parse("2026-07-01T13:00:00Z"),
                    sourceRunId: "run-private-capital-close-2026-06",
                    reconciliationEvidenceLinks: ["reconciliation:capital-call:20260630"],
                    approvalEvidenceLinks: ["approval:capital-call:controller"]))
            .Validate("controller", DateTimeOffset.Parse("2026-07-01T14:00:00Z"), "capital account totals validated", ["evidence:capital-account-validation"])
            .Approve("controller", DateTimeOffset.Parse("2026-07-01T15:00:00Z"), "approved investor statement output", ["approval:capital-account-statement"])
            .Publish("controller", DateTimeOffset.Parse("2026-07-01T16:00:00Z"), "published investor statement output", ["vault:report-pack/lrp-private-capital-2026-06"]);

        var projection = PrivateCapitalFundEventLedgerProjector.Project(ledger, [reportPack]);

        projection.EventCount.Should().Be(1);
        projection.PostingReadyCount.Should().Be(1);
        projection.ReportReadyCount.Should().Be(1);

        var fundEvent = projection.Events.Should().ContainSingle().Subject;
        fundEvent.FundEventId.Should().Be("fund-event:fund-alpha:capital-call:20260630:lp-1");
        fundEvent.FundEventType.Should().Be("CapitalCall");
        fundEvent.EffectiveDate.Should().Be(new DateOnly(2026, 6, 30));
        fundEvent.CapitalAccountId.Should().Be("capital-account:fund-alpha:lp-1");
        fundEvent.InvestorId.Should().Be("investor:lp-1");
        fundEvent.PaymentIntentId.Should().Be("payment:fund-alpha:capital-call:lp-1:20260630");
        fundEvent.SettlementReference.Should().Be("settlement:fund-alpha:capital-call:lp-1:20260630");
        fundEvent.ApprovalState.Should().Be(PrivateCapitalFundEventApprovalState.Approved);
        fundEvent.ApprovalId.Should().Be(approvalId.ToString("D"));
        fundEvent.ApprovedBy.Should().Be("controller");
        fundEvent.IsPostingReady.Should().BeTrue();
        fundEvent.IsReportReady.Should().BeTrue();
        fundEvent.Issues.Should().BeEmpty();
        fundEvent.EvidenceLinks.Should().BeEquivalentTo(
        [
            "source:capital-call-notice:20260630",
            "settlement:wire-confirmation:20260630"
        ]);

        var ledgerImpact = fundEvent.LedgerImpacts.Should().ContainSingle().Subject;
        ledgerImpact.TotalDebits.Should().Be(250_000m);
        ledgerImpact.TotalCredits.Should().Be(250_000m);
        ledgerImpact.IsBalanced.Should().BeTrue();
        ledgerImpact.Lines.Should().Contain(line =>
            line.AccountName == "Investor Capital" &&
            line.AccountType == LedgerAccountType.Equity &&
            line.Credit == 250_000m &&
            line.NetNormalBalanceImpact == 250_000m);

        var capitalAccount = fundEvent.CapitalAccountImpacts.Should().ContainSingle().Subject;
        capitalAccount.CapitalAccountId.Should().Be("capital-account:fund-alpha:lp-1");
        capitalAccount.InvestorId.Should().Be("investor:lp-1");
        capitalAccount.AccountName.Should().Be("Investor Capital");
        capitalAccount.NetCapitalAccountImpact.Should().Be(250_000m);
        capitalAccount.LedgerEntryIds.Should().ContainSingle();

        var reportOutput = fundEvent.ReportOutputs.Should().ContainSingle().Subject;
        reportOutput.ReportId.Should().Be("lrp-private-capital-2026-06");
        reportOutput.Status.Should().Be(LedgerReportPackLifecycleStatus.Published);
        reportOutput.IsPublished.Should().BeTrue();
        reportOutput.SignatureHash.Should().HaveLength(64);
        reportOutput.ArtifactNames.Should().Contain("line-provenance.csv");
        reportOutput.EvidenceLinks.Should().Contain("vault:report-pack/lrp-private-capital-2026-06");
        reportOutput.EvidenceLinks.Should().Contain(link => link.StartsWith("ledger-entry:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PrivateCapitalFundEventLedgerProjector_FlagsIncompletePostedEventContext()
    {
        var ledger = new Meridian.Ledger.Ledger();

        ledger.PostLines(
            DateTimeOffset.Parse("2026-06-30T20:00:00Z"),
            "incomplete capital call",
            new[]
            {
                (LedgerAccounts.CashAccount("fund-alpha-operating"), 100m, 0m),
                (LedgerAccounts.InvestorCapitalFor("investor:lp-1"), 0m, 100m),
            },
            new JournalEntryMetadata(
                ActivityType: "CapitalCall",
                FundEventId: "fund-event:fund-alpha:capital-call:incomplete",
                FundEventType: "CapitalCall"));

        var reportPack = LedgerReportPackBuilder.Build(
                ledger,
                new LedgerReportPackRequest(
                    "lrp-private-capital-incomplete",
                    "fund-alpha",
                    "2026-06",
                    new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 6, 30, 23, 59, 59, TimeSpan.Zero),
                    new DateTimeOffset(2026, 6, 30, 23, 59, 59, TimeSpan.Zero),
                    "usd",
                    "controller",
                    DateTimeOffset.Parse("2026-07-01T13:00:00Z"),
                    sourceRunId: "run-private-capital-incomplete",
                    reconciliationEvidenceLinks: ["reconciliation:capital-call:incomplete"],
                    approvalEvidenceLinks: ["approval:capital-call:incomplete"]))
            .Validate("controller", DateTimeOffset.Parse("2026-07-01T14:00:00Z"), "incomplete event retained for review", ["evidence:capital-account-review"])
            .Approve("controller", DateTimeOffset.Parse("2026-07-01T15:00:00Z"), "approved package publication", ["approval:capital-account-review"])
            .Publish("controller", DateTimeOffset.Parse("2026-07-01T16:00:00Z"), "published package", ["vault:report-pack/lrp-private-capital-incomplete"]);

        var projection = PrivateCapitalFundEventLedgerProjector.Project(ledger, [reportPack]);

        projection.EventCount.Should().Be(1);
        projection.PostingReadyCount.Should().Be(0);
        projection.ReportReadyCount.Should().Be(0);
        var fundEvent = projection.Events.Single();
        fundEvent.IsPostingReady.Should().BeFalse();
        fundEvent.IsReportReady.Should().BeFalse();
        fundEvent.ReportOutputs.Should().ContainSingle(output =>
            output.IsPublished &&
            output.ReportId == "lrp-private-capital-incomplete");
        fundEvent.ApprovalState.Should().Be(PrivateCapitalFundEventApprovalState.Missing);
        fundEvent.CapitalAccountImpacts.Should().BeEmpty();
        fundEvent.EvidenceLinks.Should().BeEmpty();
        fundEvent.Issues.Select(static issue => issue.Code).Should().Contain(
        [
            "private-capital.capital-account-missing",
            "private-capital.effective-date-missing",
            "private-capital.idempotency-key-missing",
            "private-capital.evidence-missing",
            "private-capital.approval-missing",
            "private-capital.capital-account-impact-missing"
        ]);
        fundEvent.Issues.Should().Contain(issue =>
            issue.Code == "private-capital.evidence-missing" &&
            issue.Severity == PrivateCapitalFundEventIssueSeverity.Critical);
    }

    [Fact]
    public void PrivateCapitalFundEventLedgerProjector_BlocksOneFundEventMappedToMultipleCapitalAccounts()
    {
        var ledger = new Meridian.Ledger.Ledger();
        const string fundEventId = "fund-event:fund-alpha:capital-call:conflicted";

        ledger.PostLines(
            DateTimeOffset.Parse("2026-06-30T20:00:00Z"),
            "capital call - LP 1",
            new[]
            {
                (LedgerAccounts.CashAccount("fund-alpha-operating"), 100m, 0m),
                (LedgerAccounts.InvestorCapitalFor("investor:lp-1"), 0m, 100m),
            },
            new JournalEntryMetadata(
                ActivityType: "CapitalCall",
                EffectiveDate: new DateOnly(2026, 6, 30),
                IdempotencyKey: "capital-call:fund-alpha:conflicted:lp-1",
                FundEventId: fundEventId,
                FundEventType: "CapitalCall",
                CapitalAccountId: "capital-account:fund-alpha:lp-1",
                InvestorId: "investor:lp-1",
                PaymentIntentId: "payment:capital-call-conflicted:lp-1",
                SettlementReference: "settlement:capital-call-conflicted:lp-1",
                Tags: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["evidenceLinks"] = "source:capital-call-conflicted:lp-1",
                    ["automatedJournalStatus"] = "Posted",
                    ["automatedJournalApprovalId"] = "approval:capital-call-conflicted",
                    ["approvedBy"] = "controller"
                }));
        ledger.PostLines(
            DateTimeOffset.Parse("2026-06-30T20:05:00Z"),
            "capital call - LP 2",
            new[]
            {
                (LedgerAccounts.CashAccount("fund-alpha-operating"), 200m, 0m),
                (LedgerAccounts.InvestorCapitalFor("investor:lp-2"), 0m, 200m),
            },
            new JournalEntryMetadata(
                ActivityType: "CapitalCall",
                EffectiveDate: new DateOnly(2026, 6, 30),
                IdempotencyKey: "capital-call:fund-alpha:conflicted:lp-2",
                FundEventId: fundEventId,
                FundEventType: "CapitalCall",
                CapitalAccountId: "capital-account:fund-alpha:lp-2",
                InvestorId: "investor:lp-2",
                PaymentIntentId: "payment:capital-call-conflicted:lp-2",
                SettlementReference: "settlement:capital-call-conflicted:lp-2",
                Tags: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["evidenceLinks"] = "source:capital-call-conflicted:lp-2",
                    ["automatedJournalStatus"] = "Posted",
                    ["automatedJournalApprovalId"] = "approval:capital-call-conflicted",
                    ["approvedBy"] = "controller"
                }));

        var projection = PrivateCapitalFundEventLedgerProjector.Project(ledger);

        projection.EventCount.Should().Be(1);
        projection.PostingReadyCount.Should().Be(0);
        projection.ReportReadyCount.Should().Be(0);
        var fundEvent = projection.Events.Should().ContainSingle().Subject;
        fundEvent.FundEventId.Should().Be(fundEventId);
        fundEvent.IsPostingReady.Should().BeFalse();
        fundEvent.HasCriticalIssues.Should().BeTrue();
        fundEvent.LedgerImpacts.Should().HaveCount(2);
        fundEvent.CapitalAccountImpacts.Should().HaveCount(2);
        fundEvent.CapitalAccountImpacts.Should().Contain(impact =>
            impact.CapitalAccountId == "capital-account:fund-alpha:lp-1" &&
            impact.InvestorId == "investor:lp-1" &&
            impact.NetCapitalAccountImpact == 100m);
        fundEvent.CapitalAccountImpacts.Should().Contain(impact =>
            impact.CapitalAccountId == "capital-account:fund-alpha:lp-2" &&
            impact.InvestorId == "investor:lp-2" &&
            impact.NetCapitalAccountImpact == 200m);
        fundEvent.Issues.Should().Contain(issue =>
            issue.Code == "private-capital.capital-account-conflict" &&
            issue.Severity == PrivateCapitalFundEventIssueSeverity.Critical);
        fundEvent.Issues.Should().Contain(issue =>
            issue.Code == "private-capital.investor-conflict" &&
            issue.Severity == PrivateCapitalFundEventIssueSeverity.Critical);
        fundEvent.Issues.Should().Contain(issue =>
            issue.Code == "private-capital.idempotency-key-conflict" &&
            issue.Severity == PrivateCapitalFundEventIssueSeverity.Critical);
        fundEvent.Issues.Should().Contain(issue =>
            issue.Code == "private-capital.payment-intent-conflict" &&
            issue.Severity == PrivateCapitalFundEventIssueSeverity.Critical);
        fundEvent.Issues.Should().Contain(issue =>
            issue.Code == "private-capital.settlement-reference-conflict" &&
            issue.Severity == PrivateCapitalFundEventIssueSeverity.Critical);
    }

    [Fact]
    public void ProjectLedgerBook_CanBuildConsolidatedAccountSummaries()
    {
        var projectLedgers = new ProjectLedgerBook("project-alpha");
        var actual = projectLedgers.GetOrCreate(new LedgerBookKey("project-alpha", "core", LedgerViewKind.Actual));
        var historical = projectLedgers.GetOrCreate(new LedgerBookKey("project-alpha", "core", LedgerViewKind.Historical));
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue", LedgerAccountType.Revenue);

        actual.PostLines(DateTimeOffset.UtcNow, "actual", new[] { (cash, 100m, 0m), (revenue, 0m, 100m) });
        historical.PostLines(DateTimeOffset.UtcNow, "historical", new[] { (cash, 30m, 0m), (revenue, 0m, 30m) });

        var summaries = projectLedgers.ConsolidatedAccountSummaries();
        var cashSummary = summaries.Single(s => s.Account == cash);
        var revenueSummary = summaries.Single(s => s.Account == revenue);

        cashSummary.Balance.Should().Be(130m);
        cashSummary.TotalDebits.Should().Be(130m);
        cashSummary.TotalCredits.Should().Be(0m);
        cashSummary.EntryCount.Should().Be(2);

        revenueSummary.Balance.Should().Be(130m);
        revenueSummary.TotalDebits.Should().Be(0m);
        revenueSummary.TotalCredits.Should().Be(130m);
        revenueSummary.EntryCount.Should().Be(2);
    }

    [Fact]
    public void GetJournalEntries_WithAccountTypeFilter_ReturnsOnlyEntriesTouchingThatType()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var timestamp = DateTimeOffset.UtcNow;
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue", LedgerAccountType.Revenue);
        var expense = new LedgerAccount("Commission", LedgerAccountType.Expense);

        // Entry 1: Asset + Revenue
        ledger.PostLines(timestamp, "sale", new[]
        {
            (cash, 100m, 0m),
            (revenue, 0m, 100m),
        });

        // Entry 2: Expense + Asset
        ledger.PostLines(timestamp, "commission", new[]
        {
            (expense, 5m, 0m),
            (cash, 0m, 5m),
        });

        // Filter to Revenue-touching entries only
        var revenueEntries = ledger.GetJournalEntries(new LedgerQuery(AccountType: LedgerAccountType.Revenue));
        revenueEntries.Should().HaveCount(1);
        revenueEntries[0].Description.Should().Be("sale");

        // Filter to Expense-touching entries only
        var expenseEntries = ledger.GetJournalEntries(new LedgerQuery(AccountType: LedgerAccountType.Expense));
        expenseEntries.Should().HaveCount(1);
        expenseEntries[0].Description.Should().Be("commission");

        // No filter — both returned
        var all = ledger.GetJournalEntries(new LedgerQuery());
        all.Should().HaveCount(2);
    }

    [Fact]
    public void LedgerAccounts_DividendReceivable_IsNormalizedAndScopedPerSymbol()
    {
        var aapl = LedgerAccounts.DividendReceivable("aapl");
        var msft = LedgerAccounts.DividendReceivable("MSFT");
        var aaplScoped = LedgerAccounts.DividendReceivable("aapl", "broker-1");

        aapl.Symbol.Should().Be("AAPL");
        aapl.AccountType.Should().Be(LedgerAccountType.Asset);
        msft.Symbol.Should().Be("MSFT");
        aapl.Should().NotBe(msft);
        aaplScoped.FinancialAccountId.Should().Be("broker-1");
        aapl.Should().NotBe(aaplScoped);
    }

    [Fact]
    public void LedgerAccounts_AccruedInterestReceivable_IsAssetAccount()
    {
        var account = LedgerAccounts.AccruedInterestReceivable("USTBILL");
        account.AccountType.Should().Be(LedgerAccountType.Asset);
        account.Symbol.Should().Be("USTBILL");
    }

    [Fact]
    public void LedgerAccounts_CorpActionDistribution_IsRevenueAccount()
    {
        var account = LedgerAccounts.CorpActionDistribution("AAPL");
        account.AccountType.Should().Be(LedgerAccountType.Revenue);
        account.Symbol.Should().Be("AAPL");
    }

    [Fact]
    public void GetJournalEntries_CanFilterByProjectSecurityAndLedgerViewMetadata()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var timestamp = DateTimeOffset.UtcNow;
        var securityId = Guid.NewGuid();
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue", LedgerAccountType.Revenue);

        ledger.PostLines(
            timestamp,
            "security-master accrual",
            new[]
            {
                (cash, 25m, 0m),
                (revenue, 0m, 25m),
            },
            new JournalEntryMetadata(
                ActivityType: "Accrual",
                Symbol: "USTBILL",
                SecurityId: securityId,
                ProjectId: "project-alpha",
                LedgerBook: "cashflows",
                LedgerView: LedgerViewKind.SecurityMaster,
                ScenarioId: "baseline"));

        ledger.PostLines(
            timestamp,
            "actual cash",
            new[]
            {
                (cash, 10m, 0m),
                (revenue, 0m, 10m),
            },
            new JournalEntryMetadata(
                ActivityType: "Cash",
                Symbol: "USTBILL",
                SecurityId: securityId,
                ProjectId: "project-alpha",
                LedgerBook: "cashflows",
                LedgerView: LedgerViewKind.Actual));

        var filtered = ledger.GetJournalEntries(new LedgerQuery(
            ProjectId: "project-alpha",
            LedgerBook: "cashflows",
            LedgerView: LedgerViewKind.SecurityMaster,
            SecurityId: securityId,
            ScenarioId: "baseline"));

        filtered.Should().HaveCount(1);
        filtered[0].Metadata.LedgerView.Should().Be(LedgerViewKind.SecurityMaster);
        filtered[0].Metadata.SecurityId.Should().Be(securityId);
    }

    [Fact]
    public void LedgerAccounts_UnrealizedGain_IsRevenueAccount()
    {
        LedgerAccounts.UnrealizedGain.AccountType.Should().Be(LedgerAccountType.Revenue);
        LedgerAccounts.UnrealizedGain.Name.Should().Be("Unrealized Gain");
    }

    [Fact]
    public void LedgerAccounts_UnrealizedLoss_IsExpenseAccount()
    {
        LedgerAccounts.UnrealizedLoss.AccountType.Should().Be(LedgerAccountType.Expense);
        LedgerAccounts.UnrealizedLoss.Name.Should().Be("Unrealized Loss");
    }

    [Fact]
    public void LedgerAccounts_RetainedEarnings_IsEquityAccount()
    {
        LedgerAccounts.RetainedEarnings.AccountType.Should().Be(LedgerAccountType.Equity);
        LedgerAccounts.RetainedEarnings.Name.Should().Be("Retained Earnings");
    }

    [Fact]
    public void LedgerAccounts_ScopedVariants_IncludeFinancialAccountId()
    {
        var unrealizedGain = LedgerAccounts.UnrealizedGainFor("broker-1");
        var unrealizedLoss = LedgerAccounts.UnrealizedLossFor("broker-1");
        var retained = LedgerAccounts.RetainedEarningsFor("broker-1");

        unrealizedGain.FinancialAccountId.Should().Be("broker-1");
        unrealizedLoss.FinancialAccountId.Should().Be("broker-1");
        retained.FinancialAccountId.Should().Be("broker-1");

        unrealizedGain.Should().NotBe(LedgerAccounts.UnrealizedGain);
        retained.Should().NotBe(LedgerAccounts.RetainedEarnings);
    }

    [Fact]
    public void Ledger_JournalEntryCount_ReflectsPostedEntries()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue", LedgerAccountType.Revenue);
        var ts = DateTimeOffset.UtcNow;

        ledger.JournalEntryCount.Should().Be(0);
        ledger.TotalLedgerEntryCount.Should().Be(0);

        ledger.PostLines(ts, "sale-1", new[] { (cash, 100m, 0m), (revenue, 0m, 100m) });

        ledger.JournalEntryCount.Should().Be(1);
        ledger.TotalLedgerEntryCount.Should().Be(2);

        ledger.PostLines(ts, "sale-2", new[] { (cash, 50m, 0m), (revenue, 0m, 50m) });

        ledger.JournalEntryCount.Should().Be(2);
        ledger.TotalLedgerEntryCount.Should().Be(4);
    }

    [Fact]
    public void Ledger_GetRunningBalance_ReturnsChronologicalCheckpoints()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue", LedgerAccountType.Revenue);
        var t1 = new DateTimeOffset(2025, 1, 1, 1, 0, 0, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2025, 1, 1, 2, 0, 0, TimeSpan.Zero);

        ledger.PostLines(t1, "sale", new[] { (cash, 100m, 0m), (revenue, 0m, 100m) });
        ledger.PostLines(t2, "commission", new[] { (revenue, 10m, 0m), (cash, 0m, 10m) });

        var running = ledger.GetRunningBalance(cash);

        running.Should().HaveCount(2);
        running[0].Balance.Should().Be(100m);
        running[0].Debit.Should().Be(100m);
        running[0].Credit.Should().Be(0m);
        running[1].Balance.Should().Be(90m);
        running[1].Debit.Should().Be(0m);
        running[1].Credit.Should().Be(10m);
    }

    [Fact]
    public void Ledger_GetRunningBalance_WithTimeRange_StartsFromOpeningBalance()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue", LedgerAccountType.Revenue);
        var t1 = new DateTimeOffset(2025, 1, 1, 1, 0, 0, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2025, 1, 1, 2, 0, 0, TimeSpan.Zero);

        ledger.PostLines(t1, "first-sale", new[] { (cash, 100m, 0m), (revenue, 0m, 100m) });
        ledger.PostLines(t2, "second-sale", new[] { (cash, 50m, 0m), (revenue, 0m, 50m) });

        // Range starts at t2; opening balance from t1 must be carried forward
        var running = ledger.GetRunningBalance(cash, from: t2, to: t2);

        running.Should().HaveCount(1);
        running[0].Balance.Should().Be(150m);  // 100 carried + 50
    }

    [Fact]
    public void Ledger_SnapshotAsOf_ReturnsPointInTimeState()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue", LedgerAccountType.Revenue);
        var t1 = new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var t2 = t1.AddHours(1);

        ledger.PostLines(t1, "sale", new[] { (cash, 200m, 0m), (revenue, 0m, 200m) });
        ledger.PostLines(t2, "refund", new[] { (cash, 0m, 50m), (revenue, 50m, 0m) });

        var snapAtT1 = ledger.SnapshotAsOf(t1);
        var snapAtT2 = ledger.SnapshotAsOf(t2);

        snapAtT1.Balances[cash].Should().Be(200m);
        snapAtT1.JournalEntryCount.Should().Be(1);
        snapAtT1.LedgerEntryCount.Should().Be(2);

        snapAtT2.Balances[cash].Should().Be(150m);
        snapAtT2.JournalEntryCount.Should().Be(2);
        snapAtT2.LedgerEntryCount.Should().Be(4);
    }

    [Fact]
    public void LedgerEntry_BothZero_ThrowsWithDistinctMessage()
    {
        var journalId = Guid.NewGuid();
        var ts = DateTimeOffset.UtcNow;
        var account = new LedgerAccount("Cash", LedgerAccountType.Asset);

        var act = () => new LedgerEntry(Guid.NewGuid(), journalId, ts, account, 0m, 0m, "test");

        act.Should()
            .Throw<LedgerValidationException>()
            .WithMessage("*both Debit and Credit are zero*");
    }

    [Fact]
    public void LedgerEntry_BothNonZero_ThrowsWithDistinctMessage()
    {
        var journalId = Guid.NewGuid();
        var ts = DateTimeOffset.UtcNow;
        var account = new LedgerAccount("Cash", LedgerAccountType.Asset);

        var act = () => new LedgerEntry(Guid.NewGuid(), journalId, ts, account, 10m, 5m, "test");

        act.Should()
            .Throw<LedgerValidationException>()
            .WithMessage("*Exactly one*");
    }

    [Fact]
    public void JournalEntry_IsBalanced_TrueForBalancedEntry()
    {
        var journalId = Guid.NewGuid();
        var ts = DateTimeOffset.UtcNow;
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue", LedgerAccountType.Revenue);

        var entry = new JournalEntry(
            journalId,
            ts,
            "sale",
            new[]
            {
                new LedgerEntry(Guid.NewGuid(), journalId, ts, cash, 100m, 0m, "sale"),
                new LedgerEntry(Guid.NewGuid(), journalId, ts, revenue, 0m, 100m, "sale"),
            });

        entry.IsBalanced.Should().BeTrue();
    }

    [Property(MaxTest = 200)]
    public void Scenario_FundAccountingClose_GeneratedBalancedJournalIsStableUnderLineReordering(
        int lineCountSeed,
        int amountSeed,
        int accountSeed)
    {
        var timestamp = DateTimeOffset.UnixEpoch.AddMinutes(BoundedLong(accountSeed, 0, 525_600));
        var forwardLedger = new Meridian.Ledger.Ledger();
        var reversedLedger = new Meridian.Ledger.Ledger();
        var forwardEntry = BuildGeneratedBalancedJournal(lineCountSeed, amountSeed, accountSeed, timestamp, reverse: false);
        var reversedEntry = BuildGeneratedBalancedJournal(lineCountSeed, amountSeed, accountSeed, timestamp, reverse: true);

        forwardLedger.Post(forwardEntry);
        reversedLedger.Post(reversedEntry);

        forwardEntry.IsBalanced.Should().BeTrue();
        reversedEntry.IsBalanced.Should().BeTrue();
        forwardLedger.TrialBalance().Should().BeEquivalentTo(reversedLedger.TrialBalance());
        forwardLedger.SummarizeAccounts().Should().BeEquivalentTo(reversedLedger.SummarizeAccounts());
        forwardLedger.TotalLedgerEntryCount.Should().Be(reversedLedger.TotalLedgerEntryCount);
    }

    [Property(MaxTest = 200)]
    public void Scenario_FundAccountingClose_GeneratedDuplicateLedgerLineIdsAlwaysReject(
        int lineCountSeed,
        int amountSeed,
        int accountSeed)
    {
        var timestamp = DateTimeOffset.UnixEpoch.AddMinutes(BoundedLong(accountSeed, 0, 525_600));
        var journalId = DeterministicGuid("duplicate-journal", lineCountSeed, amountSeed, accountSeed);
        var duplicateEntryId = DeterministicGuid("duplicate-line", lineCountSeed, amountSeed, accountSeed);
        var lines = BuildGeneratedBalancedLines(journalId, timestamp, lineCountSeed, amountSeed, accountSeed);
        var duplicate = new LedgerEntry(
            duplicateEntryId,
            journalId,
            timestamp,
            lines[0].Account,
            lines[0].Debit,
            lines[0].Credit,
            lines[0].Description);
        var duplicatedLines = new[] { duplicate, duplicate }
            .Concat(lines.Skip(1))
            .ToArray();

        var act = () => new JournalEntry(journalId, timestamp, duplicate.Description, duplicatedLines);

        act.Should()
            .Throw<LedgerValidationException>()
            .WithMessage("*duplicated within the journal entry*");
    }

    [Fact]
    public void FundLedgerBook_EntitySleeveVehicle_GetIndependentLedgers()
    {
        var fund = new FundLedgerBook("fund-xyz");
        var ts = DateTimeOffset.UtcNow;
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue", LedgerAccountType.Revenue);

        fund.EntityLedger("entity-1").PostLines(ts, "entity-1-sale", new[] { (cash, 100m, 0m), (revenue, 0m, 100m) });
        fund.SleeveLedger("sleeve-a").PostLines(ts, "sleeve-a-sale", new[] { (cash, 40m, 0m), (revenue, 0m, 40m) });
        fund.VehicleLedger("vehicle-x").PostLines(ts, "vehicle-x-sale", new[] { (cash, 20m, 0m), (revenue, 0m, 20m) });

        fund.EntityLedger("entity-1").GetBalance(cash).Should().Be(100m);
        fund.SleeveLedger("sleeve-a").GetBalance(cash).Should().Be(40m);
        fund.VehicleLedger("vehicle-x").GetBalance(cash).Should().Be(20m);
    }

    [Fact]
    public void FundLedgerBook_ConsolidatedTrialBalance_AggregatesAllSubLedgers()
    {
        var fund = new FundLedgerBook("fund-xyz");
        var ts = DateTimeOffset.UtcNow;
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue", LedgerAccountType.Revenue);

        fund.FundLedger.PostLines(ts, "fund-level", new[] { (cash, 50m, 0m), (revenue, 0m, 50m) });
        fund.EntityLedger("e1").PostLines(ts, "entity-1", new[] { (cash, 30m, 0m), (revenue, 0m, 30m) });

        var consolidated = fund.ConsolidatedTrialBalance();

        consolidated[cash].Should().Be(80m);
        consolidated[revenue].Should().Be(80m);
    }

    [Fact]
    public void FundLedgerBook_ReconciliationSnapshot_ShouldFilterByLineDimensionsAcrossSubLedgers()
    {
        var fund = new FundLedgerBook("fund-alpha");
        var cash = new LedgerAccount("Assets:Cash", LedgerAccountType.Asset);
        var equity = new LedgerAccount("Equity:Capital", LedgerAccountType.Equity);
        var asOf = DateTimeOffset.Parse("2026-05-31T23:59:59Z");
        var entityAlpha = new LedgerLineDimensionSet(FundId: "fund-alpha", EntityId: "entity-alpha");
        var entityBeta = new LedgerLineDimensionSet(FundId: "fund-alpha", EntityId: "entity-beta");

        fund.FundLedger.PostLines(
            asOf.AddHours(-2),
            "fund alpha capital",
            new[]
            {
                (cash, 100m, 0m, (LedgerLineDimensionSet?)entityAlpha),
                (equity, 0m, 100m, (LedgerLineDimensionSet?)entityAlpha),
            });
        fund.EntityLedger("entity-alpha").PostLines(
            asOf.AddHours(-1),
            "entity alpha capital",
            new[]
            {
                (cash, 25m, 0m, (LedgerLineDimensionSet?)entityAlpha),
                (equity, 0m, 25m, (LedgerLineDimensionSet?)entityAlpha),
            });
        fund.EntityLedger("entity-beta").PostLines(
            asOf,
            "entity beta capital",
            new[]
            {
                (cash, 50m, 0m, (LedgerLineDimensionSet?)entityBeta),
                (equity, 0m, 50m, (LedgerLineDimensionSet?)entityBeta),
            });

        var snapshot = fund.ReconciliationSnapshot(
            asOf,
            new LedgerLineDimensionSet(FundId: "fund-alpha", EntityId: "entity-alpha"));

        snapshot.Consolidated.Balances[cash].Should().Be(125m);
        snapshot.Consolidated.JournalEntryCount.Should().Be(2);
        snapshot.Entities["entity-alpha"].Balances[cash].Should().Be(25m);
        snapshot.Entities["entity-alpha"].JournalEntryCount.Should().Be(1);
        snapshot.Entities["entity-beta"].JournalEntryCount.Should().Be(0);
        snapshot.Entities["entity-beta"].Balances.Should().BeEmpty();
    }

    [Fact]
    public void FundLedgerBook_EntitySnapshotsAsOf_KeyedByEntityId()
    {
        var fund = new FundLedgerBook("fund-abc");
        var ts = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue", LedgerAccountType.Revenue);

        fund.EntityLedger("alpha").PostLines(ts, "sale", new[] { (cash, 75m, 0m), (revenue, 0m, 75m) });
        fund.EntityLedger("beta").PostLines(ts, "sale", new[] { (cash, 25m, 0m), (revenue, 0m, 25m) });

        var snapshots = fund.EntitySnapshotsAsOf(ts);

        snapshots.Should().ContainKey("alpha");
        snapshots.Should().ContainKey("beta");
        snapshots["alpha"].Balances[cash].Should().Be(75m);
        snapshots["beta"].Balances[cash].Should().Be(25m);
    }

    [Fact]
    public void FundLedgerBook_ReconciliationSnapshot_ContainsConsolidatedAndDimensionBreakdowns()
    {
        var fund = new FundLedgerBook("fund-recon");
        var ts = new DateTimeOffset(2025, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue", LedgerAccountType.Revenue);

        fund.EntityLedger("e1").PostLines(ts, "sale", new[] { (cash, 60m, 0m), (revenue, 0m, 60m) });
        fund.SleeveLedger("s1").PostLines(ts, "sale", new[] { (cash, 40m, 0m), (revenue, 0m, 40m) });
        fund.VehicleLedger("v1").PostLines(ts, "sale", new[] { (cash, 25m, 0m), (revenue, 0m, 25m) });

        var snap = fund.ReconciliationSnapshot(ts);

        snap.FundId.Should().Be("fund-recon");
        snap.AsOf.Should().Be(ts);
        snap.Consolidated.Balances[cash].Should().Be(125m);
        snap.Consolidated.JournalEntryCount.Should().Be(3);
        snap.Consolidated.LedgerEntryCount.Should().Be(6);
        snap.Entities.Should().ContainKey("e1");
        snap.Entities["e1"].Balances[cash].Should().Be(60m);
        snap.Entities["e1"].JournalEntryCount.Should().Be(1);
        snap.Sleeves.Should().ContainKey("s1");
        snap.Sleeves["s1"].LedgerEntryCount.Should().Be(2);
        snap.Vehicles.Should().ContainKey("v1");
        snap.Vehicles["v1"].Balances[cash].Should().Be(25m);
    }

    [Fact]
    public void ChartOfAccounts_Register_CreatesHierarchyForCustomAccountPaths()
    {
        var chart = new ChartOfAccounts();

        var brokerageCash = chart.Register(" Assets : Cash : Brokerage ", LedgerAccountType.Asset, financialAccountId: "broker-1");
        var collateralCash = chart.Register("Assets:Cash:Collateral", LedgerAccountType.Asset);

        brokerageCash.Name.Should().Be("Assets:Cash:Brokerage");
        brokerageCash.FinancialAccountId.Should().Be("broker-1");
        collateralCash.Name.Should().Be("Assets:Cash:Collateral");
        chart.Find("Assets").Should().NotBeNull();
        chart.Find("Assets:Cash").Should().NotBeNull();
        chart.GetChildren("Assets").Should().ContainSingle(node => node.Path == "Assets:Cash");
        chart.GetDescendants("Assets").Select(node => node.Path).Should().Contain(new[]
        {
            "Assets:Cash",
            "Assets:Cash:Brokerage",
            "Assets:Cash:Collateral",
        });
    }

    [Fact]
    public void ChartOfAccounts_Register_RejectsConflictingParentAccountTypes()
    {
        var chart = new ChartOfAccounts();
        chart.Register("Assets:Cash", LedgerAccountType.Asset);

        var act = () => chart.Register("Assets:Fees", LedgerAccountType.Expense);

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*parent 'Assets' is registered as Asset, not Expense*");
    }

    [Fact]
    public void ChartOfAccounts_Register_RejectsDuplicatePathWithDifferentScope()
    {
        var chart = new ChartOfAccounts();
        chart.Register("Assets:Cash:Brokerage", LedgerAccountType.Asset, financialAccountId: "broker-1");

        var act = () => chart.Register("Assets:Cash:Brokerage", LedgerAccountType.Asset, financialAccountId: "broker-2");

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*already registered with a different account scope*");
    }

    [Fact]
    public void ChartOfAccounts_AggregateBalances_RollsTrialBalanceUpHierarchy()
    {
        var chart = new ChartOfAccounts();
        var brokerageCash = chart.Register("Assets:Cash:Brokerage", LedgerAccountType.Asset);
        var collateralCash = chart.Register("Assets:Cash:Collateral", LedgerAccountType.Asset);
        var investorCapital = chart.Register("Equity:Partners:Capital", LedgerAccountType.Equity);
        var ledger = new Meridian.Ledger.Ledger();
        var ts = new DateTimeOffset(2026, 5, 28, 0, 0, 0, TimeSpan.Zero);

        ledger.PostLines(ts, "capital contribution", new[]
        {
            (brokerageCash, 100m, 0m),
            (collateralCash, 25m, 0m),
            (investorCapital, 0m, 125m),
        });

        var balances = chart.AggregateBalances(ledger.TrialBalance());
        var assets = balances.Single(row => row.Path == "Assets");
        var cash = balances.Single(row => row.Path == "Assets:Cash");
        var brokerage = balances.Single(row => row.Path == "Assets:Cash:Brokerage");
        var equity = balances.Single(row => row.Path == "Equity");

        assets.DirectBalance.Should().Be(0m);
        assets.AggregateBalance.Should().Be(125m);
        cash.AggregateBalance.Should().Be(125m);
        brokerage.DirectBalance.Should().Be(100m);
        brokerage.AggregateBalance.Should().Be(100m);
        equity.AggregateBalance.Should().Be(125m);
    }

    [Fact]
    public void LedgerFinancialStatementBuilder_BuildsIncomeStatementAndBalanceSheetFromChart()
    {
        var chart = new ChartOfAccounts();
        var cash = chart.Register("Assets:Cash:Brokerage", LedgerAccountType.Asset);
        var investorCapital = chart.Register("Equity:Partners:Capital", LedgerAccountType.Equity);
        var feeRevenue = chart.Register("Revenue:Management Fees", LedgerAccountType.Revenue);
        var commissionExpense = chart.Register("Expenses:Commissions", LedgerAccountType.Expense);
        var ledger = new Meridian.Ledger.Ledger();
        var ts = new DateTimeOffset(2026, 5, 28, 0, 0, 0, TimeSpan.Zero);

        ledger.PostLines(ts, "capital contribution", new[]
        {
            (cash, 1_000m, 0m),
            (investorCapital, 0m, 1_000m),
        });
        ledger.PostLines(ts.AddHours(1), "management fee accrual", new[]
        {
            (cash, 100m, 0m),
            (feeRevenue, 0m, 100m),
        });
        ledger.PostLines(ts.AddHours(2), "commission accrual", new[]
        {
            (commissionExpense, 25m, 0m),
            (cash, 0m, 25m),
        });

        var statements = LedgerFinancialStatementBuilder.Build(ledger, chart);

        statements.TotalAssets.Should().Be(1_075m);
        statements.TotalLiabilities.Should().Be(0m);
        statements.TotalEquity.Should().Be(1_000m);
        statements.TotalRevenue.Should().Be(100m);
        statements.TotalExpenses.Should().Be(25m);
        statements.NetIncome.Should().Be(75m);
        statements.EndingEquity.Should().Be(1_075m);
        statements.AccountingEquationVariance.Should().Be(0m);
        statements.IncomeStatementRows.Should().Contain(row => row.Path == "Revenue" && row.AggregateBalance == 100m);
        statements.IncomeStatementRows.Should().Contain(row => row.Path == "Expenses" && row.AggregateBalance == 25m);
        statements.BalanceSheetRows.Should().Contain(row => row.Path == "Assets" && row.AggregateBalance == 1_075m);
    }

    [Fact]
    public void LedgerFinancialStatementBuilder_BuildAsOf_ExcludesLaterPostings()
    {
        var cash = new LedgerAccount("Assets:Cash", LedgerAccountType.Asset);
        var investorCapital = new LedgerAccount("Equity:Capital", LedgerAccountType.Equity);
        var revenue = new LedgerAccount("Revenue:Fees", LedgerAccountType.Revenue);
        var ledger = new Meridian.Ledger.Ledger();
        var t1 = new DateTimeOffset(2026, 5, 28, 9, 0, 0, TimeSpan.Zero);
        var t2 = t1.AddHours(1);

        ledger.PostLines(t1, "capital contribution", new[]
        {
            (cash, 500m, 0m),
            (investorCapital, 0m, 500m),
        });
        ledger.PostLines(t2, "fee accrual", new[]
        {
            (cash, 40m, 0m),
            (revenue, 0m, 40m),
        });

        var beforeFee = LedgerFinancialStatementBuilder.BuildAsOf(ledger, t1);
        var afterFee = LedgerFinancialStatementBuilder.BuildAsOf(ledger, t2);

        beforeFee.AsOf.Should().Be(t1);
        beforeFee.TotalAssets.Should().Be(500m);
        beforeFee.NetIncome.Should().Be(0m);
        beforeFee.AccountingEquationVariance.Should().Be(0m);

        afterFee.TotalAssets.Should().Be(540m);
        afterFee.NetIncome.Should().Be(40m);
        afterFee.AccountingEquationVariance.Should().Be(0m);
    }

    [Fact]
    public void LedgerQuery_CanFilterJournalEntriesByLineDimensions()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var cash = new LedgerAccount("Assets:Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue:Fees", LedgerAccountType.Revenue);
        var alphaDimensions = new LedgerLineDimensionSet(
            FundId: "fund-alpha",
            EntityId: "entity-alpha",
            StrategyId: "strategy-income",
            CostCenterId: "cost-center-ops",
            CounterpartyId: "counterparty-custodian",
            ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Department"] = "Investments"
            });
        var betaDimensions = alphaDimensions with
        {
            EntityId = "entity-beta",
            CostCenterId = "cost-center-admin",
            ExternalGlDimensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Department"] = "Administration"
            }
        };

        ledger.PostLines(
            DateTimeOffset.Parse("2026-05-28T09:00:00Z"),
            "alpha fee accrual",
            new[]
            {
                (cash, 100m, 0m, (LedgerLineDimensionSet?)alphaDimensions),
                (revenue, 0m, 100m, (LedgerLineDimensionSet?)alphaDimensions),
            });
        ledger.PostLines(
            DateTimeOffset.Parse("2026-05-28T10:00:00Z"),
            "beta fee accrual",
            new[]
            {
                (cash, 50m, 0m, (LedgerLineDimensionSet?)betaDimensions),
                (revenue, 0m, 50m, (LedgerLineDimensionSet?)betaDimensions),
            });

        var filtered = ledger.GetJournalEntries(new LedgerQuery(
            LineDimensions: new LedgerLineDimensionSet(
                FundId: "fund-alpha",
                EntityId: "ENTITY-ALPHA",
                ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["department"] = "investments"
                })));

        filtered.Should().ContainSingle();
        filtered[0].Description.Should().Be("alpha fee accrual");
    }

    [Fact]
    public void Ledger_TrialBalance_ShouldFilterByLineDimensions()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var cash = new LedgerAccount("Assets:Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue:Fees", LedgerAccountType.Revenue);
        var alphaDimensions = new LedgerLineDimensionSet(
            FundId: "fund-alpha",
            EntityId: "entity-alpha",
            InstrumentId: Guid.Parse("4D2AE42D-38DC-4B62-A8FD-7306C8627764"),
            CounterpartyId: "counterparty-custodian");
        var betaDimensions = alphaDimensions with { EntityId = "entity-beta" };

        ledger.PostLines(
            DateTimeOffset.Parse("2026-05-28T09:00:00Z"),
            "alpha fee accrual",
            new[]
            {
                (cash, 100m, 0m, (LedgerLineDimensionSet?)alphaDimensions),
                (revenue, 0m, 100m, (LedgerLineDimensionSet?)alphaDimensions),
            });
        ledger.PostLines(
            DateTimeOffset.Parse("2026-05-28T10:00:00Z"),
            "beta fee accrual",
            new[]
            {
                (cash, 50m, 0m, (LedgerLineDimensionSet?)betaDimensions),
                (revenue, 0m, 50m, (LedgerLineDimensionSet?)betaDimensions),
            });

        var trialBalance = ledger.TrialBalance(lineDimensions: new LedgerLineDimensionSet(
            FundId: "fund-alpha",
            EntityId: "entity-alpha",
            InstrumentId: alphaDimensions.InstrumentId));

        trialBalance[cash].Should().Be(100m);
        trialBalance[revenue].Should().Be(100m);
    }

    [Fact]
    public void LedgerFinancialStatementBuilder_BuildAsOf_ShouldFilterByLineDimensions()
    {
        var cash = new LedgerAccount("Assets:Cash", LedgerAccountType.Asset);
        var capital = new LedgerAccount("Equity:Capital", LedgerAccountType.Equity);
        var revenue = new LedgerAccount("Revenue:Fees", LedgerAccountType.Revenue);
        var ledger = new Meridian.Ledger.Ledger();
        var t1 = DateTimeOffset.Parse("2026-05-28T09:00:00Z");
        var t2 = t1.AddHours(1);
        var alphaDimensions = new LedgerLineDimensionSet(FundId: "fund-alpha", EntityId: "entity-alpha");
        var betaDimensions = new LedgerLineDimensionSet(FundId: "fund-alpha", EntityId: "entity-beta");

        ledger.PostLines(
            t1,
            "alpha capital contribution",
            new[]
            {
                (cash, 500m, 0m, (LedgerLineDimensionSet?)alphaDimensions),
                (capital, 0m, 500m, (LedgerLineDimensionSet?)alphaDimensions),
            });
        ledger.PostLines(
            t1.AddMinutes(30),
            "alpha fee accrual",
            new[]
            {
                (cash, 40m, 0m, (LedgerLineDimensionSet?)alphaDimensions),
                (revenue, 0m, 40m, (LedgerLineDimensionSet?)alphaDimensions),
            });
        ledger.PostLines(
            t2,
            "beta fee accrual",
            new[]
            {
                (cash, 25m, 0m, (LedgerLineDimensionSet?)betaDimensions),
                (revenue, 0m, 25m, (LedgerLineDimensionSet?)betaDimensions),
            });

        var statements = LedgerFinancialStatementBuilder.BuildAsOf(
            ledger,
            t2,
            lineDimensions: new LedgerLineDimensionSet(FundId: "fund-alpha", EntityId: "entity-alpha"));

        statements.TotalAssets.Should().Be(540m);
        statements.TotalEquity.Should().Be(500m);
        statements.TotalRevenue.Should().Be(40m);
        statements.NetIncome.Should().Be(40m);
        statements.AccountingEquationVariance.Should().Be(0m);
    }

    [Fact]
    public void LedgerReportPackBuilder_BuildsSignedArtifactsForLockedPeriodStatements()
    {
        var chart = new ChartOfAccounts();
        var cash = chart.Register("Assets:Cash:Brokerage", LedgerAccountType.Asset);
        var investorCapital = chart.Register("Equity:Partners:Capital", LedgerAccountType.Equity);
        var feeRevenue = chart.Register("Revenue:Management Fees", LedgerAccountType.Revenue);
        var commissionExpense = chart.Register("Expenses:Commissions", LedgerAccountType.Expense);
        var aaplSecurity = chart.Register("Assets:Investments:AAPL", LedgerAccountType.Asset, "AAPL", "broker-1");
        var ledger = new Meridian.Ledger.Ledger();
        var periodStart = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        var periodEnd = new DateTimeOffset(2026, 5, 31, 23, 59, 59, TimeSpan.Zero);
        var asOf = periodEnd;
        var ledgerKey = new LedgerBookKey("fund-alpha", "official", LedgerViewKind.Actual);
        var lockedPeriod = new LockedAccountingPeriod(
            ledgerKey,
            "2026-05",
            periodStart,
            periodEnd,
            new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero),
            "controller",
            "published NAV");

        ledger.PostLines(periodStart.AddDays(1), "capital contribution", new[]
        {
            (cash, 1_000m, 0m),
            (investorCapital, 0m, 1_000m),
        });
        ledger.PostLines(periodStart.AddDays(10), "management fee revenue", new[]
        {
            (cash, 125m, 0m),
            (feeRevenue, 0m, 125m),
        });
        ledger.PostLines(periodStart.AddDays(11), "commission accrual", new[]
        {
            (commissionExpense, 25m, 0m),
            (cash, 0m, 25m),
        });

        var request = new LedgerReportPackRequest(
            "lrp-2026-05",
            "fund-alpha",
            "2026-05",
            periodStart,
            periodEnd,
            asOf,
            "usd",
            "controller",
            new DateTimeOffset(2026, 6, 1, 13, 0, 0, TimeSpan.Zero),
            lockedPeriod,
            sourceRunId: "run-close-2026-05",
            sourceSessionId: "session-close-2026-05",
            reconciliationEvidenceLinks: ["reconciliation:run-2026-05"],
            approvalEvidenceLinks: ["approval:close-2026-05"]);
        var taxLotProjection = LedgerTaxLotReliefProjector.Project(new LedgerTaxLotReliefInput(
            aaplSecurity,
            new DateOnly(2026, 5, 15),
            quantitySold: 5m,
            salePrice: 120m,
            LedgerTaxLotReliefMethod.Hifo,
            [
                new LedgerTaxLot("lot-low", new DateOnly(2026, 1, 10), 4m, 90m),
                new LedgerTaxLot("lot-high", new DateOnly(2026, 2, 10), 5m, 100m),
            ]));

        var pack = LedgerReportPackBuilder.Build(
            ledger,
            request,
            chart,
            taxLotReliefProjections: [taxLotProjection]);

        pack.IsBalanced.Should().BeTrue();
        pack.Status.Should().Be(LedgerReportPackLifecycleStatus.Draft);
        pack.Statements.TotalAssets.Should().Be(1_100m);
        pack.Statements.NetIncome.Should().Be(100m);
        pack.Artifacts.Should().Contain(artifact => artifact.Name == "trial-balance.csv");
        pack.Artifacts.Should().Contain(artifact => artifact.Name == "income-statement.csv");
        pack.Artifacts.Should().Contain(artifact => artifact.Name == "balance-sheet.csv");
        pack.Artifacts.Should().Contain(artifact => artifact.Name == "financial-statements.json");
        pack.Artifacts.Should().Contain(artifact => artifact.Name == "tax-lot-realized-gains.csv");
        pack.Artifacts.Should().Contain(artifact => artifact.Name == "line-provenance.csv");
        pack.Artifacts.Should().Contain(artifact => artifact.Name == "manifest.csv");
        pack.Artifacts.Should().OnlyContain(artifact => artifact.ChecksumSha256.Length == 64);
        pack.LineProvenance.Should().Contain(row =>
            row.ArtifactName == "trial-balance.csv" &&
            row.RowKey == "Assets:Cash:Brokerage" &&
            row.SourceRunId == "run-close-2026-05" &&
            row.SourceSessionId == "session-close-2026-05" &&
            row.LedgerEntryIds.Count > 0 &&
            row.EvidenceLinks.Contains("reconciliation:run-2026-05") &&
            row.EvidenceLinks.Contains("approval:close-2026-05"));

        var manifest = pack.Artifacts.Single(artifact => artifact.Name == "manifest.csv");
        manifest.Content.Should().Contain("ledger-report-pack-manifest-v1");
        manifest.Content.Should().Contain("locked-period,true");
        manifest.Content.Should().Contain("source-run-id,run-close-2026-05");
        manifest.Content.Should().Contain("reconciliation-evidence-count,1");
        manifest.Content.Should().Contain("net-income,100");
        manifest.Content.Should().Contain("accounting-equation-variance,0");
        manifest.Content.Should().Contain("trial-balance.csv,text/csv,");
        manifest.Content.Should().Contain("financial-statements.json,application/json,");
        manifest.Content.Should().Contain("tax-lot-realized-gains.csv,text/csv,");
        manifest.Content.Should().Contain("line-provenance.csv,text/csv,");
        var statementsArtifact = pack.Artifacts.Single(artifact => artifact.Name == "financial-statements.json");
        statementsArtifact.Content.Should().Contain("\"schema\": \"ledger-financial-statements-v1\"");
        statementsArtifact.Content.Should().Contain("\"trialBalanceRows\": [");
        statementsArtifact.Content.Should().Contain("\"incomeStatementRows\": [");
        statementsArtifact.Content.Should().Contain("\"balanceSheetRows\": [");
        statementsArtifact.Content.Should().Contain("\"netIncome\": 100");
        statementsArtifact.Content.Should().Contain("\"accountingEquationVariance\": 0");
        var provenanceArtifact = pack.Artifacts.Single(artifact => artifact.Name == "line-provenance.csv");
        provenanceArtifact.Content.Should().Contain("LedgerJournalEntryIds");
        provenanceArtifact.Content.Should().Contain("reconciliation:run-2026-05");
        var taxArtifact = pack.Artifacts.Single(artifact => artifact.Name == "tax-lot-realized-gains.csv");
        taxArtifact.Content.Should().Contain("SaleDate,AccountName,Symbol,FinancialAccountId,ReliefMethod,LotId");
        taxArtifact.Content.Should().Contain("2026-05-15,Assets:Investments:AAPL,AAPL,broker-1,Hifo,lot-high,2026-02-10,5,100,600,500,100");
        pack.Signature.Algorithm.Should().Be("SHA256");
        pack.Signature.PayloadChecksumSha256.Should().HaveLength(64);
        pack.Signature.SignedBy.Should().Be("controller");
    }

    [Fact]
    public void LedgerReportPackBuilder_Build_ShouldFilterStatementsAndProvenanceByLineDimensions()
    {
        var chart = new ChartOfAccounts();
        var cash = chart.Register("Assets:Cash", LedgerAccountType.Asset);
        var capital = chart.Register("Equity:Capital", LedgerAccountType.Equity);
        var revenue = chart.Register("Revenue:Fees", LedgerAccountType.Revenue);
        var ledger = new Meridian.Ledger.Ledger();
        var periodStart = DateTimeOffset.Parse("2026-05-01T00:00:00Z");
        var periodEnd = DateTimeOffset.Parse("2026-05-31T23:59:59Z");
        var alphaDimensions = new LedgerLineDimensionSet(
            FundId: "fund-alpha",
            EntityId: "entity-alpha",
            ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Department"] = "Investments"
            });
        var betaDimensions = alphaDimensions with
        {
            EntityId = "entity-beta",
            ExternalGlDimensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Department"] = "Administration"
            }
        };

        ledger.PostLines(
            periodStart.AddDays(1),
            "alpha capital",
            new[]
            {
                (cash, 500m, 0m, (LedgerLineDimensionSet?)alphaDimensions),
                (capital, 0m, 500m, (LedgerLineDimensionSet?)alphaDimensions),
            });
        ledger.PostLines(
            periodStart.AddDays(2),
            "alpha fee",
            new[]
            {
                (cash, 40m, 0m, (LedgerLineDimensionSet?)alphaDimensions),
                (revenue, 0m, 40m, (LedgerLineDimensionSet?)alphaDimensions),
            });
        ledger.PostLines(
            periodStart.AddDays(3),
            "beta fee",
            new[]
            {
                (cash, 25m, 0m, (LedgerLineDimensionSet?)betaDimensions),
                (revenue, 0m, 25m, (LedgerLineDimensionSet?)betaDimensions),
            });
        var betaLedgerEntryIds = ledger.Journal
            .Single(entry => entry.Description == "beta fee")
            .Lines
            .Select(static line => line.EntryId)
            .ToArray();

        var request = new LedgerReportPackRequest(
            "lrp-entity-alpha-2026-05",
            "fund-alpha",
            "2026-05",
            periodStart,
            periodEnd,
            periodEnd,
            "usd",
            "controller",
            DateTimeOffset.Parse("2026-06-01T13:00:00Z"),
            sourceRunId: "run-alpha-close",
            reconciliationEvidenceLinks: ["reconciliation:alpha"],
            approvalEvidenceLinks: ["approval:alpha"],
            lineDimensions: new LedgerLineDimensionSet(
                FundId: "fund-alpha",
                EntityId: "ENTITY-ALPHA",
                ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["department"] = "investments"
                }));

        var pack = LedgerReportPackBuilder.Build(ledger, request, chart);

        pack.Statements.TotalAssets.Should().Be(540m);
        pack.Statements.TotalEquity.Should().Be(500m);
        pack.Statements.TotalRevenue.Should().Be(40m);
        pack.Statements.NetIncome.Should().Be(40m);
        pack.Request.LineDimensions.Should().NotBeNull();
        pack.LineProvenance.Should().Contain(row =>
            row.ArtifactName == "trial-balance.csv" &&
            row.RowKey == "Assets:Cash" &&
            row.LedgerEntryIds.Count == 2 &&
            !row.LedgerEntryIds.Intersect(betaLedgerEntryIds).Any());

        var manifest = pack.Artifacts.Single(artifact => artifact.Name == "manifest.csv");
        manifest.Content.Should().Contain("dimension-scope,entityId=ENTITY-ALPHA;externalGl.department=investments;fundId=fund-alpha");
        var statementsArtifact = pack.Artifacts.Single(artifact => artifact.Name == "financial-statements.json");
        statementsArtifact.Content.Should().Contain("\"dimensionScope\": {");
        statementsArtifact.Content.Should().Contain("\"entityId\": \"ENTITY-ALPHA\"");
        statementsArtifact.Content.Should().Contain("\"externalGl.department\": \"investments\"");
        var provenanceArtifact = pack.Artifacts.Single(artifact => artifact.Name == "line-provenance.csv");
        foreach (var betaLedgerEntryId in betaLedgerEntryIds)
            provenanceArtifact.Content.Should().NotContain(betaLedgerEntryId.ToString("D"));
    }

    [Fact]
    public void LedgerReportPackLifecycle_TracksApprovalPublicationRestatementAndArchive()
    {
        var chart = new ChartOfAccounts();
        var cash = chart.Register("Assets:Cash", LedgerAccountType.Asset);
        var capital = chart.Register("Equity:Capital", LedgerAccountType.Equity);
        var ledger = new Meridian.Ledger.Ledger();
        var periodStart = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        var periodEnd = new DateTimeOffset(2026, 5, 31, 23, 59, 59, TimeSpan.Zero);
        ledger.PostLines(periodStart.AddDays(1), "capital contribution", new[]
        {
            (cash, 100m, 0m),
            (capital, 0m, 100m),
        });
        var pack = LedgerReportPackBuilder.Build(
            ledger,
            new LedgerReportPackRequest(
                "lrp-2026-05",
                "fund-alpha",
                "2026-05",
                periodStart,
                periodEnd,
                periodEnd,
                "usd",
                "controller",
                new DateTimeOffset(2026, 6, 1, 13, 0, 0, TimeSpan.Zero),
                sourceRunId: "run-close-2026-05",
                reconciliationEvidenceLinks: ["reconciliation:clean"],
                approvalEvidenceLinks: ["approval:controller"]),
            chart);

        var published = pack
            .Validate("controller", new DateTimeOffset(2026, 6, 1, 14, 0, 0, TimeSpan.Zero), "control totals validated", ["evidence:validation"])
            .Approve("controller", new DateTimeOffset(2026, 6, 1, 15, 0, 0, TimeSpan.Zero), "approved for publication", ["approval:controller"])
            .Publish("controller", new DateTimeOffset(2026, 6, 1, 16, 0, 0, TimeSpan.Zero), "published retained report pack", ["vault:report-pack/lrp-2026-05"]);
        var restated = published.Restate(
            "restatement-1",
            "late custodian correction",
            "controller",
            new DateTimeOffset(2026, 6, 2, 10, 0, 0, TimeSpan.Zero),
            ["trial-balance.csv:Assets:Cash:aggregate-balance"],
            ["statement:custodian-correction", "approval:restatement"]);
        var archived = restated.Archive(
            "records",
            new DateTimeOffset(2026, 6, 30, 17, 0, 0, TimeSpan.Zero),
            "retention window closed",
            ["vault:archive/lrp-2026-05"]);

        published.Status.Should().Be(LedgerReportPackLifecycleStatus.Published);
        published.LifecycleEvents.Should().HaveCount(3);
        published.LifecycleEvents.Select(item => item.ToStatus).Should().Equal(
            LedgerReportPackLifecycleStatus.Validated,
            LedgerReportPackLifecycleStatus.Approved,
            LedgerReportPackLifecycleStatus.Published);
        restated.Status.Should().Be(LedgerReportPackLifecycleStatus.Restated);
        var restatement = restated.Restatements.Should().ContainSingle().Subject;
        restatement.Reason.Should().Be("late custodian correction");
        restatement.ApprovedBy.Should().Be("controller");
        restatement.ChangedLineKeys.Should().Contain("trial-balance.csv:Assets:Cash:aggregate-balance");
        restatement.EvidenceLinks.Should().Contain("statement:custodian-correction");
        archived.Status.Should().Be(LedgerReportPackLifecycleStatus.Archived);
        archived.LifecycleEvents.Last().EvidenceLinks.Should().Contain("vault:archive/lrp-2026-05");
    }

    [Fact]
    public void LedgerReportPackRequest_RejectsAsOfOutsideAccountingPeriod()
    {
        var periodStart = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        var periodEnd = new DateTimeOffset(2026, 5, 31, 23, 59, 59, TimeSpan.Zero);

        var action = () => new LedgerReportPackRequest(
            "lrp-2026-05",
            "fund-alpha",
            "2026-05",
            periodStart,
            periodEnd,
            periodEnd.AddTicks(1),
            "USD",
            "controller",
            DateTimeOffset.UtcNow);

        action.Should()
            .Throw<ArgumentException>()
            .WithMessage("*as-of timestamp must fall inside the accounting period*");
    }

    [Fact]
    public void LedgerReportSchedulePlanner_ProjectsScheduledQuarterlyExports()
    {
        var schedule = new LedgerReportSchedule(
            "investor-pack",
            "fund-alpha",
            "Quarterly investor report pack",
            LedgerReportScheduleFrequency.Quarterly,
            new DateOnly(2026, 1, 1),
            dueDaysAfterPeriodEnd: 10,
            "usd",
            [LedgerReportExportFormat.Csv, LedgerReportExportFormat.Xlsx],
            ["investors@example.com", "regulator@example.com"],
            "controller",
            new DateTimeOffset(2025, 12, 15, 12, 0, 0, TimeSpan.Zero));

        var exports = LedgerReportSchedulePlanner.Project(schedule, occurrenceCount: 2);

        exports.Should().HaveCount(2);
        exports[0].ReportId.Should().Be("investor-pack:2026-Q1:0001");
        exports[0].PeriodId.Should().Be("2026-Q1");
        exports[0].PeriodStart.Should().Be(new DateOnly(2026, 1, 1));
        exports[0].PeriodEnd.Should().Be(new DateOnly(2026, 3, 31));
        exports[0].AsOf.Should().Be(new DateTimeOffset(2026, 3, 31, 23, 59, 59, 999, TimeSpan.Zero).AddTicks(9999));
        exports[0].DueAtUtc.Should().Be(new DateTimeOffset(2026, 4, 10, 0, 0, 0, TimeSpan.Zero));
        exports[0].Formats.Should().BeEquivalentTo([LedgerReportExportFormat.Csv, LedgerReportExportFormat.Xlsx]);
        exports[0].Recipients.Should().BeEquivalentTo(["investors@example.com", "regulator@example.com"]);
        exports[1].ReportId.Should().Be("investor-pack:2026-Q2:0002");
        exports[1].PeriodStart.Should().Be(new DateOnly(2026, 4, 1));
        exports[1].PeriodEnd.Should().Be(new DateOnly(2026, 6, 30));
    }

    [Fact]
    public void LedgerReportScheduledExport_CreatesReportPackRequest()
    {
        var schedule = new LedgerReportSchedule(
            "reg-pack",
            "fund-alpha",
            "Monthly regulatory export",
            LedgerReportScheduleFrequency.Monthly,
            new DateOnly(2026, 5, 1),
            dueDaysAfterPeriodEnd: 5,
            "usd",
            [LedgerReportExportFormat.Csv],
            ["regulator@example.com"],
            "controller",
            DateTimeOffset.Parse("2026-04-15T12:00:00Z"));
        var scheduledExport = LedgerReportSchedulePlanner.Project(schedule, occurrenceCount: 1).Single();
        var lockPeriod = new LockedAccountingPeriod(
            new LedgerBookKey("fund-alpha", "official", LedgerViewKind.Actual),
            "2026-05",
            new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 31, 23, 59, 59, 999, TimeSpan.Zero).AddTicks(9999),
            DateTimeOffset.Parse("2026-06-01T12:00:00Z"),
            "controller",
            "published close");

        var request = scheduledExport.ToReportPackRequest(
            "controller",
            DateTimeOffset.Parse("2026-06-01T13:00:00Z"),
            lockPeriod);

        request.ReportId.Should().Be("reg-pack:2026-05:0001");
        request.FundId.Should().Be("fund-alpha");
        request.PeriodId.Should().Be("2026-05");
        request.BaseCurrency.Should().Be("USD");
        request.AsOf.Should().Be(lockPeriod.EndsAtInclusive);
        request.LockedPeriod.Should().BeSameAs(lockPeriod);
    }

    [Fact]
    public void LedgerScheduledReportExportPackageBuilder_BuildsDeliveryManifestAndRegulatoryXml()
    {
        var chart = new ChartOfAccounts();
        var cash = chart.Register("Assets:Cash", LedgerAccountType.Asset);
        var capital = chart.Register("Equity:Capital", LedgerAccountType.Equity);
        var ledger = new Meridian.Ledger.Ledger();
        ledger.PostLines(
            new DateTimeOffset(2026, 5, 15, 12, 0, 0, TimeSpan.Zero),
            "capital contribution",
            [(cash, 1_000m, 0m), (capital, 0m, 1_000m)]);
        var schedule = new LedgerReportSchedule(
            "reg-pack",
            "fund-alpha",
            "Monthly regulatory export",
            LedgerReportScheduleFrequency.Monthly,
            new DateOnly(2026, 5, 1),
            dueDaysAfterPeriodEnd: 5,
            "usd",
            [LedgerReportExportFormat.Csv, LedgerReportExportFormat.RegulatoryXml],
            ["regulator@example.com", "controller@example.com"],
            "controller",
            DateTimeOffset.Parse("2026-04-15T12:00:00Z"));
        var scheduledExport = LedgerReportSchedulePlanner.Project(schedule, occurrenceCount: 1).Single();
        var request = scheduledExport.ToReportPackRequest(
            "controller",
            DateTimeOffset.Parse("2026-06-01T13:00:00Z"));
        var reportPack = LedgerReportPackBuilder.Build(ledger, request, chart);

        var artifacts = LedgerScheduledReportExportPackageBuilder.Build(reportPack, scheduledExport);

        artifacts.Should().Contain(artifact => artifact.Name == "scheduled-export-manifest.csv");
        artifacts.Should().Contain(artifact => artifact.Name == "regulatory-summary.xml");
        artifacts.Should().OnlyContain(artifact => artifact.ChecksumSha256.Length == 64);
        var manifest = artifacts.Single(artifact => artifact.Name == "scheduled-export-manifest.csv");
        manifest.Content.Should().Contain("ledger-scheduled-export-manifest-v1");
        manifest.Content.Should().Contain("formats,Csv|RegulatoryXml");
        manifest.Content.Should().Contain("recipients,controller@example.com|regulator@example.com");
        manifest.Content.Should().Contain("report-pack-signature,");
        manifest.Content.Should().Contain("financial-statements.json,application/json,");
        var regulatoryXml = artifacts.Single(artifact => artifact.Name == "regulatory-summary.xml");
        regulatoryXml.Content.Should().Contain("schema=\"ledger-regulatory-summary-v1\"");
        regulatoryXml.Content.Should().Contain("<FundId>fund-alpha</FundId>");
        regulatoryXml.Content.Should().Contain("<TotalAssets>1000</TotalAssets>");
        regulatoryXml.Content.Should().Contain("<AccountingEquationVariance>0</AccountingEquationVariance>");
        regulatoryXml.Content.Should().Contain("<Recipient>regulator@example.com</Recipient>");
    }

    [Fact]
    public void LedgerScheduledReportExportPackageBuilder_RejectsMismatchedReportPack()
    {
        var chart = new ChartOfAccounts();
        var cash = chart.Register("Assets:Cash", LedgerAccountType.Asset);
        var capital = chart.Register("Equity:Capital", LedgerAccountType.Equity);
        var ledger = new Meridian.Ledger.Ledger();
        ledger.PostLines(
            new DateTimeOffset(2026, 5, 15, 12, 0, 0, TimeSpan.Zero),
            "capital contribution",
            [(cash, 1_000m, 0m), (capital, 0m, 1_000m)]);
        var schedule = new LedgerReportSchedule(
            "reg-pack",
            "fund-alpha",
            "Monthly regulatory export",
            LedgerReportScheduleFrequency.Monthly,
            new DateOnly(2026, 5, 1),
            dueDaysAfterPeriodEnd: 5,
            "usd",
            [LedgerReportExportFormat.RegulatoryXml],
            ["regulator@example.com"],
            "controller",
            DateTimeOffset.Parse("2026-04-15T12:00:00Z"));
        var scheduledExport = LedgerReportSchedulePlanner.Project(schedule, occurrenceCount: 1).Single();
        var mismatchedRequest = new LedgerReportPackRequest(
            "different-report",
            "fund-alpha",
            "2026-05",
            new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 31, 23, 59, 59, 999, TimeSpan.Zero).AddTicks(9999),
            scheduledExport.AsOf,
            "usd",
            "controller",
            DateTimeOffset.Parse("2026-06-01T13:00:00Z"));
        var reportPack = LedgerReportPackBuilder.Build(ledger, mismatchedRequest, chart);

        var act = () => LedgerScheduledReportExportPackageBuilder.Build(reportPack, scheduledExport);

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*report identifier must match*");
    }

    [Fact]
    public void LedgerReportSchedule_RejectsEmptyRecipients()
    {
        var act = () => new LedgerReportSchedule(
            "reg-pack",
            "fund-alpha",
            "Monthly regulatory export",
            LedgerReportScheduleFrequency.Monthly,
            new DateOnly(2026, 5, 1),
            dueDaysAfterPeriodEnd: 5,
            "usd",
            [LedgerReportExportFormat.Csv],
            [" "],
            "controller",
            DateTimeOffset.UtcNow);

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*At least one non-empty report recipient*");
    }

    [Fact]
    public void MultiCurrencyLedgerTranslator_TranslatesLocalCurrencyBalancesToBaseCurrency()
    {
        var eurCash = LedgerAccounts.CashInCurrency("eur", "broker-1");
        var eurCapital = new LedgerAccount("Equity:Capital", LedgerAccountType.Equity, Symbol: "EUR", FinancialAccountId: "broker-1");
        var ledger = new Meridian.Ledger.Ledger();
        var ts = new DateTimeOffset(2026, 5, 28, 0, 0, 0, TimeSpan.Zero);

        ledger.PostLines(ts, "EUR subscription in local currency", new[]
        {
            (eurCash, 1_000m, 0m),
            (eurCapital, 0m, 1_000m),
        });

        var translation = MultiCurrencyLedgerTranslator.Translate(
            ledger,
            "USD",
            new Dictionary<string, decimal> { ["EUR"] = 1.1m },
            financialAccountId: "broker-1");

        translation.BaseCurrency.Should().Be("USD");
        translation.TranslatedTrialBalance[eurCash].Should().Be(1_100m);
        translation.Total(LedgerAccountType.Asset).Should().Be(1_100m);
        translation.Total(LedgerAccountType.Equity).Should().Be(1_100m);
        translation.Exposures.Single(exposure => exposure.Account == eurCash).LocalCurrency.Should().Be("EUR");
    }

    [Fact]
    public void MultiCurrencyJournalProjector_ConvertsLocalLinesToBalancedBasePosting()
    {
        var eurCash = LedgerAccounts.CashInCurrency("eur", "broker-1");
        var usdCapital = LedgerAccounts.CapitalAccountFor("broker-1");
        var input = new MultiCurrencyJournalInput(
            new DateTimeOffset(2026, 5, 28, 0, 0, 0, TimeSpan.Zero),
            "EUR subscription posted in USD base currency",
            "usd",
            [
                new MultiCurrencyJournalLineInput(eurCash, "eur", localDebit: 1_000m, localCredit: 0m, fxRateToBase: 1.10m),
                new MultiCurrencyJournalLineInput(usdCapital, "usd", localDebit: 0m, localCredit: 1_100m, fxRateToBase: 1m),
            ],
            new JournalEntryMetadata(ActivityType: "Subscription", FinancialAccountId: "broker-1"));

        var projection = MultiCurrencyJournalProjector.Project(input);
        var ledger = new Meridian.Ledger.Ledger();
        ledger.PostLines(input.Timestamp, input.Description, projection.ToLedgerLines(), input.Metadata);

        projection.IsBaseBalanced.Should().BeTrue();
        projection.TotalBaseDebits.Should().Be(1_100m);
        projection.TotalBaseCredits.Should().Be(1_100m);
        projection.Lines.Should().Contain(line =>
            line.Account == eurCash &&
            line.LocalCurrency == "EUR" &&
            line.BaseCurrency == "USD" &&
            line.LocalDebit == 1_000m &&
            line.FxRateToBase == 1.10m &&
            line.BaseDebit == 1_100m);
        ledger.GetBalance(eurCash).Should().Be(1_100m);
        ledger.GetBalance(usdCapital).Should().Be(1_100m);
    }

    [Fact]
    public void MultiCurrencyJournalProjector_RejectsUnbalancedBaseCurrencyPosting()
    {
        var input = new MultiCurrencyJournalInput(
            DateTimeOffset.UtcNow,
            "unbalanced local-to-base posting",
            "USD",
            [
                new MultiCurrencyJournalLineInput(LedgerAccounts.CashInCurrency("EUR", "broker-1"), "EUR", localDebit: 1_000m, localCredit: 0m, fxRateToBase: 1.10m),
                new MultiCurrencyJournalLineInput(LedgerAccounts.CapitalAccountFor("broker-1"), "USD", localDebit: 0m, localCredit: 1_000m, fxRateToBase: 1m),
            ]);

        var act = () => MultiCurrencyJournalProjector.Project(input);

        act.Should()
            .Throw<LedgerValidationException>()
            .WithMessage("*not balanced in base currency USD*");
    }

    [Fact]
    public void DailyPortfolioPricingProjector_ProjectsPolicyBackedMarksAndAuditEvidence()
    {
        var policy = new DailyPortfolioPricingPolicy(
            "fund-alpha",
            "policy-close-v1",
            "Fund Alpha daily close",
            "Listed close, then broker quote, then valuation committee model",
            "valuation-committee",
            new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero));
        var input = new DailyPortfolioPricingInput(
            policy,
            "2026-05",
            new DateTimeOffset(2026, 5, 31, 23, 59, 59, TimeSpan.Zero),
            "usd",
            [
                new DailyPortfolioPriceMark(
                    "aapl",
                    Quantity: 10m,
                    CostPrice: 100m,
                    MarkPrice: 120m,
                    PriceSource: "NYSE official close",
                    EvidenceReference: "price://nyse/aapl/2026-05-31",
                    FinancialAccountId: "broker-1",
                    InstrumentType: "ListedEquity"),
                new DailyPortfolioPriceMark(
                    "otc-note",
                    Quantity: 5m,
                    CostPrice: 100m,
                    MarkPrice: 90m,
                    PriceSource: "Valuation committee model",
                    EvidenceReference: "evidence://valuation/otc-note/2026-05-31",
                    FinancialAccountId: "broker-1",
                    InstrumentType: "OtcInstrument"),
            ]);

        var projection = DailyPortfolioPricingProjector.Project(input);

        projection.TotalCostBasis.Should().Be(1_500m);
        projection.TotalMarketValue.Should().Be(1_650m);
        projection.NetUnrealizedGainOrLoss.Should().Be(150m);
        projection.IsBalanced.Should().BeTrue();
        projection.Lines.Should().Contain(line =>
            line.Symbol == "AAPL" &&
            line.MarketValue == 1_200m &&
            line.UnrealizedGainOrLoss == 200m &&
            line.PolicyId == "policy-close-v1" &&
            line.PriceSource == "NYSE official close" &&
            line.EvidenceReference == "price://nyse/aapl/2026-05-31");
        projection.Lines.Should().Contain(line =>
            line.Symbol == "OTC-NOTE" &&
            line.MarketValue == 450m &&
            line.UnrealizedGainOrLoss == -50m &&
            line.InstrumentType == "OtcInstrument");
        projection.JournalLines.Should().Contain(line =>
            line.account.Name == "Securities" &&
            line.account.Symbol == "AAPL" &&
            line.account.FinancialAccountId == "broker-1" &&
            line.debit == 200m);
        projection.JournalLines.Should().Contain(line =>
            line.account.Name == "Unrealized Gain" &&
            line.account.FinancialAccountId == "broker-1" &&
            line.credit == 200m);
        projection.JournalLines.Should().Contain(line =>
            line.account.Name == "Unrealized Loss" &&
            line.account.FinancialAccountId == "broker-1" &&
            line.debit == 50m);
        projection.JournalLines.Should().Contain(line =>
            line.account.Name == "Securities" &&
            line.account.Symbol == "OTC-NOTE" &&
            line.credit == 50m);
    }

    [Fact]
    public void DailyPortfolioPriceMark_RejectsMissingPricingEvidence()
    {
        var act = () => new DailyPortfolioPriceMark(
            "AAPL",
            Quantity: 10m,
            CostPrice: 100m,
            MarkPrice: 120m,
            PriceSource: "NYSE official close",
            EvidenceReference: " ");

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*evidence reference must not be null or whitespace*");
    }

    [Fact]
    public void MultiCurrencyLedgerTranslator_BuildsBalancedUnrealizedFxRevaluationLines()
    {
        var eurCash = LedgerAccounts.CashInCurrency("EUR", "broker-1");
        var trialBalance = new Dictionary<LedgerAccount, decimal>
        {
            [eurCash] = 1_000m,
        };
        var carryingBaseBalances = new Dictionary<LedgerAccount, decimal>
        {
            [eurCash] = 1_050m,
        };

        var translation = MultiCurrencyLedgerTranslator.Translate(
            trialBalance,
            "USD",
            new Dictionary<string, decimal> { ["EUR"] = 1.1m },
            carryingBaseBalances: carryingBaseBalances);
        var lines = MultiCurrencyLedgerTranslator.BuildUnrealizedFxRevaluationLines(translation, "broker-1");

        lines.Should().HaveCount(2);
        lines.Sum(line => line.debit).Should().Be(50m);
        lines.Sum(line => line.credit).Should().Be(50m);
        lines.Should().Contain(line => line.account == eurCash && line.debit == 50m && line.credit == 0m);
        lines.Should().Contain(line =>
            line.account.Name == "Unrealized FX Gain" &&
            line.account.FinancialAccountId == "broker-1" &&
            line.debit == 0m &&
            line.credit == 50m);
    }

    [Fact]
    public void FixedIncomeAmortizationProjector_ProjectsCouponAndDiscountAccretionLines()
    {
        var carryingAccount = LedgerAccounts.Securities("corp2029", "broker-1");

        var projection = FixedIncomeAmortizationProjector.Project(new FixedIncomeAmortizationInput(
            "corp2029",
            carryingAccount,
            CouponAccrual: 20m,
            DiscountAccretion: 5m,
            PremiumAmortization: 0m,
            FinancialAccountId: "broker-1"));

        projection.Symbol.Should().Be("CORP2029");
        projection.IsBalanced.Should().BeTrue();
        projection.TotalDebits.Should().Be(25m);
        projection.TotalCredits.Should().Be(25m);
        projection.Lines.Should().Contain(line =>
            line.account.Name == "Accrued Interest Receivable" &&
            line.account.Symbol == "CORP2029" &&
            line.debit == 20m);
        projection.Lines.Should().Contain(line => line.account == carryingAccount && line.debit == 5m);
        projection.Lines.Where(line => line.account.Name == "Coupon Income").Sum(line => line.credit).Should().Be(25m);
    }

    [Fact]
    public void FixedIncomeAmortizationProjector_ProjectsPremiumAmortizationAsIncomeReduction()
    {
        var carryingAccount = LedgerAccounts.Securities("muni2031");

        var projection = FixedIncomeAmortizationProjector.Project(new FixedIncomeAmortizationInput(
            "muni2031",
            carryingAccount,
            CouponAccrual: 0m,
            DiscountAccretion: 0m,
            PremiumAmortization: 7.5m));

        projection.IsBalanced.Should().BeTrue();
        projection.Lines.Should().Contain(line => line.account == LedgerAccounts.CouponIncome && line.debit == 7.5m);
        projection.Lines.Should().Contain(line => line.account == carryingAccount && line.credit == 7.5m);
    }

    [Fact]
    public void FixedIncomeAmortizationProjector_RejectsNegativeAmounts()
    {
        var carryingAccount = LedgerAccounts.Securities("UST2030");

        var act = () => FixedIncomeAmortizationProjector.Project(new FixedIncomeAmortizationInput(
            "UST2030",
            carryingAccount,
            CouponAccrual: 0m,
            DiscountAccretion: -1m,
            PremiumAmortization: 0m));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void LedgerAccountTaxLotPolicyBook_ResolvesAccountSpecificReliefMethod()
    {
        var policyBook = new LedgerAccountTaxLotPolicyBook();
        var account = LedgerAccounts.Securities("AAPL", "broker-1");
        var effectiveDate = new DateOnly(2026, 1, 1);

        var registered = policyBook.Register(
            account,
            LedgerTaxLotReliefMethod.Hifo,
            "policy-hifo-aapl",
            effectiveDate,
            "Minimize realized gains for this sleeve.");

        var resolved = policyBook.Resolve(account, new DateOnly(2026, 5, 28));

        resolved.Should().Be(registered);
        resolved.ReliefMethod.Should().Be(LedgerTaxLotReliefMethod.Hifo);
        resolved.PolicyId.Should().Be("policy-hifo-aapl");
        resolved.Rationale.Should().Be("Minimize realized gains for this sleeve.");
    }

    [Fact]
    public void LedgerAccountTaxLotPolicyBook_UsesDefaultWhenAccountPolicyIsMissing()
    {
        var policyBook = new LedgerAccountTaxLotPolicyBook(LedgerTaxLotReliefMethod.Lifo);
        var account = LedgerAccounts.Securities("MSFT", "broker-2");

        var resolved = policyBook.Resolve(account, new DateOnly(2026, 5, 28));

        resolved.Account.Should().Be(account);
        resolved.ReliefMethod.Should().Be(LedgerTaxLotReliefMethod.Lifo);
        resolved.PolicyId.Should().Be("default");
    }

    [Fact]
    public void LedgerAccountTaxLotPolicyBook_RejectsPoliciesBeforeEffectiveDate()
    {
        var policyBook = new LedgerAccountTaxLotPolicyBook();
        var account = LedgerAccounts.Securities("TSLA");
        policyBook.Register(account, LedgerTaxLotReliefMethod.SpecificId, "policy-specific", new DateOnly(2026, 6, 1));

        var act = () => policyBook.Resolve(account, new DateOnly(2026, 5, 28));

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*not effective until 2026-06-01*");
    }

    [Fact]
    public void LedgerTaxLotReliefProjector_UsesAccountPolicyToRelieveHifoLotsAndProjectGain()
    {
        var account = LedgerAccounts.Securities("AAPL", "broker-1");
        var policyBook = new LedgerAccountTaxLotPolicyBook();
        var policy = policyBook.Register(
            account,
            LedgerTaxLotReliefMethod.Hifo,
            "policy-hifo-aapl",
            new DateOnly(2026, 1, 1),
            "Align realized P&L with front-office HIFO relief.");
        var input = new LedgerTaxLotReliefInput(
            account,
            new DateOnly(2026, 5, 28),
            quantitySold: 12m,
            salePrice: 130m,
            policy.ReliefMethod,
            [
                new LedgerTaxLot("lot-low", new DateOnly(2026, 1, 1), 10m, 90m),
                new LedgerTaxLot("lot-high", new DateOnly(2026, 2, 1), 8m, 110m),
                new LedgerTaxLot("lot-mid", new DateOnly(2026, 3, 1), 10m, 100m),
            ]);

        var projection = LedgerTaxLotReliefProjector.Project(input);

        projection.IsBalanced.Should().BeTrue();
        projection.Selections.Should().HaveCount(2);
        projection.Selections[0].Lot.LotId.Should().Be("lot-high");
        projection.Selections[0].QuantityRelieved.Should().Be(8m);
        projection.Selections[1].Lot.LotId.Should().Be("lot-mid");
        projection.Selections[1].QuantityRelieved.Should().Be(4m);
        projection.Proceeds.Should().Be(1560m);
        projection.CostBasis.Should().Be(1280m);
        projection.RealizedGainOrLoss.Should().Be(280m);
        projection.Lines.Should().Contain(line => line.account == LedgerAccounts.CashAccount("broker-1") && line.debit == 1560m);
        projection.Lines.Should().Contain(line => line.account == account && line.credit == 1280m);
        projection.Lines.Should().Contain(line => line.account == LedgerAccounts.RealizedGainFor("broker-1") && line.credit == 280m);
    }

    [Fact]
    public void LedgerTaxLotReliefProjector_UsesSpecificIdsAndRejectsMissingSpecificSelection()
    {
        var account = LedgerAccounts.Securities("MSFT", "broker-2");
        var openLots = new[]
        {
            new LedgerTaxLot("lot-a", new DateOnly(2026, 1, 1), 5m, 200m),
            new LedgerTaxLot("lot-b", new DateOnly(2026, 2, 1), 5m, 210m),
        };
        var projection = LedgerTaxLotReliefProjector.Project(new LedgerTaxLotReliefInput(
            account,
            new DateOnly(2026, 5, 28),
            quantitySold: 4m,
            salePrice: 220m,
            LedgerTaxLotReliefMethod.SpecificId,
            openLots,
            specificLotIds: ["lot-b"]));

        projection.IsBalanced.Should().BeTrue();
        projection.Selections.Should().ContainSingle();
        projection.Selections[0].Lot.LotId.Should().Be("lot-b");
        projection.CostBasis.Should().Be(840m);
        projection.RealizedGainOrLoss.Should().Be(40m);

        var act = () => LedgerTaxLotReliefProjector.Project(new LedgerTaxLotReliefInput(
            account,
            new DateOnly(2026, 5, 28),
            quantitySold: 4m,
            salePrice: 220m,
            LedgerTaxLotReliefMethod.SpecificId,
            openLots));

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*SpecificId relief requires at least one selected lot identifier*");
    }

    [Fact]
    public void LedgerTaxLotReliefProjector_RejectsInsufficientOpenLotQuantity()
    {
        var account = LedgerAccounts.Securities("TSLA");
        var input = new LedgerTaxLotReliefInput(
            account,
            new DateOnly(2026, 5, 28),
            quantitySold: 8m,
            salePrice: 185m,
            LedgerTaxLotReliefMethod.Fifo,
            [new LedgerTaxLot("lot-a", new DateOnly(2026, 1, 1), 5m, 150m)]);

        var act = () => LedgerTaxLotReliefProjector.Project(input);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Insufficient open tax-lot quantity*");
    }

    [Fact]
    public void AutomatedJournalDraftProjector_ProjectsDividendDeclarationAndReceipt()
    {
        var declared = AutomatedJournalDraftProjector.Project(new AutomatedJournalEvent(
            AutomatedJournalEventKind.DividendDeclared,
            "aapl",
            42m,
            new DateTimeOffset(2026, 5, 28, 0, 0, 0, TimeSpan.Zero),
            FinancialAccountId: "broker-1",
            SourceEventId: "div-001"));
        var received = AutomatedJournalDraftProjector.Project(new AutomatedJournalEvent(
            AutomatedJournalEventKind.DividendReceived,
            "AAPL",
            42m,
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            FinancialAccountId: "broker-1"));

        declared.IsBalanced.Should().BeTrue();
        declared.Metadata.ActivityType.Should().Be(nameof(AutomatedJournalEventKind.DividendDeclared));
        declared.Metadata.Symbol.Should().Be("AAPL");
        declared.Metadata.Tags.Should().ContainKey("sourceEventId");
        declared.Lines.Should().Contain(line => line.account.Name == "Dividend Receivable" && line.account.Symbol == "AAPL" && line.debit == 42m);
        declared.Lines.Should().Contain(line => line.account.Name == "Dividend Income" && line.account.FinancialAccountId == "broker-1" && line.credit == 42m);

        received.IsBalanced.Should().BeTrue();
        received.Lines.Should().Contain(line => line.account.Name == "Cash" && line.account.FinancialAccountId == "broker-1" && line.debit == 42m);
        received.Lines.Should().Contain(line => line.account.Name == "Dividend Receivable" && line.credit == 42m);
    }

    [Fact]
    public void AutomatedJournalDraftProjector_ProjectsCorporateActionExpense()
    {
        var draft = AutomatedJournalDraftProjector.Project(new AutomatedJournalEvent(
            AutomatedJournalEventKind.CorporateActionExpense,
            "msft",
            12.5m,
            DateTimeOffset.UtcNow,
            FinancialAccountId: "broker-2"));

        draft.IsBalanced.Should().BeTrue();
        draft.Lines.Should().Contain(line => line.account.Name == "Corporate Action Expense" && line.account.FinancialAccountId == "broker-2" && line.debit == 12.5m);
        draft.Lines.Should().Contain(line => line.account.Name == "Cash" && line.account.FinancialAccountId == "broker-2" && line.credit == 12.5m);
    }

    [Fact]
    public void AutomatedJournalDraftProjector_ProjectsRecurringAccrualObligations()
    {
        var performanceFee = AutomatedJournalDraftProjector.Project(new AutomatedJournalEvent(
            AutomatedJournalEventKind.PerformanceFeeAccrued,
            "fund-alpha",
            75m,
            new DateTimeOffset(2026, 5, 31, 23, 59, 59, TimeSpan.Zero),
            FinancialAccountId: "fund-alpha",
            SourceEventId: "fee-run-001"));
        var commission = AutomatedJournalDraftProjector.Project(new AutomatedJournalEvent(
            AutomatedJournalEventKind.CommissionAccrued,
            "broker",
            8.25m,
            new DateTimeOffset(2026, 5, 31, 23, 59, 59, TimeSpan.Zero),
            FinancialAccountId: "broker-1"));
        var withholding = AutomatedJournalDraftProjector.Project(new AutomatedJournalEvent(
            AutomatedJournalEventKind.WithholdingTaxAccrued,
            "AAPL",
            4.50m,
            new DateTimeOffset(2026, 5, 31, 23, 59, 59, TimeSpan.Zero),
            FinancialAccountId: "broker-1"));

        performanceFee.IsBalanced.Should().BeTrue();
        performanceFee.Metadata.ActivityType.Should().Be(nameof(AutomatedJournalEventKind.PerformanceFeeAccrued));
        performanceFee.Metadata.Tags.Should().ContainKey("sourceEventId");
        performanceFee.Lines.Should().Contain(line => line.account.Name == "Performance Fee Expense" && line.account.FinancialAccountId == "fund-alpha" && line.debit == 75m);
        performanceFee.Lines.Should().Contain(line => line.account.Name == "Performance Fee Payable" && line.account.FinancialAccountId == "fund-alpha" && line.credit == 75m);

        commission.IsBalanced.Should().BeTrue();
        commission.Lines.Should().Contain(line => line.account.Name == "Commission Expense" && line.account.FinancialAccountId == "broker-1" && line.debit == 8.25m);
        commission.Lines.Should().Contain(line => line.account.Name == "Commission Payable" && line.account.FinancialAccountId == "broker-1" && line.credit == 8.25m);

        withholding.IsBalanced.Should().BeTrue();
        withholding.Lines.Should().Contain(line => line.account.Name == "Withholding Tax Expense" && line.account.FinancialAccountId == "broker-1" && line.debit == 4.50m);
        withholding.Lines.Should().Contain(line => line.account.Name == "Withholding Tax Payable" && line.account.FinancialAccountId == "broker-1" && line.credit == 4.50m);
    }

    [Fact]
    public void AutomatedJournalDraftProjector_RejectsNonPositiveAmounts()
    {
        var act = () => AutomatedJournalDraftProjector.Project(new AutomatedJournalEvent(
            AutomatedJournalEventKind.CashInterestCredited,
            "USD",
            0m,
            DateTimeOffset.UtcNow));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AutomatedJournalApproval_ApprovesAndPostsDraftWithAuditEvidence()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var draft = AutomatedJournalDraftProjector.Project(new AutomatedJournalEvent(
            AutomatedJournalEventKind.ManagementFeeAccrued,
            "fund-alpha",
            125m,
            new DateTimeOffset(2026, 5, 31, 23, 59, 59, TimeSpan.Zero),
            FinancialAccountId: "fund-alpha",
            SourceEventId: "fee-run-2026-05"));

        var submitted = Meridian.Ledger.AutomatedJournalApproval.Submit(
            draft,
            "automation",
            new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero),
            "monthly fee automation run",
            ["job:fee-run-2026-05"]);
        var approved = submitted.Approve(
            "controller",
            new DateTimeOffset(2026, 6, 1, 13, 0, 0, TimeSpan.Zero),
            "controller approved May accrual",
            ["approval:controller:2026-05"]);
        var posted = approved.PostTo(
            ledger,
            "ledger-bot",
            new DateTimeOffset(2026, 6, 1, 13, 5, 0, TimeSpan.Zero),
            "posted approved automated accrual",
            ["journal-store:automated:2026-05"]);

        posted.Status.Should().Be(Meridian.Ledger.AutomatedJournalApprovalStatus.Posted);
        posted.Events.Select(item => item.ToStatus).Should().Equal(
            Meridian.Ledger.AutomatedJournalApprovalStatus.Submitted,
            Meridian.Ledger.AutomatedJournalApprovalStatus.Approved,
            Meridian.Ledger.AutomatedJournalApprovalStatus.Posted);
        ledger.Journal.Should().ContainSingle();
        var journal = ledger.Journal.Single();
        journal.JournalEntryId.Should().Be(posted.JournalEntryId);
        journal.Metadata.Tags.Should().ContainKey("sourceEventId").WhoseValue.Should().Be("fee-run-2026-05");
        journal.Metadata.Tags.Should().ContainKey("automatedJournalApprovalId").WhoseValue.Should().Be(posted.ApprovalId.ToString("D"));
        journal.Metadata.Tags.Should().ContainKey("approvedBy").WhoseValue.Should().Be("controller");
        ledger.GetBalance(LedgerAccounts.ManagementFeeExpenseFor("fund-alpha")).Should().Be(125m);
        ledger.GetBalance(LedgerAccounts.ManagementFeePayableFor("fund-alpha")).Should().Be(125m);
    }

    [Fact]
    public void AutomatedJournalApproval_RejectsMissingEvidenceAndUnapprovedPosting()
    {
        var draft = AutomatedJournalDraftProjector.Project(new AutomatedJournalEvent(
            AutomatedJournalEventKind.WithholdingTaxAccrued,
            "AAPL",
            5m,
            new DateTimeOffset(2026, 5, 31, 23, 59, 59, TimeSpan.Zero),
            FinancialAccountId: "broker-1"));
        var submitted = Meridian.Ledger.AutomatedJournalApproval.Submit(
            draft,
            "automation",
            new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero),
            "withholding tax automation run");

        var approveWithoutEvidence = () => submitted.Approve(
            "controller",
            new DateTimeOffset(2026, 6, 1, 13, 0, 0, TimeSpan.Zero),
            "approved",
            []);
        var postBeforeApproval = () => submitted.PostTo(
            new Meridian.Ledger.Ledger(),
            "ledger-bot",
            new DateTimeOffset(2026, 6, 1, 13, 5, 0, TimeSpan.Zero),
            "posted",
            ["journal-store:withholding"]);
        var rejected = submitted.Reject(
            "controller",
            new DateTimeOffset(2026, 6, 1, 13, 10, 0, TimeSpan.Zero),
            "missing withholding evidence");

        approveWithoutEvidence.Should().Throw<ArgumentException>().WithMessage("*evidence is required*");
        postBeforeApproval.Should().Throw<InvalidOperationException>().WithMessage("*Only approved*");
        rejected.Status.Should().Be(Meridian.Ledger.AutomatedJournalApprovalStatus.Rejected);
        rejected.Events.Last().Reason.Should().Be("missing withholding evidence");
    }

    [Fact]
    public void LockedAccountingPeriodBook_RejectsPostingInsideLockedPeriod()
    {
        var projectLedgers = new ProjectLedgerBook("fund-alpha");
        var locks = new LockedAccountingPeriodBook();
        var key = new LedgerBookKey("fund-alpha", "Fund", LedgerViewKind.Actual);
        var cash = LedgerAccounts.CashAccount("broker-1");
        var revenue = LedgerAccounts.DividendIncomeFor("broker-1");
        var periodStart = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        var periodEnd = new DateTimeOffset(2026, 5, 31, 23, 59, 59, TimeSpan.Zero);

        locks.LockPeriod(
            key,
            "2026-05",
            periodStart,
            periodEnd,
            new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero),
            "nav-controller",
            "Published May NAV.");

        var act = () => locks.PostLines(
            projectLedgers,
            key,
            new DateTimeOffset(2026, 5, 15, 12, 0, 0, TimeSpan.Zero),
            "late dividend",
            new[] { (cash, 15m, 0m), (revenue, 0m, 15m) });

        act.Should()
            .Throw<LedgerValidationException>()
            .WithMessage("*2026-05*locked*");
        projectLedgers.GetOrCreate(key).JournalEntryCount.Should().Be(0);
    }

    [Fact]
    public void LockedAccountingPeriodBook_AllowsPostingOutsideLockedPeriodAndScopesByBook()
    {
        var projectLedgers = new ProjectLedgerBook("fund-alpha");
        var locks = new LockedAccountingPeriodBook();
        var actualKey = new LedgerBookKey("fund-alpha", "Fund", LedgerViewKind.Actual);
        var shadowKey = new LedgerBookKey("fund-alpha", "ShadowNAV", LedgerViewKind.Actual);
        var cash = LedgerAccounts.CashAccount("broker-1");
        var revenue = LedgerAccounts.CashInterestIncomeFor("broker-1");

        locks.LockPeriod(
            actualKey,
            "2026-05",
            new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 31, 23, 59, 59, TimeSpan.Zero),
            DateTimeOffset.UtcNow,
            "nav-controller",
            "Published May NAV.");

        locks.PostLines(
            projectLedgers,
            actualKey,
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            "June interest",
            new[] { (cash, 5m, 0m), (revenue, 0m, 5m) });
        locks.PostLines(
            projectLedgers,
            shadowKey,
            new DateTimeOffset(2026, 5, 15, 0, 0, 0, TimeSpan.Zero),
            "shadow nav adjustment",
            new[] { (cash, 7m, 0m), (revenue, 0m, 7m) });

        projectLedgers.GetOrCreate(actualKey).JournalEntryCount.Should().Be(1);
        projectLedgers.GetOrCreate(shadowKey).JournalEntryCount.Should().Be(1);
        locks.TryFindLock(actualKey, new DateTimeOffset(2026, 5, 10, 0, 0, 0, TimeSpan.Zero), out var lockedPeriod).Should().BeTrue();
        lockedPeriod!.LockedBy.Should().Be("nav-controller");
    }

    [Fact]
    public void LockedAccountingPeriodBook_RejectsOverlappingLocksForSameBook()
    {
        var locks = new LockedAccountingPeriodBook();
        var key = new LedgerBookKey("fund-alpha", "Fund");

        locks.LockPeriod(
            key,
            "2026-05",
            new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 31, 23, 59, 59, TimeSpan.Zero),
            DateTimeOffset.UtcNow,
            "nav-controller",
            "Published May NAV.");

        var act = () => locks.LockPeriod(
            key,
            "2026-05-overlap",
            new DateTimeOffset(2026, 5, 15, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 15, 23, 59, 59, TimeSpan.Zero),
            DateTimeOffset.UtcNow,
            "nav-controller",
            "Duplicate close range.");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*overlaps*");
    }

    [Fact]
    public void ShadowNavValidator_ReportsMatchedActualAndShadowLedgersWithinTolerance()
    {
        var projectLedgers = new ProjectLedgerBook("fund-alpha");
        var actualKey = new LedgerBookKey("fund-alpha", "Fund", LedgerViewKind.Actual);
        var shadowKey = new LedgerBookKey("fund-alpha", "ShadowNAV", LedgerViewKind.Actual);
        var asOf = new DateTimeOffset(2026, 5, 31, 23, 59, 59, TimeSpan.Zero);
        var cash = LedgerAccounts.CashAccount("broker-1");
        var capital = LedgerAccounts.CapitalAccountFor("broker-1");

        projectLedgers.GetOrCreate(actualKey).PostLines(
            asOf,
            "actual nav",
            new[] { (cash, 1_000m, 0m), (capital, 0m, 1_000m) });
        projectLedgers.GetOrCreate(shadowKey).PostLines(
            asOf,
            "shadow nav",
            new[] { (cash, 1_000m, 0m), (capital, 0m, 1_000m) });

        var report = ShadowNavValidator.Validate(
            projectLedgers,
            actualKey,
            shadowKey,
            asOf,
            new ShadowNavValidationPolicy(navTolerance: 0.01m, accountTolerance: 0.01m, reviewerGroup: "fund-admin"));

        report.ActualNav.Should().Be(1_000m);
        report.ShadowNav.Should().Be(1_000m);
        report.RequiresOverride.Should().BeFalse();
        report.Findings.Should().BeEmpty();
    }

    [Fact]
    public void ShadowNavValidator_FlagsMaterialVarianceAndCreatesOverrideDraft()
    {
        var projectLedgers = new ProjectLedgerBook("fund-alpha");
        var actualKey = new LedgerBookKey("fund-alpha", "Fund", LedgerViewKind.Actual);
        var shadowKey = new LedgerBookKey("fund-alpha", "ShadowNAV", LedgerViewKind.Actual);
        var asOf = new DateTimeOffset(2026, 5, 31, 23, 59, 59, TimeSpan.Zero);
        var cash = LedgerAccounts.CashAccount("broker-1");
        var capital = LedgerAccounts.CapitalAccountFor("broker-1");

        projectLedgers.GetOrCreate(actualKey).PostLines(
            asOf,
            "actual nav",
            new[] { (cash, 1_000m, 0m), (capital, 0m, 1_000m) });
        projectLedgers.GetOrCreate(shadowKey).PostLines(
            asOf,
            "shadow nav",
            new[] { (cash, 995m, 0m), (capital, 0m, 995m) });

        var report = ShadowNavValidator.Validate(
            projectLedgers,
            actualKey,
            shadowKey,
            asOf,
            new ShadowNavValidationPolicy(navTolerance: 1m, accountTolerance: 1m, policyId: "may-nav-policy", reviewerGroup: "fund-admin"));
        var overrideDraft = report.CreateOverrideDraft(
            new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero),
            "controller",
            "Fund administrator shadow NAV differs from actual book.");

        report.RequiresOverride.Should().BeTrue();
        report.NavVariance.Should().Be(5m);
        report.Findings.Should().Contain(finding =>
            finding.Account == cash &&
            finding.ActualBalance == 1_000m &&
            finding.ShadowBalance == 995m &&
            finding.RequiresOverride);
        overrideDraft.PolicyId.Should().Be("may-nav-policy");
        overrideDraft.ReviewerGroup.Should().Be("fund-admin");
        overrideDraft.CreatedBy.Should().Be("controller");
        overrideDraft.NavVariance.Should().Be(5m);
        overrideDraft.Findings.Should().BeSameAs(report.Findings);
    }

    [Fact]
    public void ShadowNavValidator_RejectsMissingShadowLedger()
    {
        var projectLedgers = new ProjectLedgerBook("fund-alpha");
        var actualKey = new LedgerBookKey("fund-alpha", "Fund", LedgerViewKind.Actual);
        var shadowKey = new LedgerBookKey("fund-alpha", "ShadowNAV", LedgerViewKind.Actual);
        var asOf = new DateTimeOffset(2026, 5, 31, 23, 59, 59, TimeSpan.Zero);

        projectLedgers.GetOrCreate(actualKey).PostLines(
            asOf,
            "actual nav",
            new[]
            {
                (LedgerAccounts.CashAccount("broker-1"), 100m, 0m),
                (LedgerAccounts.CapitalAccountFor("broker-1"), 0m, 100m),
            });

        var act = () => ShadowNavValidator.Validate(
            projectLedgers,
            actualKey,
            shadowKey,
            asOf,
            new ShadowNavValidationPolicy(navTolerance: 0.01m, accountTolerance: 0.01m));

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Shadow ledger*not found*");
    }

    [Fact]
    public void PartnershipInvestorAccountingProjector_ProjectsFeesHighWaterMarkAndInvestorAllocations()
    {
        var input = new PartnershipInvestorAllocationInput(
            "master-fund",
            "2026-05",
            new DateTimeOffset(2026, 5, 31, 23, 59, 59, TimeSpan.Zero),
            BeginningNav: 1_000m,
            EndingNavBeforeFees: 1_200m,
            HighWaterMark: 1_050m,
            ManagementFeeRate: 0.02m,
            PerformanceFeeRate: 0.20m,
            [
                new PartnershipInvestor("feeder-a", "Feeder A", 0.60m),
                new PartnershipInvestor("spv-b", "SPV B", 0.40m),
            ]);

        var projection = PartnershipInvestorAccountingProjector.Project(input);

        projection.ManagementFee.Should().Be(20m);
        projection.PerformanceFee.Should().Be(26m);
        projection.AllocableProfitOrLoss.Should().Be(154m);
        projection.EndingNavAfterFees.Should().Be(1_154m);
        projection.UpdatedHighWaterMark.Should().Be(1_154m);
        projection.IsBalanced.Should().BeTrue();
        projection.InvestorAllocations.Should().Contain(allocation =>
            allocation.Investor.InvestorId == "feeder-a" &&
            allocation.AllocatedProfitOrLoss == 92.40m &&
            allocation.CapitalAccount.Name == "Investor Capital");
        projection.InvestorAllocations.Should().Contain(allocation =>
            allocation.Investor.InvestorId == "spv-b" &&
            allocation.AllocatedProfitOrLoss == 61.60m);
        projection.Lines.Should().Contain(line => line.account.Name == "Management Fee Expense" && line.debit == 20m);
        projection.Lines.Should().Contain(line => line.account.Name == "Management Fee Payable" && line.credit == 20m);
        projection.Lines.Should().Contain(line => line.account.Name == "Performance Fee Expense" && line.debit == 26m);
        projection.Lines.Should().Contain(line => line.account.Name == "Performance Fee Payable" && line.credit == 26m);
        projection.Lines.Should().Contain(line => line.account.Name == "Retained Earnings" && line.debit == 154m);
    }

    [Fact]
    public void PartnershipInvestorAccountingProjector_AllocatesLossesToInvestorCapital()
    {
        var input = new PartnershipInvestorAllocationInput(
            "master-fund",
            "2026-06",
            new DateTimeOffset(2026, 6, 30, 23, 59, 59, TimeSpan.Zero),
            BeginningNav: 1_000m,
            EndingNavBeforeFees: 900m,
            HighWaterMark: 1_154m,
            ManagementFeeRate: 0m,
            PerformanceFeeRate: 0.20m,
            [
                new PartnershipInvestor("feeder-a", "Feeder A", 0.50m),
                new PartnershipInvestor("feeder-b", "Feeder B", 0.50m),
            ]);

        var projection = PartnershipInvestorAccountingProjector.Project(input);

        projection.ManagementFee.Should().Be(0m);
        projection.PerformanceFee.Should().Be(0m);
        projection.AllocableProfitOrLoss.Should().Be(-100m);
        projection.UpdatedHighWaterMark.Should().Be(1_154m);
        projection.IsBalanced.Should().BeTrue();
        projection.Lines.Should().Contain(line => line.account.FinancialAccountId == "feeder-a" && line.debit == 50m);
        projection.Lines.Should().Contain(line => line.account.FinancialAccountId == "feeder-b" && line.debit == 50m);
        projection.Lines.Should().Contain(line => line.account.Name == "Retained Earnings" && line.credit == 100m);
    }

    [Fact]
    public void PartnershipInvestorAccountingProjector_RejectsInvalidInvestorAllocationWeights()
    {
        var input = new PartnershipInvestorAllocationInput(
            "master-fund",
            "2026-05",
            DateTimeOffset.UtcNow,
            BeginningNav: 1_000m,
            EndingNavBeforeFees: 1_100m,
            HighWaterMark: 1_000m,
            ManagementFeeRate: 0.01m,
            PerformanceFeeRate: 0.10m,
            [
                new PartnershipInvestor("investor-a", "Investor A", 0.50m),
                new PartnershipInvestor("investor-b", "Investor B", 0.25m),
            ]);

        var act = () => PartnershipInvestorAccountingProjector.Project(input);

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*sum to 1.000000*");
    }

    [Fact]
    public void PartnershipWaterfallProjector_AllocatesPreferredReturnAndCarryTiers()
    {
        var input = new PartnershipWaterfallAllocationInput(
            "master-fund",
            "2026-05",
            new DateTimeOffset(2026, 5, 31, 23, 59, 59, TimeSpan.Zero),
            DistributableProfit: 200m,
            [
                new PartnershipInvestor("limited-partner", "Limited Partner", 0.80m),
                new PartnershipInvestor("general-partner", "General Partner", 0.20m),
            ],
            [
                new PartnershipWaterfallTier(
                    "preferred-return",
                    "Preferred return to limited partner",
                    [new PartnershipWaterfallAllocationRule("limited-partner", 1m)],
                    capAmount: 100m),
                new PartnershipWaterfallTier(
                    "carry",
                    "Residual carried-interest split",
                    [
                        new PartnershipWaterfallAllocationRule("limited-partner", 0.80m),
                        new PartnershipWaterfallAllocationRule("general-partner", 0.20m),
                    ]),
            ]);

        var projection = PartnershipWaterfallProjector.Project(input);

        projection.AllocatedProfit.Should().Be(200m);
        projection.UnallocatedProfit.Should().Be(0m);
        projection.IsBalanced.Should().BeTrue();
        projection.TierAllocations.Should().Contain(allocation =>
            allocation.TierId == "preferred-return" &&
            allocation.Investor.InvestorId == "limited-partner" &&
            allocation.AllocatedAmount == 100m);
        projection.TierAllocations.Should().Contain(allocation =>
            allocation.TierId == "carry" &&
            allocation.Investor.InvestorId == "limited-partner" &&
            allocation.AllocatedAmount == 80m);
        projection.TierAllocations.Should().Contain(allocation =>
            allocation.TierId == "carry" &&
            allocation.Investor.InvestorId == "general-partner" &&
            allocation.AllocatedAmount == 20m);
        projection.InvestorAllocations.Should().Contain(allocation =>
            allocation.Investor.InvestorId == "limited-partner" &&
            allocation.AllocatedProfitOrLoss == 180m);
        projection.InvestorAllocations.Should().Contain(allocation =>
            allocation.Investor.InvestorId == "general-partner" &&
            allocation.AllocatedProfitOrLoss == 20m);
        projection.Lines.Should().Contain(line => line.account.Name == "Retained Earnings" && line.debit == 200m);
        projection.Lines.Should().Contain(line => line.account.FinancialAccountId == "limited-partner" && line.credit == 180m);
        projection.Lines.Should().Contain(line => line.account.FinancialAccountId == "general-partner" && line.credit == 20m);
    }

    [Fact]
    public void PartnershipWaterfallProjector_RejectsInvalidTierWeights()
    {
        var input = new PartnershipWaterfallAllocationInput(
            "master-fund",
            "2026-05",
            DateTimeOffset.UtcNow,
            DistributableProfit: 100m,
            [
                new PartnershipInvestor("limited-partner", "Limited Partner", 0.80m),
                new PartnershipInvestor("general-partner", "General Partner", 0.20m),
            ],
            [
                new PartnershipWaterfallTier(
                    "carry",
                    "Residual carried-interest split",
                    [
                        new PartnershipWaterfallAllocationRule("limited-partner", 0.80m),
                        new PartnershipWaterfallAllocationRule("general-partner", 0.10m),
                    ]),
            ]);

        var act = () => PartnershipWaterfallProjector.Project(input);

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*allocation percentages must sum to 1.000000*");
    }

    private static JournalEntry BuildGeneratedBalancedJournal(
        int lineCountSeed,
        int amountSeed,
        int accountSeed,
        DateTimeOffset timestamp,
        bool reverse)
    {
        var journalId = DeterministicGuid("journal", lineCountSeed, amountSeed, accountSeed);
        var lines = BuildGeneratedBalancedLines(journalId, timestamp, lineCountSeed, amountSeed, accountSeed);
        if (reverse)
            Array.Reverse(lines);

        return new JournalEntry(journalId, timestamp, "generated balanced journal", lines);
    }

    private static LedgerEntry[] BuildGeneratedBalancedLines(
        Guid journalId,
        DateTimeOffset timestamp,
        int lineCountSeed,
        int amountSeed,
        int accountSeed)
    {
        var pairCount = (int)BoundedLong(lineCountSeed, 1, 16);
        var lines = new List<LedgerEntry>(pairCount * 2);

        for (var index = 0; index < pairCount; index++)
        {
            var amount = BoundedLong(HashCode.Combine(amountSeed, index), 1, 1_000_000) / 100m;
            var debitAccount = new LedgerAccount(
                $"Generated Asset {BoundedLong(HashCode.Combine(accountSeed, index), 1, 5)}",
                LedgerAccountType.Asset);
            var creditAccount = new LedgerAccount(
                $"Generated Revenue {BoundedLong(HashCode.Combine(accountSeed, ~index), 1, 5)}",
                LedgerAccountType.Revenue);

            lines.Add(new LedgerEntry(
                DeterministicGuid("debit", lineCountSeed, amountSeed, accountSeed, index),
                journalId,
                timestamp,
                debitAccount,
                amount,
                0m,
                "generated balanced journal"));
            lines.Add(new LedgerEntry(
                DeterministicGuid("credit", lineCountSeed, amountSeed, accountSeed, index),
                journalId,
                timestamp,
                creditAccount,
                0m,
                amount,
                "generated balanced journal"));
        }

        return lines.ToArray();
    }

    private static long BoundedLong(int seed, long min, long max)
    {
        var range = (ulong)(max - min + 1);
        return min + (long)((uint)seed % range);
    }

    private static Guid DeterministicGuid(string scope, params int[] seeds)
    {
        unchecked
        {
            var h0 = (uint)StringComparer.Ordinal.GetHashCode(scope);
            var h1 = 0x9E3779B9u;
            var h2 = 0x85EBCA6Bu;
            var h3 = 0xC2B2AE35u;

            foreach (var seed in seeds)
            {
                var value = (uint)seed;
                h0 = (h0 ^ value) * 16777619u;
                h1 = (h1 + value) * 2246822519u;
                h2 = (h2 ^ (value << 13 | value >> 19)) * 3266489917u;
                h3 = (h3 + (value ^ h0)) * 668265263u;
            }

            var bytes = new byte[16];
            BitConverter.GetBytes(h0).CopyTo(bytes, 0);
            BitConverter.GetBytes(h1).CopyTo(bytes, 4);
            BitConverter.GetBytes(h2).CopyTo(bytes, 8);
            BitConverter.GetBytes(h3).CopyTo(bytes, 12);

            return new Guid(bytes);
        }
    }
}
