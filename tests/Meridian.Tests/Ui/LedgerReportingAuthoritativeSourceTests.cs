using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Services;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Reporting;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Meridian.Tests.Storage;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class LedgerReportingAuthoritativeSourceTests
{
    private static readonly DateOnly AsOfDate = new(2026, 7, 15);
    private static readonly DateTimeOffset CutoffUtc = new(
        AsOfDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc),
        TimeSpan.Zero);

    [Fact]
    public async Task CaptureAsync_AppliesExactAsOfAndDimensions_FiltersMixedLegs_ExcludesFutureJournal_AndIsStable()
    {
        var fixture = CreateFixture();
        var included = Record(
            fixture,
            new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero),
            11,
            debitCostCenterId: "cost-center-a",
            creditCostCenterId: "cost-center-b");
        var future = Record(fixture, new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero), 12);
        fixture.JournalStore.Records.AddRange([included, future]);

        var first = await fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access);
        var second = await fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access);

        first.DatasetRows.Should().ContainSingle();
        first.DatasetRows.Should().OnlyContain(row => row["journalEntryId"] == included.Entry.JournalEntryId.ToString("D"));
        first.DatasetRows[0]["currency"].Should().Be("USD");
        first.DatasetRows[0].Should().NotContainKey("transactionCurrency");
        first.DatasetRows[0].Should().NotContainKey("accountId");
        first.Checkpoint.JournalEntryCount.Should().Be(1);
        first.Checkpoint.HighestGlobalSequence.Should().Be(11);
        first.Checkpoint.CheckpointId.Should().Be(second.Checkpoint.CheckpointId);
        first.Checkpoint.CheckpointHash.Should().Be(second.Checkpoint.CheckpointHash);

        fixture.JournalStore.LastQuery.Should().NotBeNull();
        fixture.JournalStore.LastQuery!.OccurredTo.Should().Be(CutoffUtc);
        fixture.JournalStore.LastQuery.LedgerBookId.Should().Be(fixture.Book.LedgerBookId);
        fixture.JournalStore.LastQuery.PeriodId.Should().BeNull(
            "period activity is derived from the same retained historical population as opening balances");
        fixture.JournalStore.LastQuery.LineDimensions.Should().BeEquivalentTo(new LedgerLineDimensionSet(
            fixture.FundId,
            OrganizationId: fixture.OrganizationId.ToString("D"),
            BookId: fixture.Book.LedgerBookId.ToString("D")));
        fixture.LastStructureQuery.Should().NotBeNull();
        fixture.LastStructureQuery!.ActiveOnly.Should().BeTrue();
        fixture.LastStructureQuery.AsOf.Should().Be(CutoffUtc);
        fixture.LastStructureQuery.NodeId.Should().BeNull();
    }

    [Fact]
    public async Task CaptureAsync_RetainsExactAccrualAccountCurrencyAndJournalEvidenceInCheckpoint()
    {
        var fixture = CreateFixture();
        var accrual = IncomeAccrualRecord(fixture);
        fixture.JournalStore.Records.Add(accrual);

        var capture = await fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access);
        var incomeLine = accrual.Entry.Lines.Single(line => line.Account.AccountType == LedgerAccountType.Revenue);
        var row = capture.DatasetRows.Single(value => value["entryId"] == incomeLine.EntryId.ToString("D"));

        row["journalEntryId"].Should().Be(accrual.Entry.JournalEntryId.ToString("D"));
        row["sourceEventId"].Should().Be(accrual.SourceEventId!.Value.ToString("D"));
        row["sourceJournalEntryId"].Should().Be(accrual.SourceJournalEntryId!.Value.ToString("D"));
        row["account"].Should().Be(LedgerAccounts.CouponIncome.Name);
        row["accountType"].Should().Be("Revenue");
        row["financialAccountId"].Should().Be("brokerage-a");
        row["accountId"].Should().Be("retained-account-a");
        row["currency"].Should().Be("USD", "functional amounts must not be labelled with the transaction currency");
        row["credit"].Should().Be("125");
        row["netAmount"].Should().Be("-125");
        row["transactionCurrency"].Should().Be("EUR");
        row["transactionDebit"].Should().Be("0");
        row["transactionCredit"].Should().Be("100");
        row["fxRateToFunctional"].Should().Be("1.25");
        row["activityType"].Should().Be("CouponAccrual");
        row["accountingPolicyId"].Should().Be("coupon-policy");
        row["accountingPolicyVersion"].Should().Be("2");
        row["recordedAtUtc"].Should().Be(accrual.CreatedAt.ToString("O"));
        row["timestampUtc"].Should().Be(accrual.Entry.Timestamp.ToString("O"));

        fixture.JournalStore.Records[0] = accrual with { CreatedAt = accrual.CreatedAt.AddMinutes(1) };
        var recaptured = await fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access);

        recaptured.Checkpoint.CheckpointHash.Should().NotBe(capture.Checkpoint.CheckpointHash,
            "the checkpoint must bind retained journal recording metadata as well as its amount");
        row["recordedAtUtc"].Should().Be(accrual.CreatedAt.ToString("O"),
            "a captured explanation must retain the original source facts after the source changes");
    }

    [Fact]
    public async Task CaptureAsync_RejectsFunctionalCurrencyThatDisagreesWithCertifiedBook()
    {
        var fixture = CreateFixture();
        fixture.JournalStore.Records.Add(IncomeAccrualRecord(fixture, functionalCurrency: "GBP"));

        var capture = () => fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access).AsTask();

        await capture.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>()
            .WithMessage("*functional currency 'GBP'*certified book currency 'USD'*");
    }

    [Fact]
    public async Task CaptureAsync_RejectsHistoricalFunctionalCurrencyThatDisagreesWithCertifiedBook()
    {
        var fixture = CreateFixture();
        fixture.JournalStore.Records.Add(IncomeAccrualRecord(fixture, functionalCurrency: "GBP") with
        {
            PeriodId = Guid.NewGuid()
        });
        fixture.JournalStore.Records.Add(Record(
            fixture, new DateTimeOffset(2026, 7, 10, 13, 0, 0, TimeSpan.Zero), 12));

        var capture = () => fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access).AsTask();

        await capture.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>()
            .WithMessage("*functional currency 'GBP'*certified book currency 'USD'*");
    }

    [Fact]
    public async Task CaptureAsync_WithRealFundStructureService_RetainsAncestorHierarchyAndExcludesFutureOrganization()
    {
        var fixture = CreateFixture();
        var structure = new InMemoryFundStructureService(new InMemoryFundAccountService());
        var effectiveFrom = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var businessId = Guid.NewGuid();
        await structure.CreateOrganizationAsync(new CreateOrganizationRequest(
            fixture.OrganizationId,
            "REPORTING-ORG",
            "Reporting organization",
            "USD",
            effectiveFrom,
            "test"));
        await structure.CreateBusinessAsync(new CreateBusinessRequest(
            businessId,
            fixture.OrganizationId,
            BusinessKindDto.FundManager,
            "REPORTING-MANAGER",
            "Reporting manager",
            "USD",
            effectiveFrom,
            "test"));
        await structure.CreateFundAsync(new CreateFundRequest(
            fixture.FundNodeId,
            "REPORTING-FUND",
            "Reporting fund",
            "USD",
            effectiveFrom,
            "test",
            BusinessId: businessId));

        // A second hierarchy that becomes effective after the report cut must not become a
        // competing ancestor or influence the certified checkpoint.
        var futureOrganizationId = Guid.NewGuid();
        await structure.CreateOrganizationAsync(new CreateOrganizationRequest(
            futureOrganizationId,
            "FUTURE-ORG",
            "Future organization",
            "USD",
            CutoffUtc.AddTicks(1),
            "test"));

        var tenancy = Substitute.For<IFundProfileTenancyRegistry>();
        tenancy.ResolveAsync(fixture.FundId, Arg.Any<CancellationToken>())
            .Returns(new FundProfileOwnership(
                fixture.FundId,
                fixture.TenantId,
                fixture.CompanyId));
        fixture.Source = new LedgerReportingAuthoritativeSource(
            fixture.JournalStore,
            tenancy,
            structure,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 15, 20, 0, 0, TimeSpan.Zero)));
        fixture.JournalStore.Records.Add(Record(
            fixture,
            new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero),
            11));

        var capture = await fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access);

        capture.Checkpoint.OrganizationId.Should().Be(fixture.OrganizationId.ToString("D"));
        capture.Checkpoint.CompanyId.Should().Be(fixture.CompanyId);
        capture.Checkpoint.FundId.Should().Be(fixture.FundId);
        capture.Checkpoint.EvidenceIds.Should().NotContain(reference =>
            reference.Contains(futureOrganizationId.ToString("D"), StringComparison.Ordinal));
    }

    [Fact]
    public async Task CaptureAsync_FinalReportRejectsSoftClosedPeriod()
    {
        var fixture = CreateFixture(periodStatus: "SoftClosed");
        fixture.JournalStore.Records.Add(Record(
            fixture,
            new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero),
            11,
            debitCostCenterId: "cost-center-a",
            creditCostCenterId: "cost-center-a"));

        Func<Task> capture = async () =>
            await fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access);

        await capture.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>()
            .WithMessage("*SoftClosed*not HardClosed*");
    }

    [Fact]
    public async Task CaptureAsync_FinalReporting_ValidatesPeriodStateRetainedWithPopulation()
    {
        var fixture = CreateFixture();
        fixture.JournalStore.Records.Add(Record(
            fixture, new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero), 11));
        fixture.JournalStore.SnapshotPeriod = fixture.Period with
        {
            Status = "Open",
            Version = fixture.Period.Version + 1
        };

        var capture = () => fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access).AsTask();

        await capture.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>()
            .WithMessage("*Open*not HardClosed*");
    }

    [Fact]
    public async Task CaptureAsync_FailsClosedWhenStoreReturnsLineOutsideRequestedDimension()
    {
        var fixture = CreateFixture();
        fixture.JournalStore.ApplyQueryFilters = false;
        fixture.JournalStore.Records.Add(Record(
            fixture,
            new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero),
            11,
            fundId: "another-fund"));

        Func<Task> capture = async () =>
            await fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access);

        await capture.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>()
            .WithMessage("*dimension*does not match certified value*");
    }

    [Fact]
    public async Task CaptureAsync_FailsClosedWhenSelectedEntityIsOnlyEffectiveAfterAsOfCut()
    {
        var fixture = CreateFixture();
        var futureEntityId = Guid.NewGuid();
        fixture.Parameters = fixture.Parameters with
        {
            Scope = fixture.Parameters.Scope with
            {
                EntityScopeKind = ReportingEntityScopeKindDto.Entity,
                EntityId = futureEntityId.ToString("D")
            },
            ConsolidationLevel = ReportingConsolidationLevelDto.Entity
        };

        Func<Task> capture = async () =>
            await fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access);

        await capture.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>()
            .WithMessage("*not present in the authoritative as-of structure graph*");
        fixture.LastStructureQuery.Should().Match<OrganizationStructureQuery>(query =>
            query.ActiveOnly && query.AsOf == CutoffUtc);
    }

    [Theory]
    [InlineData(ReportingOutputFormatDto.Pdf)]
    [InlineData(ReportingOutputFormatDto.Xlsx)]
    [InlineData(ReportingOutputFormatDto.ClientPackage)]
    public async Task CaptureAsync_CapitalAccountPrimaryDocument_ShouldBindPartnersCapitalToCompleteAsOfLedgerHistory(
        ReportingOutputFormatDto outputFormat)
    {
        var fixture = CreateFixture();
        fixture.Parameters = fixture.Parameters with
        {
            OutputFormat = outputFormat
        };
        var priorPeriodId = Guid.NewGuid();
        fixture.JournalStore.Records.Add(
            Record(
                fixture,
                new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero),
                10,
                debitCostCenterId: "cost-center-a",
                creditCostCenterId: "cost-center-a") with
            {
                PeriodId = priorPeriodId
            });
        fixture.JournalStore.Records.Add(Record(
            fixture,
            new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero),
            11,
            debitCostCenterId: "cost-center-a",
            creditCostCenterId: "cost-center-a"));

        var templateNeutralCapture =
            await fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access);
        var capture = await fixture.Source.CaptureAsync(
            fixture.Parameters,
            fixture.Access,
            new ReportingAuthoritativeSourceCaptureIntent("capital-account-statement"));

        capture.DatasetRows.Should().HaveCount(2);
        capture.Checkpoint.CheckpointId.Should().Be(templateNeutralCapture.Checkpoint.CheckpointId);
        capture.Checkpoint.CheckpointHash.Should().Be(
            templateNeutralCapture.Checkpoint.CheckpointHash,
            "the durable source checkpoint must remain independent of the output template");
        capture.CertifiedLedgerPresentation.Should().NotBeNull();
        var presentation = capture.CertifiedLedgerPresentation!;
        presentation.SourceCheckpointId.Should().Be(capture.Checkpoint.CheckpointId);
        presentation.SourceCheckpointHash.Should().Be(capture.Checkpoint.CheckpointHash);
        var partnersCapital = presentation.ReportPack.Statements.PartnersCapital;
        partnersCapital.Should().NotBeNull();
        partnersCapital!.BeginningCapital.Should().Be(125m);
        partnersCapital.EndingCapital.Should().Be(250m);
        partnersCapital.IsReconciled.Should().BeTrue();
        presentation.ReportPack.IsBalanced.Should().BeTrue();
        capture.Checkpoint.EvidenceIds.Should().Contain(
            $"ledger-report-pack:{presentation.ReportPack.Request.ReportId}:{presentation.ReportPack.Signature.PayloadChecksumSha256}");
        capture.Checkpoint.EvidenceIds.Should().ContainSingle(reference =>
            reference.StartsWith("ledger-report-pack:", StringComparison.Ordinal));
        fixture.JournalStore.LastQuery.Should().NotBeNull();
        fixture.JournalStore.LastQuery!.PeriodId.Should().BeNull(
            "the presentation must include retained history before the reporting period");
        fixture.JournalStore.QueryCount.Should().Be(2, "each capture must query the ledger population once");
        fixture.JournalStore.CompleteHistoryQueryCount.Should().Be(2);
    }

    [Fact]
    public async Task CaptureAsync_ConcurrentBackdatedPosting_AllCertifiedComponentsUseOnePopulation()
    {
        var fixture = CreateFixture();
        fixture.Parameters = fixture.Parameters with { OutputFormat = ReportingOutputFormatDto.ClientPackage };
        var priorPeriod = Guid.NewGuid();
        var openingCapital = Record(fixture, new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero), 10)
            with
        { PeriodId = priorPeriod };
        var periodCapital = Record(fixture, new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero), 11);
        var periodIncome = IncomeAccrualRecord(fixture) with { GlobalSequence = 12 };
        var originalPopulation = new[] { openingCapital, periodCapital, periodIncome };
        fixture.JournalStore.Records.AddRange(originalPopulation);
        var captured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.JournalStore.AfterFirstQuerySnapshot = async () =>
        {
            captured.SetResult();
            await resume.Task;
        };
        var intent = new ReportingAuthoritativeSourceCaptureIntent("capital-account-statement");

        var captureTask = fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access, intent).AsTask();
        await captured.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var lateOpeningCapital = Record(fixture, openingCapital.Entry.Timestamp, 13) with { PeriodId = priorPeriod };
        var latePeriodCapital = Record(fixture, periodCapital.Entry.Timestamp.AddHours(1), 14);
        var latePeriodIncome = IncomeAccrualRecord(fixture) with { GlobalSequence = 15 };
        fixture.JournalStore.Records.Add(lateOpeningCapital);
        await fixture.JournalStore.AppendAsync(new LedgerJournalEntryWrite(
            latePeriodCapital.Entry,
            latePeriodCapital.AggregateId,
            latePeriodCapital.PeriodId,
            AccountingBasis: latePeriodCapital.AccountingBasis));
        fixture.JournalStore.Records.Add(latePeriodIncome);
        resume.SetResult();
        var capture = await captureTask.WaitAsync(TimeSpan.FromSeconds(10));

        fixture.JournalStore.QueryCount.Should().Be(1,
            "period rows and historical balances must be selected from the same database population");
        capture.DatasetRows.Should().HaveCount(4);
        capture.DatasetRows.Should().OnlyContain(row =>
            row["journalEntryId"] == periodCapital.Entry.JournalEntryId.ToString("D")
            || row["journalEntryId"] == periodIncome.Entry.JournalEntryId.ToString("D"));
        capture.Checkpoint.HighestGlobalSequence.Should().Be(12);
        capture.Checkpoint.LedgerPopulation.Should().NotBeNull();
        capture.Checkpoint.LedgerPopulation!.HighestGlobalSequence.Should().Be(12);
        capture.Checkpoint.LedgerPopulation.JournalEntryCount.Should().Be(3);
        capture.Checkpoint.LedgerPopulation.LedgerLineCount.Should().Be(6);
        var presentation = capture.CertifiedLedgerPresentation!;
        presentation.ReportPack.Statements.PartnersCapital!.BeginningCapital.Should().Be(125m);
        presentation.ReportPack.Statements.PartnersCapital.EndingCapital.Should().Be(375m);
        presentation.ReportPack.Statements.TotalRevenue.Should().Be(125m);
        presentation.ReportPack.Statements.TotalAssets.Should().Be(375m);

        var persistedCheckpoint = JsonSerializer.Deserialize<ReportingAuthoritativeSourceCheckpoint>(
            JsonSerializer.Serialize(capture.Checkpoint))!;
        var retainedSnapshot = ReportingLedgerPopulationSnapshot.Decode(persistedCheckpoint);
        retainedSnapshot.Journals.Select(static record => record.Entry.JournalEntryId).Should().Equal(
            originalPopulation.Select(static record => record.Entry.JournalEntryId));
        retainedSnapshot.Journals[0].PeriodId.Should().Be(priorPeriod);
        var retainedPeriodLines = retainedSnapshot.Journals
            .Where(record => record.PeriodId == fixture.Period.PeriodId)
            .SelectMany(static record => record.Entry.Lines)
            .ToDictionary(static line => line.EntryId.ToString("D"));
        capture.DatasetRows.Select(static row => row["entryId"]).Should().BeEquivalentTo(retainedPeriodLines.Keys);
        foreach (var row in capture.DatasetRows)
        {
            var retainedLine = retainedPeriodLines[row["entryId"]];
            row["journalEntryId"].Should().Be(retainedLine.JournalEntryId.ToString("D"));
            row["debit"].Should().Be(retainedLine.Debit.ToString("G29", CultureInfo.InvariantCulture));
            row["credit"].Should().Be(retainedLine.Credit.ToString("G29", CultureInfo.InvariantCulture));
            row["netAmount"].Should().Be((retainedLine.Debit - retainedLine.Credit).ToString("G29", CultureInfo.InvariantCulture));
        }
        var reproduced = retainedSnapshot.Replay();
        reproduced.Statements.Should().BeEquivalentTo(presentation.ReportPack.Statements);
        reproduced.LineProvenance.Should().BeEquivalentTo(presentation.ReportPack.LineProvenance);
        reproduced.Artifacts.Should().BeEquivalentTo(presentation.ReportPack.Artifacts,
            options => options.WithStrictOrdering());
        reproduced.Signature.Should().Be(presentation.ReportPack.Signature);

        var recaptured = await fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access, intent);
        recaptured.DatasetRows.Should().HaveCount(8);
        recaptured.Checkpoint.CheckpointHash.Should().NotBe(capture.Checkpoint.CheckpointHash);
        recaptured.Checkpoint.HighestGlobalSequence.Should().Be(15);
        recaptured.CertifiedLedgerPresentation!.ReportPack.Statements.PartnersCapital!.BeginningCapital.Should().Be(250m);
        recaptured.CertifiedLedgerPresentation.ReportPack.Statements.PartnersCapital.EndingCapital.Should().Be(750m);
        recaptured.CertifiedLedgerPresentation.ReportPack.Statements.TotalRevenue.Should().Be(250m);
        presentation.ReportPack.Statements.TotalRevenue.Should().Be(125m,
            "later posting and recapture must not mutate an already captured report");
        retainedSnapshot.Replay().Signature.Should().Be(presentation.ReportPack.Signature,
            "retained input must reproduce every component after the live journal population changes");
    }

    [Fact]
    public async Task CaptureAsync_HistoricalAmountChange_ChangesCheckpointWithoutChangingPeriodRows()
    {
        var fixture = CreateFixture();
        fixture.Parameters = fixture.Parameters with { OutputFormat = ReportingOutputFormatDto.Pdf };
        var historical = Record(fixture, new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero), 40)
            with
        { PeriodId = Guid.NewGuid() };
        var activity = Record(fixture, new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero), 11);
        fixture.JournalStore.Records.AddRange([historical, activity]);
        var intent = new ReportingAuthoritativeSourceCaptureIntent("capital-account-statement");

        var captured = await fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access, intent);
        var retained = ReportingLedgerPopulationSnapshot.Decode(captured.Checkpoint);
        var changedEntry = new JournalEntry(
            historical.Entry.JournalEntryId,
            historical.Entry.Timestamp,
            historical.Entry.Description,
            historical.Entry.Lines.Select(line => new LedgerEntry(
                line.EntryId,
                line.JournalEntryId,
                line.Timestamp,
                line.Account,
                line.Debit * 2,
                line.Credit * 2,
                line.Description,
                line.Dimensions)).ToArray());
        fixture.JournalStore.Records[0] = historical with { Entry = changedEntry };
        var recaptured = await fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access, intent);

        captured.Checkpoint.HighestGlobalSequence.Should().Be(40,
            "the certified sequence boundary covers historical balance input as well as period activity");
        recaptured.DatasetRows.Should().BeEquivalentTo(captured.DatasetRows);
        recaptured.Checkpoint.CheckpointHash.Should().NotBe(captured.Checkpoint.CheckpointHash);
        recaptured.Checkpoint.LedgerPopulation!.ContentHashSha256.Should().NotBe(
            captured.Checkpoint.LedgerPopulation!.ContentHashSha256);
        captured.CertifiedLedgerPresentation!.ReportPack.Statements.PartnersCapital!.BeginningCapital.Should().Be(125m);
        recaptured.CertifiedLedgerPresentation!.ReportPack.Statements.PartnersCapital!.BeginningCapital.Should().Be(250m);
        retained.Replay().Signature.Should().Be(captured.CertifiedLedgerPresentation.ReportPack.Signature);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("altered")]
    [InlineData("rebound-amount")]
    [InlineData("foreign-scope")]
    public async Task RetainedPopulation_MissingAlteredOrForeignScope_CannotReplayCertifiedReport(string fault)
    {
        var fixture = CreateFixture();
        fixture.JournalStore.Records.Add(Record(
            fixture, new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero), 11));
        var capture = await fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access);
        var checkpoint = capture.Checkpoint;
        if (fault == "missing")
        {
            checkpoint = checkpoint with { LedgerPopulation = null };
        }
        else if (fault == "altered")
        {
            checkpoint = checkpoint with
            {
                LedgerPopulation = checkpoint.LedgerPopulation! with
                {
                    PayloadJson = checkpoint.LedgerPopulation!.PayloadJson + " "
                }
            };
        }
        else
        {
            var snapshot = ReportingLedgerPopulationSnapshot.Decode(checkpoint);
            ReportingLedgerPopulationSnapshot replacement;
            if (fault == "rebound-amount")
            {
                var journal = snapshot.Journals[0];
                var changed = new JournalEntry(journal.Entry.JournalEntryId, journal.Entry.Timestamp,
                    journal.Entry.Description, journal.Entry.Lines.Select(line => new LedgerEntry(
                        line.EntryId, line.JournalEntryId, line.Timestamp, line.Account,
                        line.Debit * 2, line.Credit * 2, line.Description, line.Dimensions)).ToArray());
                replacement = snapshot with
                {
                    Journals = snapshot.Journals.SetItem(0, journal with { Entry = changed })
                };
            }
            else
            {
                replacement = snapshot with { Scope = snapshot.Scope with { FundProfileId = "foreign-fund" } };
            }
            var replacementPopulation = replacement.Retain();
            checkpoint = checkpoint with
            {
                LedgerPopulation = replacementPopulation,
                EvidenceIds = checkpoint.EvidenceIds
                    .Where(static evidence => !evidence.StartsWith("ledger-population:", StringComparison.Ordinal))
                    .Append($"ledger-population:{replacementPopulation.SnapshotId}:{replacementPopulation.ContentHashSha256}")
                    .ToImmutableArray()
            };
        }

        Action replay = () => ReportingLedgerPopulationSnapshot.Decode(checkpoint).Replay();

        replay.Should().Throw<ReportingGovernanceException>();
    }

    [Fact]
    public async Task CaptureAsync_CapitalAccountCsv_DoesNotBuildLedgerDocumentPresentation()
    {
        var fixture = CreateFixture();
        fixture.Parameters = fixture.Parameters with
        {
            OutputFormat = ReportingOutputFormatDto.Csv
        };
        fixture.JournalStore.Records.Add(Record(
            fixture,
            new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero),
            11,
            debitCostCenterId: "cost-center-a",
            creditCostCenterId: "cost-center-a"));

        var capture = await fixture.Source.CaptureAsync(
            fixture.Parameters,
            fixture.Access,
            new ReportingAuthoritativeSourceCaptureIntent("capital-account-statement"));

        capture.CertifiedLedgerPresentation.Should().BeNull();
        capture.Checkpoint.LedgerPopulation.Should().NotBeNull();
        capture.Checkpoint.EvidenceIds.Should().NotContain(reference =>
            reference.StartsWith("ledger-report-pack:", StringComparison.Ordinal));
        fixture.JournalStore.CompleteHistoryQueryCount.Should().Be(1);
    }

    [Fact]
    public async Task CaptureAsync_NonCapitalClientPackage_RetainsHistoryWithoutBuildingCapitalPresentation()
    {
        var fixture = CreateFixture();
        fixture.Parameters = fixture.Parameters with
        {
            OutputFormat = ReportingOutputFormatDto.ClientPackage
        };
        fixture.JournalStore.Records.Add(Record(
            fixture,
            new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero),
            11,
            debitCostCenterId: "cost-center-a",
            creditCostCenterId: "cost-center-a"));

        var capture = await fixture.Source.CaptureAsync(
            fixture.Parameters,
            fixture.Access,
            new ReportingAuthoritativeSourceCaptureIntent("investor-statement"));

        capture.DatasetRows.Should().HaveCount(2);
        capture.CertifiedLedgerPresentation.Should().BeNull();
        capture.Checkpoint.LedgerPopulation.Should().NotBeNull();
        capture.Checkpoint.EvidenceIds.Should().NotContain(reference =>
            reference.StartsWith("ledger-report-pack:", StringComparison.Ordinal));
        fixture.JournalStore.QueryCount.Should().Be(1);
        fixture.JournalStore.CompleteHistoryQueryCount.Should().Be(1);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("scope")]
    [InlineData("basis")]
    public async Task CaptureAsync_CanonicalDisposalEvidenceBlocksAndRestoringItProducesSignedLotEvidence(string fault)
    {
        var fixture = CreateFixture();
        fixture.Parameters = fixture.Parameters with { OutputFormat = ReportingOutputFormatDto.Pdf };
        var lot = CanonicalOpenLotConsumerTests.DurableLot(1) with { LedgerBookId = fixture.Book.LedgerBookId };
        var sourceRecord = Record(fixture, new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero), 11);
        var disposal = CanonicalOpenLotConsumerTests.DisposalJournal(lot);
        var dimensions = sourceRecord.Entry.Lines[0].Dimensions! with
        {
            InstrumentId = lot.SecurityId,
            PositionId = lot.BookPositionId
        };
        var entry = new JournalEntry(disposal.JournalEntryId, disposal.Timestamp, disposal.Description,
            disposal.Lines.Select(line => new LedgerEntry(line.EntryId, line.JournalEntryId, line.Timestamp,
                line.Account, line.Debit, line.Credit, line.Description, dimensions)).ToArray());
        fixture.JournalStore.Records.Add(sourceRecord with { Entry = entry });
        var history = CanonicalOpenLotConsumerTests.History(lot, entry, lot.ToOpenLot());
        fixture.JournalStore.Disposals.Add(fault switch
        {
            "missing" => history with { CanonicalLots = null },
            "scope" => history with { CanonicalLots = [history.CanonicalLots![0] with { BookPositionId = Guid.NewGuid() }] },
            _ => history with { Lots = [history.Lots[0] with { CostBasis = 301m }] }
        });
        var intent = new ReportingAuthoritativeSourceCaptureIntent("capital-account-statement");
        var capture = () => fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access, intent).AsTask();
        await capture.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>().WithMessage("*blocks canonical reporting*");

        fixture.JournalStore.Disposals[0] = history;
        var restored = await fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access, intent);
        var artifact = restored.CertifiedLedgerPresentation!.ReportPack.Artifacts
            .Single(item => item.Name == "canonical-open-lot-evidence.json");
        artifact.Content.Should().Contain(lot.SecurityId.ToString("D"))
            .And.Contain("AcquisitionFxRateToFunctional").And.Contain("1.2")
            .And.Contain("FunctionalCostBasis");
        restored.Checkpoint.EvidenceIds.Should().Contain(reference => reference.StartsWith("ledger-report-pack:", StringComparison.Ordinal));
        var retained = ReportingLedgerPopulationSnapshot.Decode(restored.Checkpoint);
        retained.TaxLotReliefProjections.Should().ContainSingle();
        retained.TaxLotReliefProjections[0].Lines.Sum(static line => line.debit).Should().BeGreaterThan(0m,
            "retained tax-relief tuple amounts must survive payload serialization");
        retained.Replay().Artifacts.Should().BeEquivalentTo(restored.CertifiedLedgerPresentation.ReportPack.Artifacts,
            options => options.WithStrictOrdering());
        retained.Replay().Signature.Should().Be(restored.CertifiedLedgerPresentation.ReportPack.Signature);
    }

    [Fact]
    public async Task CaptureAsync_TaxHistoryChangedAfterSnapshot_UsesRetainedReliefAndBlocksNextCapture()
    {
        var fixture = CreateFixture();
        fixture.Parameters = fixture.Parameters with { OutputFormat = ReportingOutputFormatDto.Pdf };
        var lot = CanonicalOpenLotConsumerTests.DurableLot(1) with { LedgerBookId = fixture.Book.LedgerBookId };
        var sourceRecord = Record(fixture, new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero), 11);
        var disposal = CanonicalOpenLotConsumerTests.DisposalJournal(lot);
        var dimensions = sourceRecord.Entry.Lines[0].Dimensions! with
        {
            InstrumentId = lot.SecurityId,
            PositionId = lot.BookPositionId
        };
        var entry = new JournalEntry(disposal.JournalEntryId, disposal.Timestamp, disposal.Description,
            disposal.Lines.Select(line => new LedgerEntry(line.EntryId, line.JournalEntryId, line.Timestamp,
                line.Account, line.Debit, line.Credit, line.Description, dimensions)).ToArray());
        fixture.JournalStore.Records.Add(sourceRecord with { Entry = entry });
        var history = CanonicalOpenLotConsumerTests.History(lot, entry, lot.ToOpenLot());
        fixture.JournalStore.Disposals.Add(history);
        fixture.JournalStore.AfterFirstQuerySnapshot = () =>
        {
            fixture.JournalStore.Disposals[0] = history with { CanonicalLots = null };
            return Task.CompletedTask;
        };
        var intent = new ReportingAuthoritativeSourceCaptureIntent("capital-account-statement");

        var captured = await fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access, intent);
        var retained = ReportingLedgerPopulationSnapshot.Decode(captured.Checkpoint);

        retained.TaxLotReliefProjections.Should().ContainSingle();
        retained.Replay().Artifacts.Should().BeEquivalentTo(captured.CertifiedLedgerPresentation!.ReportPack.Artifacts,
            options => options.WithStrictOrdering());
        retained.Replay().Signature.Should().Be(captured.CertifiedLedgerPresentation.ReportPack.Signature);
        var recapture = () => fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access, intent).AsTask();
        await recapture.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>()
            .WithMessage("*blocks canonical reporting*");
    }

    [Theory]
    [InlineData("ledger-validation")]
    [InlineData("argument")]
    [InlineData("invalid-operation")]
    [InlineData("json")]
    public async Task CaptureAsync_MalformedSnapshotHistory_ReturnsSourceUnavailableAndCanRetryAfterRepair(string fault)
    {
        var fixture = CreateFixture();
        fixture.JournalStore.Records.Add(Record(
            fixture, new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero), 11));
        const string reason = "Retained tax-lot history is malformed or incomplete.";
        fixture.JournalStore.SnapshotCaptureException = fault switch
        {
            "ledger-validation" => new LedgerValidationException(reason),
            "argument" => new ArgumentException(reason, "retainedHistory"),
            "invalid-operation" => new InvalidOperationException(reason),
            _ => new JsonException(reason)
        };

        var capture = () => fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access).AsTask();

        await capture.Should().ThrowExactlyAsync<ReportingAuthoritativeSourceUnavailableException>()
            .WithMessage($"*snapshot is unavailable*{reason}*");
        fixture.JournalStore.QueryCount.Should().Be(1);
        fixture.JournalStore.SnapshotCaptureException = null;
        var repaired = await fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access);
        repaired.DatasetRows.Should().HaveCount(2);
        ReportingLedgerPopulationSnapshot.Decode(repaired.Checkpoint).Journals.Should().ContainSingle();
    }

    [Theory]
    [InlineData("cancelled")]
    [InlineData("foreign-tenant")]
    [InlineData("already-unavailable")]
    public async Task CaptureAsync_SnapshotAuthorityRejection_PreservesOriginalException(string fault)
    {
        var fixture = CreateFixture();
        fixture.JournalStore.Records.Add(Record(
            fixture, new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero), 11));
        Exception rejection = fault switch
        {
            "cancelled" => new OperationCanceledException("Snapshot capture was cancelled."),
            "foreign-tenant" => new UnauthorizedAccessException("Snapshot belongs to another tenant."),
            _ => new ReportingAuthoritativeSourceUnavailableException("Retained reporting period authority is unavailable.")
        };
        fixture.JournalStore.SnapshotCaptureException = rejection;

        var capture = () => fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access).AsTask();

        var failed = await capture.Should().ThrowAsync<Exception>();
        failed.Which.Should().BeSameAs(rejection);
    }

    private static Fixture CreateFixture(string periodStatus = "HardClosed")
    {
        const string tenantId = "tenant-reporting";
        const string companyId = "company-reporting";
        const string fundId = "fund-reporting";
        var organizationId = Guid.NewGuid();
        var fundNodeId = Guid.NewGuid();
        var bookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 7, 15, 20, 0, 0, TimeSpan.Zero);
        var book = new LedgerBookRecord(
            bookId,
            fundId,
            fundNodeId,
            FundStructureNodeKindDto.Fund,
            "Primary book",
            "USD",
            now,
            now,
            AccountingBasis: AccountingBasisKindDto.Gaap);
        var period = new LedgerAccountingPeriod(
            periodId,
            bookId,
            2026,
            7,
            "2026-07",
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 31),
            periodStatus,
            now,
            now,
            4);
        var journalStore = new QueryFilteringJournalStore(book, period);
        var tenancy = Substitute.For<IFundProfileTenancyRegistry>();
        tenancy.ResolveAsync(fundId, Arg.Any<CancellationToken>())
            .Returns(new FundProfileOwnership(fundId, tenantId, companyId));

        var structure = Substitute.For<IFundStructureService>();
        OrganizationStructureQuery? lastQuery = null;
        var graph = new OrganizationStructureGraphDto(
            [new OrganizationSummaryDto(
                organizationId,
                "ORG",
                "Reporting organization",
                "USD",
                true,
                now.AddYears(-1),
                null,
                [])],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [
                new FundStructureNodeDto(
                    organizationId,
                    FundStructureNodeKindDto.Organization,
                    "ORG",
                    "Reporting organization",
                    null,
                    true,
                    now.AddYears(-1),
                    null),
                new FundStructureNodeDto(
                    fundNodeId,
                    FundStructureNodeKindDto.Fund,
                    "FUND",
                    "Reporting fund",
                    null,
                    true,
                    now.AddYears(-1),
                    null)
            ],
            [new OwnershipLinkDto(
                Guid.NewGuid(),
                organizationId,
                fundNodeId,
                OwnershipRelationshipTypeDto.Owns,
                100m,
                true,
                now.AddYears(-1),
                null,
                null)],
            []);
        structure.GetOrganizationStructureAsync(
                Arg.Do<OrganizationStructureQuery>(query => lastQuery = query),
                Arg.Any<CancellationToken>())
            .Returns(graph);

        var parameters = new ReportingRunParametersDto(
            new ReportingRunScopeDto(
                fundId,
                Dimensions: new LedgerDimensionSetDto(CostCenterId: "cost-center-a")),
            periodId.ToString("D"),
            AsOfDate,
            new ReportingLedgerBookSelectionDto(bookId),
            ReportingAccountingBasisDto.Gaap,
            "USD",
            ReportingConsolidationLevelDto.Fund,
            ReportingOutputFormatDto.Csv,
            ReportingFinalityDto.Final,
            IncludeSupportingSchedules: true,
            IncludeEvidenceAppendix: true);
        var fixture = new Fixture(
            tenantId,
            companyId,
            fundId,
            organizationId,
            fundNodeId,
            book,
            period,
            journalStore,
            parameters,
            new ReportAccessQueryContext(
                "reporting-user",
                CompanyId: companyId,
                TenantId: tenantId,
                RequireBoundScope: true));
        fixture.Source = new LedgerReportingAuthoritativeSource(
            journalStore,
            tenancy,
            structure,
            new FixedTimeProvider(now));
        fixture.StructureQuery = () => lastQuery;
        return fixture;
    }

    private static LedgerJournalEntryRecord IncomeAccrualRecord(Fixture fixture, string functionalCurrency = "USD")
    {
        var record = Record(fixture, new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero), 11);
        var lines = record.Entry.Lines.Select(line => new LedgerEntry(
            line.EntryId,
            line.JournalEntryId,
            line.Timestamp,
            line.Debit > 0m
                ? LedgerAccounts.AccruedInterestReceivable("BOND", "brokerage-a")
                : LedgerAccounts.CouponIncomeFor("brokerage-a"),
            line.Debit,
            line.Credit,
            line.Description,
            line.Dimensions! with { AccountId = "retained-account-a" },
            new LedgerEntryCurrency(
                "EUR",
                functionalCurrency,
                line.Debit > 0m ? 100m : 0m,
                line.Credit > 0m ? 100m : 0m,
                1.25m))).ToArray();
        return record with
        {
            Entry = new JournalEntry(record.Entry.JournalEntryId, record.Entry.Timestamp,
                record.Entry.Description, lines, new JournalEntryMetadata(ActivityType: "CouponAccrual")),
            CreatedAt = new DateTimeOffset(2026, 7, 15, 15, 0, 0, TimeSpan.Zero),
            AccountingPolicyId = "coupon-policy",
            AccountingPolicyVersion = "2",
            SourceEventId = Guid.NewGuid(),
            SourceJournalEntryId = Guid.NewGuid()
        };
    }

    private static LedgerJournalEntryRecord Record(
        Fixture fixture,
        DateTimeOffset timestamp,
        long sequence,
        string? fundId = null,
        string? debitCostCenterId = "cost-center-a",
        string? creditCostCenterId = "cost-center-a")
    {
        var journalId = Guid.NewGuid();
        var debitDimensions = new LedgerLineDimensionSet(
            fundId ?? fixture.FundId,
            CostCenterId: debitCostCenterId,
            OrganizationId: fixture.OrganizationId.ToString("D"),
            BookId: fixture.Book.LedgerBookId.ToString("D"));
        var creditDimensions = debitDimensions with { CostCenterId = creditCostCenterId };
        var entry = new JournalEntry(
            journalId,
            timestamp,
            "Reporting source line",
            [
                new LedgerEntry(
                    Guid.NewGuid(),
                    journalId,
                    timestamp,
                    new LedgerAccount("Assets:Cash", LedgerAccountType.Asset),
                    125m,
                    0m,
                    "Reporting source line",
                    debitDimensions),
                new LedgerEntry(
                    Guid.NewGuid(),
                    journalId,
                    timestamp,
                    new LedgerAccount("Equity:Capital", LedgerAccountType.Equity),
                    0m,
                    125m,
                    "Reporting source line",
                    creditDimensions)
            ]);
        return new LedgerJournalEntryRecord(
            entry,
            Guid.NewGuid(),
            fixture.Period.PeriodId,
            CommandId: null,
            CorrelationId: null,
            GlobalSequence: sequence,
            CreatedAt: timestamp,
            AccountingBasis: AccountingBasisKindDto.Gaap);
    }

    private sealed class Fixture(
        string tenantId,
        string companyId,
        string fundId,
        Guid organizationId,
        Guid fundNodeId,
        LedgerBookRecord book,
        LedgerAccountingPeriod period,
        QueryFilteringJournalStore journalStore,
        ReportingRunParametersDto parameters,
        ReportAccessQueryContext access)
    {
        public string TenantId { get; } = tenantId;
        public string CompanyId { get; } = companyId;
        public string FundId { get; } = fundId;
        public Guid OrganizationId { get; } = organizationId;
        public Guid FundNodeId { get; } = fundNodeId;
        public LedgerBookRecord Book { get; } = book;
        public LedgerAccountingPeriod Period { get; } = period;
        public QueryFilteringJournalStore JournalStore { get; } = journalStore;
        public ReportingRunParametersDto Parameters { get; set; } = parameters;
        public ReportAccessQueryContext Access { get; } = access;
        public LedgerReportingAuthoritativeSource Source { get; set; } = null!;
        public Func<OrganizationStructureQuery?> StructureQuery { private get; set; } = null!;
        public OrganizationStructureQuery? LastStructureQuery => StructureQuery();
    }

    private sealed class QueryFilteringJournalStore(
        LedgerBookRecord book,
        LedgerAccountingPeriod period) : ILedgerJournalStore, ILedgerTaxLotDisposalHistory, ILedgerReportingSnapshotSource
    {
        public List<LedgerJournalEntryRecord> Records { get; } = [];
        public List<LedgerTaxLotDisposalHistoryRecord> Disposals { get; } = [];
        public Task<IReadOnlyList<LedgerTaxLotDisposalHistoryRecord>> GetTaxLotDisposalHistoryAsync(
            Guid ledgerBookId, IReadOnlyList<Guid> journalEntryIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LedgerTaxLotDisposalHistoryRecord>>(Disposals);
        public bool ApplyQueryFilters { get; set; } = true;
        public bool RejectCompleteHistoryQueries { get; set; }
        public int QueryCount { get; private set; }
        public int CompleteHistoryQueryCount { get; private set; }
        public LedgerJournalEntryQuery? LastQuery { get; private set; }
        public Func<Task>? AfterFirstQuerySnapshot { get; set; }
        public LedgerAccountingPeriod SnapshotPeriod { get; set; } = period;
        public Exception? SnapshotCaptureException { get; set; }

        public async Task<IReadOnlyList<LedgerJournalEntryRecord>> QueryAsync(
            LedgerJournalEntryQuery query,
            CancellationToken ct = default)
        {
            var result = ReadQuerySnapshot(query);
            await PauseAfterFirstSnapshotAsync();
            return result;
        }

        public async Task<LedgerReportingSnapshot> CaptureReportingSnapshotAsync(
            LedgerJournalEntryQuery query,
            Guid? accountingPeriodId = null,
            CancellationToken ct = default)
        {
            var journals = ReadQuerySnapshot(query);
            if (SnapshotCaptureException is { } captureException)
            {
                throw captureException;
            }
            var taxHistory = Disposals.ToArray();
            if (accountingPeriodId is not null)
            {
                accountingPeriodId.Should().Be(period.PeriodId);
            }
            var capturedPeriod = accountingPeriodId is null ? null : SnapshotPeriod;
            await PauseAfterFirstSnapshotAsync();
            return new LedgerReportingSnapshot(journals, taxHistory, capturedPeriod);
        }

        private IReadOnlyList<LedgerJournalEntryRecord> ReadQuerySnapshot(LedgerJournalEntryQuery query)
        {
            LastQuery = query;
            QueryCount++;
            if (query.PeriodId is null)
            {
                CompleteHistoryQueryCount++;
                if (RejectCompleteHistoryQueries)
                {
                    throw new NotSupportedException(
                        "Complete-history replay is intentionally unavailable in this test.");
                }
            }
            IReadOnlyList<LedgerJournalEntryRecord> result = ApplyQueryFilters
                ? Records.Where(record =>
                        (query.PeriodId is null || record.PeriodId == query.PeriodId)
                        && (query.OccurredTo is null || record.Entry.Timestamp <= query.OccurredTo)
                        && record.Entry.Lines.Any(line => DimensionsMatch(line.Dimensions, query.LineDimensions)))
                    .ToArray()
                : Records.ToArray();
            return result;
        }

        private Task PauseAfterFirstSnapshotAsync() =>
            QueryCount == 1 && AfterFirstQuerySnapshot is { } afterSnapshot
                ? afterSnapshot()
                : Task.CompletedTask;

        public Task<LedgerBookRecord?> GetLedgerBookAsync(Guid ledgerBookId, CancellationToken ct = default) =>
            Task.FromResult<LedgerBookRecord?>(ledgerBookId == book.LedgerBookId ? book : null);

        public Task<LedgerAccountingPeriod?> GetPeriodAsync(Guid periodId, CancellationToken ct = default) =>
            Task.FromResult<LedgerAccountingPeriod?>(periodId == period.PeriodId ? period : null);

        public Task AppendAsync(LedgerJournalEntryWrite entry, CancellationToken ct = default)
        {
            Records.Add(new LedgerJournalEntryRecord(
                entry.Entry,
                entry.AggregateId,
                entry.PeriodId,
                entry.CommandId,
                entry.CorrelationId,
                Records.Count == 0 ? 1 : Records.Max(static record => record.GlobalSequence) + 1,
                entry.Entry.Timestamp,
                entry.AccountingBasis));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByPeriodAsync(Guid periodId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(Records.Where(record => record.PeriodId == periodId).ToArray());

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByAggregateAsync(Guid aggregateId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(Records.Where(record => record.AggregateId == aggregateId).ToArray());

        public Task<IReadOnlyList<LedgerAccountingPeriod>> ListPeriodsAsync(
            Guid? ledgerBookId = null,
            string? status = null,
            string? fundProfileId = null,
            Guid? fundStructureNodeId = null,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LedgerAccountingPeriod>>([period]);

        public Task<LedgerAccountingPeriod> SavePeriodAsync(
            LedgerAccountingPeriod value,
            long expectedVersion,
            PeriodCloseEventRecord? closeEvent = null,
            CancellationToken ct = default) =>
            Task.FromResult(value);

        public Task<IReadOnlyList<LedgerBookRecord>> ListLedgerBooksAsync(
            string? fundProfileId = null,
            Guid? fundStructureNodeId = null,
            FundStructureNodeKindDto? fundStructureNodeKind = null,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LedgerBookRecord>>([book]);

        public Task<LedgerBookRecord> SaveLedgerBookAsync(LedgerBookRecord value, CancellationToken ct = default) =>
            Task.FromResult(value);

        private static bool DimensionsMatch(
            LedgerLineDimensionSet? actual,
            LedgerLineDimensionSet? expected) =>
            expected is null || actual is not null
                && Matches(actual.FundId, expected.FundId)
                && Matches(actual.OrganizationId, expected.OrganizationId)
                && Matches(actual.BookId, expected.BookId)
                && Matches(actual.CostCenterId, expected.CostCenterId);

        private static bool Matches(string? actual, string? expected) =>
            string.IsNullOrWhiteSpace(expected)
            || string.Equals(actual, expected, StringComparison.Ordinal);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
