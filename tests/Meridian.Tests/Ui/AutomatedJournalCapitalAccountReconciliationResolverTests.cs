using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.FinancialOperations.PrivateCapital;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class AutomatedJournalCapitalAccountReconciliationResolverTests
{
    private static readonly Guid BookId = Guid.Parse("ad197f30-e086-4b23-a3e6-e96d56011627");
    private static readonly Guid MayPeriodId = Guid.Parse("10000000-0000-0000-0000-000000000005");
    private static readonly Guid JunePeriodId = Guid.Parse("10000000-0000-0000-0000-000000000006");
    private static readonly Guid JulyPeriodId = Guid.Parse("10000000-0000-0000-0000-000000000007");
    private static readonly DateTimeOffset EvaluatedAt = new(2026, 8, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ResolveAsync_ExactRetainedLedgerScope_DerivesNavCapitalAndDeterministicHighWaterHistory()
    {
        var records = new[]
        {
            CapitalRecord(MayPeriodId, new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero), 1_050_000m, 1),
            CapitalRecord(JunePeriodId, new DateTimeOffset(2026, 6, 30, 12, 0, 0, TimeSpan.Zero), -50_000m, 2),
            CapitalRecord(JulyPeriodId, new DateTimeOffset(2026, 7, 31, 8, 0, 0, TimeSpan.Zero), 100_000m, 3),
            CapitalRecord(
                JulyPeriodId,
                new DateTimeOffset(2026, 7, 31, 9, 0, 0, TimeSpan.Zero),
                999_000m,
                4,
                entityId: "entity-other",
                includeEvidence: false)
        };
        var (resolver, store, _) = CreateResolver(records);

        var first = await resolver.ResolveAsync(Scope());
        var second = await resolver.ResolveAsync(Scope() with { EvaluatedAtUtc = EvaluatedAt.AddMinutes(30) });

        first.Should().NotBeNull();
        first!.IsReconciled.Should().BeTrue();
        first.ReconciledBeginningNav.Should().Be(1_000_000m);
        first.ReconciledEndingNavBeforeFees.Should().Be(1_100_000m);
        first.ReconciledHighWaterMark.Should().Be(1_050_000m);
        first.CapitalAccountOpeningBalance.Should().Be(1_000_000m);
        first.CapitalAccountEndingBalanceBeforeFees.Should().Be(1_100_000m);
        first.CapitalAccountHighWaterMark.Should().Be(1_050_000m);
        first.ReviewedBy.Should().Be("fund-controller");
        first.ConfidenceScore.Should().Be(0.95m);
        first.EvidenceLinks.Should().HaveCount(6);
        first.SourceVersion.Should().MatchRegex("^[0-9a-f]{64}$");
        second!.SourceVersion.Should().Be(first.SourceVersion);

        await store.Received(2).QueryAsync(
            Arg.Is<LedgerJournalEntryQuery>(query =>
                query.LedgerBookId == BookId &&
                query.LineDimensions != null &&
                query.LineDimensions.FundId == "fund-alpha" &&
                query.LineDimensions.EntityId == "entity-alpha" &&
                query.LineDimensions.BookId == BookId.ToString("D") &&
                query.OccurredTo == new DateTimeOffset(
                    new DateOnly(2026, 7, 31).ToDateTime(TimeOnly.MaxValue),
                    TimeSpan.Zero)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_HistoricalNavAndCapitalHighWaterMarksDiverge_BlocksReconciliation()
    {
        var records = new[]
        {
            CapitalRecord(MayPeriodId, new DateTimeOffset(2026, 5, 31, 10, 0, 0, TimeSpan.Zero), 1_050_000m, 1),
            OtherEquityRecord(MayPeriodId, new DateTimeOffset(2026, 5, 31, 11, 0, 0, TimeSpan.Zero), 150_000m, 2),
            OtherEquityRecord(JunePeriodId, new DateTimeOffset(2026, 6, 30, 10, 0, 0, TimeSpan.Zero), -150_000m, 3),
            CapitalRecord(JunePeriodId, new DateTimeOffset(2026, 6, 30, 11, 0, 0, TimeSpan.Zero), -50_000m, 4),
            CapitalRecord(JulyPeriodId, new DateTimeOffset(2026, 7, 31, 8, 0, 0, TimeSpan.Zero), 100_000m, 5)
        };
        var (resolver, _, _) = CreateResolver(records);

        var result = await resolver.ResolveAsync(Scope());

        result.Should().NotBeNull();
        result!.ReconciledBeginningNav.Should().Be(result.CapitalAccountOpeningBalance);
        result.ReconciledEndingNavBeforeFees.Should().Be(result.CapitalAccountEndingBalanceBeforeFees);
        result.ReconciledHighWaterMark.Should().Be(1_200_000m);
        result.CapitalAccountHighWaterMark.Should().Be(1_050_000m);
        result.IsReconciled.Should().BeFalse();
        result.ConfidenceScore.Should().Be(0.50m);
    }

    [Fact]
    public async Task ResolveAsync_MissingRetainedEvidence_FailsClosed()
    {
        var records = new[]
        {
            CapitalRecord(MayPeriodId, new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero), 1_050_000m, 1),
            CapitalRecord(JunePeriodId, new DateTimeOffset(2026, 6, 30, 12, 0, 0, TimeSpan.Zero), -50_000m, 2, includeEvidence: false),
            CapitalRecord(JulyPeriodId, new DateTimeOffset(2026, 7, 31, 8, 0, 0, TimeSpan.Zero), 100_000m, 3)
        };
        var (resolver, _, _) = CreateResolver(records);

        var result = await resolver.ResolveAsync(Scope());

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_CommandlessLegacyRecordsWithGenericEvidence_FailClosed()
    {
        var records = new[]
        {
            CapitalRecord(
                MayPeriodId,
                new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero),
                1_050_000m,
                1,
                governanceMode: GovernanceMode.None),
            CapitalRecord(
                JunePeriodId,
                new DateTimeOffset(2026, 6, 30, 12, 0, 0, TimeSpan.Zero),
                -50_000m,
                2,
                governanceMode: GovernanceMode.None),
            CapitalRecord(
                JulyPeriodId,
                new DateTimeOffset(2026, 7, 31, 8, 0, 0, TimeSpan.Zero),
                100_000m,
                3,
                governanceMode: GovernanceMode.None)
        };
        var (resolver, _, _) = CreateResolver(records);

        var result = await resolver.ResolveAsync(Scope());

        result.Should().BeNull("generic retained support does not prove governed ledger approval or certification");
    }

    [Fact]
    public async Task ResolveAsync_ApprovedAdjustmentGovernance_DerivesReadyFeeBasisFromActualApprover()
    {
        var records = new[]
        {
            CapitalRecord(
                MayPeriodId,
                new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero),
                1_050_000m,
                1,
                governanceMode: GovernanceMode.AdjustmentApproval),
            CapitalRecord(
                JunePeriodId,
                new DateTimeOffset(2026, 6, 30, 12, 0, 0, TimeSpan.Zero),
                -50_000m,
                2,
                governanceMode: GovernanceMode.AdjustmentApproval),
            CapitalRecord(
                JulyPeriodId,
                new DateTimeOffset(2026, 7, 31, 8, 0, 0, TimeSpan.Zero),
                100_000m,
                3,
                governanceMode: GovernanceMode.AdjustmentApproval)
        };
        var (resolver, _, _) = CreateResolver(records);

        var result = await resolver.ResolveAsync(Scope());

        result.Should().NotBeNull();
        result!.IsReconciled.Should().BeTrue();
        result.ConfidenceScore.Should().Be(0.95m);
        result.ReviewedBy.Should().Be("capital-account-certifier");
        result.ReviewedAtUtc.Should().Be(records.Max(static record => record.AdjustmentApproval!.ApprovedAt));
    }

    [Fact]
    public async Task ResolveAsync_MissingServerOwnedCompanyAuthority_FailsClosedBeforeLedgerRead()
    {
        var (resolver, store, tenancyRegistry) = CreateResolver([]);
        tenancyRegistry.ResolveAsync("fund-alpha", Arg.Any<CancellationToken>())
            .Returns(new FundProfileOwnership("fund-alpha", "tenant-alpha", CompanyId: null));

        var result = await resolver.ResolveAsync(Scope());

        result.Should().BeNull();
        await store.DidNotReceive().GetLedgerBookAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_MissingRequiredAuthorityScope_FailsClosedBeforeOwnershipLookup()
    {
        var (resolver, _, tenancyRegistry) = CreateResolver([]);
        var invalidScopes = new[]
        {
            Scope() with { TenantId = " " },
            Scope() with { CompanyId = " " },
            Scope() with { FundProfileId = " " },
            Scope() with { LedgerBookId = Guid.Empty },
            Scope() with { EntityId = " " },
            Scope() with { PeriodId = " " },
            Scope() with { Currency = " " },
            Scope() with { EvaluatedAtUtc = default }
        };

        foreach (var invalidScope in invalidScopes)
        {
            var result = await resolver.ResolveAsync(invalidScope);
            result.Should().BeNull();
        }

        await tenancyRegistry.DidNotReceive()
            .ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static (
        LedgerCapitalAccountReconciliationResolver Resolver,
        ILedgerJournalStore Store,
        IFundProfileTenancyRegistry TenancyRegistry) CreateResolver(
            IReadOnlyList<LedgerJournalEntryRecord> records)
    {
        var store = Substitute.For<ILedgerJournalStore>();
        var tenancyRegistry = Substitute.For<IFundProfileTenancyRegistry>();
        tenancyRegistry.ResolveAsync("fund-alpha", Arg.Any<CancellationToken>())
            .Returns(new FundProfileOwnership("fund-alpha", "tenant-alpha", "company-alpha"));
        store.GetLedgerBookAsync(BookId, Arg.Any<CancellationToken>())
            .Returns(Book());
        store.ListPeriodsAsync(
                BookId,
                null,
                "fund-alpha",
                null,
                Arg.Any<CancellationToken>())
            .Returns(Periods());
        store.QueryAsync(Arg.Any<LedgerJournalEntryQuery>(), Arg.Any<CancellationToken>())
            .Returns(records);

        return (
            new LedgerCapitalAccountReconciliationResolver(store, tenancyRegistry),
            store,
            tenancyRegistry);
    }

    private static AutomatedJournalCapitalAccountReconciliationScope Scope()
        => new(
            "tenant-alpha",
            "company-alpha",
            "fund-alpha",
            BookId,
            "entity-alpha",
            "2026-07",
            "USD",
            EvaluatedAt);

    private static LedgerBookRecord Book()
        => new(
            BookId,
            "fund-alpha",
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            FundStructureNodeKindDto.Fund,
            "Fund alpha primary book",
            "USD",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 31, 23, 0, 0, TimeSpan.Zero),
            AccountingPolicyId: "private-capital-policy",
            AccountingPolicyVersion: "2026.1");

    private static IReadOnlyList<LedgerAccountingPeriod> Periods()
        =>
        [
            Period(MayPeriodId, 5, "SoftClosed", version: 2),
            Period(JunePeriodId, 6, "SoftClosed", version: 3),
            Period(JulyPeriodId, 7, "Open", version: 1)
        ];

    private static LedgerAccountingPeriod Period(Guid id, int month, string status, long version)
        => new(
            id,
            BookId,
            2026,
            month,
            $"2026-{month:D2}",
            new DateOnly(2026, month, 1),
            new DateOnly(2026, month, DateTime.DaysInMonth(2026, month)),
            status,
            new DateTimeOffset(2026, month, 1, 0, 0, 0, TimeSpan.Zero),
            string.Equals(status, "Open", StringComparison.OrdinalIgnoreCase)
                ? null
                : new DateTimeOffset(2026, month, DateTime.DaysInMonth(2026, month), 23, 0, 0, TimeSpan.Zero),
            version);

    private static LedgerJournalEntryRecord CapitalRecord(
        Guid periodId,
        DateTimeOffset timestamp,
        decimal capitalDelta,
        long globalSequence,
        string entityId = "entity-alpha",
        bool includeEvidence = true,
        GovernanceMode governanceMode = GovernanceMode.PostingCommand)
        => EquityRecord(
            periodId,
            timestamp,
            capitalDelta,
            globalSequence,
            "Investor Capital",
            entityId,
            capitalAccountId: "capital-account-alpha",
            investorId: "investor-alpha",
            includeEvidence: includeEvidence,
            governanceMode: governanceMode);

    private static LedgerJournalEntryRecord OtherEquityRecord(
        Guid periodId,
        DateTimeOffset timestamp,
        decimal equityDelta,
        long globalSequence)
        => EquityRecord(
            periodId,
            timestamp,
            equityDelta,
            globalSequence,
            "Other Equity",
            "entity-alpha",
            capitalAccountId: null,
            investorId: null,
            includeEvidence: true,
            governanceMode: GovernanceMode.PostingCommand);

    private static LedgerJournalEntryRecord EquityRecord(
        Guid periodId,
        DateTimeOffset timestamp,
        decimal equityDelta,
        long globalSequence,
        string equityAccountName,
        string entityId,
        string? capitalAccountId,
        string? investorId,
        bool includeEvidence,
        GovernanceMode governanceMode)
    {
        var journalEntryId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        var approvalId = $"capital-ledger-approval-{globalSequence}";
        var magnitude = decimal.Abs(equityDelta);
        var description = equityDelta >= 0m ? $"{equityAccountName} increase" : $"{equityAccountName} decrease";
        var dimensions = new LedgerLineDimensionSet(
            FundId: "fund-alpha",
            EntityId: entityId,
            BookId: BookId.ToString("D"));
        var equityDimensions = dimensions with
        {
            CapitalAccountId = capitalAccountId,
            InvestorId = investorId
        };
        var lines = equityDelta >= 0m
            ? new[]
            {
                new LedgerEntry(Guid.NewGuid(), journalEntryId, timestamp, new LedgerAccount("Cash", LedgerAccountType.Asset), magnitude, 0m, description, dimensions),
                new LedgerEntry(Guid.NewGuid(), journalEntryId, timestamp, new LedgerAccount(equityAccountName, LedgerAccountType.Equity), 0m, magnitude, description, equityDimensions)
            }
            : new[]
            {
                new LedgerEntry(Guid.NewGuid(), journalEntryId, timestamp, new LedgerAccount(equityAccountName, LedgerAccountType.Equity), magnitude, 0m, description, equityDimensions),
                new LedgerEntry(Guid.NewGuid(), journalEntryId, timestamp, new LedgerAccount("Cash", LedgerAccountType.Asset), 0m, magnitude, description, dimensions)
            };
        var sourceEvidenceUri = $"evidence://ledger/{journalEntryId:D}";
        var evidence = new List<JournalEvidenceReference>();
        if (includeEvidence)
        {
            evidence.Add(new JournalEvidenceReference(
                $"evidence-{globalSequence}",
                sourceEvidenceUri,
                "capital-account-support",
                "ledger",
                timestamp.AddSeconds(30),
                "fund-controller",
                SubjectId: journalEntryId.ToString("D"),
                ContentHash: $"sha256:{journalEntryId:N}"));
            if (governanceMode == GovernanceMode.PostingCommand)
            {
                evidence.Add(new JournalEvidenceReference(
                    approvalId,
                    $"approval://accounting-posting/{approvalId}",
                    AccountingPostingEvidenceKindDto.Approval.ToString(),
                    "FinancialOperations",
                    timestamp.AddMinutes(1),
                    "fund-controller",
                    SubjectId: journalEntryId.ToString("D"),
                    ContentHash: $"sha256:{commandId:N}{commandId:N}"));
            }
        }

        IReadOnlyDictionary<string, string>? tags = governanceMode == GovernanceMode.PostingCommand
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["postingCommandId"] = commandId.ToString("D"),
                ["approvalState"] = AccountingPostingApprovalStateDto.Approved.ToString(),
                ["approvalId"] = approvalId,
                ["postingCommandFingerprint"] = $"sha256:{commandId:N}{commandId:N}"
            }
            : null;
        var entry = new JournalEntry(
            journalEntryId,
            timestamp,
            description,
            lines,
            new JournalEntryMetadata(
                FundEventId: $"fund-event-{globalSequence}",
                FundEventType: equityAccountName,
                CapitalAccountId: capitalAccountId,
                InvestorId: investorId,
                Tags: tags,
                EvidenceReferences: evidence));

        var adjustmentApproval = governanceMode == GovernanceMode.AdjustmentApproval
            ? new LedgerAdjustmentApprovalMetadataDto(
                approvalId,
                LedgerAdjustmentApprovalStatusDto.Approved,
                "capital-account-certifier",
                timestamp.AddMinutes(1),
                "capital-account-ledger-certification",
                GovernanceCaseId: $"capital-account-governance-{globalSequence}",
                EvidenceLink: sourceEvidenceUri)
            : null;

        return new LedgerJournalEntryRecord(
            entry,
            AggregateId: Guid.Parse("30000000-0000-0000-0000-000000000001"),
            PeriodId: periodId,
            CommandId: governanceMode == GovernanceMode.PostingCommand ? commandId : null,
            CorrelationId: null,
            GlobalSequence: globalSequence,
            CreatedAt: timestamp.AddMinutes(2),
            AccountingPolicyId: "private-capital-policy",
            AccountingPolicyVersion: "2026.1",
            PostingKind: governanceMode == GovernanceMode.AdjustmentApproval
                ? LedgerPostingKindDto.Adjustment
                : LedgerPostingKindDto.Originating,
            AdjustmentApproval: adjustmentApproval);
    }

    private enum GovernanceMode
    {
        None = 0,
        PostingCommand = 1,
        AdjustmentApproval = 2
    }
}
