using System.Text.Json;
using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Application.Ledger;
using Meridian.Contracts.DirectLending;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.DirectLending;
using Meridian.Storage.Ledger;
using NSubstitute;

namespace Meridian.Tests.Application.DirectLending;

public sealed class PostgresDirectLendingCommandServiceTests
{
    [Fact]
    public async Task PostDailyAccrualAsync_PassesProjectedLedgerEntriesToStateStoreWithEventLineage()
    {
        var loanId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var commandId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var correlationId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var contract = BuildContract(loanId);
        var servicing = BuildServicing(loanId);
        IReadOnlyList<LedgerJournalEntryWrite>? capturedLedgerEntries = null;
        Guid capturedEventId = Guid.Empty;

        var stateStore = Substitute.For<IDirectLendingStateStore>();
        stateStore.SaveAsync(
                loanId,
                expectedVersion: 7,
                nextVersion: 8,
                Arg.Any<LoanContractDetailDto>(),
                Arg.Any<LoanServicingStateDto>(),
                eventType: "loan.daily-accrual-posted",
                eventSchemaVersion: 3,
                effectiveDate: new DateOnly(2026, 3, 24),
                Arg.Any<JsonDocument>(),
                Arg.Any<DirectLendingEventWriteMetadata>(),
                Arg.Any<DirectLendingPersistenceBatch?>(),
                Arg.Any<IReadOnlyList<LedgerJournalEntryWrite>?>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(call =>
            {
                capturedLedgerEntries = call.ArgAt<IReadOnlyList<LedgerJournalEntryWrite>?>(11);
                capturedEventId = call.ArgAt<Guid>(12);
            });

        var queryService = Substitute.For<IDirectLendingQueryService>();
        queryService.LoadAggregateAsync(loanId, Arg.Any<CancellationToken>())
            .Returns(new PersistedDirectLendingState(loanId, 7, contract, servicing));

        var service = new PostgresDirectLendingCommandService(
            stateStore,
            Substitute.For<IDirectLendingOperationsStore>(),
            queryService,
            new LoanAccountingProjector(BuildLedgerJournalStore(), new AccountingPolicyService(), BuildSecurityMasterQueryService()),
            new DirectLendingOptions { CurrentEventSchemaVersion = 3 });

        var result = await service.PostDailyAccrualAsync(
            loanId,
            new PostDailyAccrualRequest(new DateOnly(2026, 3, 24)),
            new DirectLendingCommandMetadataDto(
                CommandId: commandId,
                CorrelationId: correlationId,
                CausationId: null,
                SourceSystem: "unit-test",
                ReplayFlag: false));

        result.IsSuccess.Should().BeTrue();
        capturedEventId.Should().NotBeEmpty();
        capturedLedgerEntries.Should().ContainSingle();
        var write = capturedLedgerEntries![0];
        write.AggregateId.Should().Be(loanId);
        write.SourceEventId.Should().Be(capturedEventId);
        write.CommandId.Should().Be(commandId);
        write.CorrelationId.Should().Be(correlationId);
        write.PostingKind.Should().Be(LedgerPostingKindDto.Originating);
        write.Entry.IsBalanced.Should().BeTrue();
        write.Entry.Metadata.Symbol.Should().Be("NWTERM26");
        write.Entry.Metadata.SecurityId.Should().Be(Guid.Parse("99999999-9999-9999-9999-999999999999"));
        write.Entry.Metadata.Tags.Should().ContainKey("sourceEventId")
            .WhoseValue.Should().Be(capturedEventId.ToString("D"));
        write.Entry.Metadata.Tags.Should().ContainKey("securityMasterProvenance")
            .WhoseValue.Should().Contain("server-resolved:true");
        write.Entry.Metadata.Tags.Should().ContainKey("securityMasterLineage")
            .WhoseValue.Should().Contain("ledger-map:direct-lending:NWTERM26");
        write.Entry.Lines
            .Where(static line => !string.Equals(line.Account.Name, "Cash", StringComparison.OrdinalIgnoreCase))
            .Should()
            .OnlyContain(static line => line.Account.Symbol == "NWTERM26");
    }

    [Fact]
    public async Task PostDailyAccrualAsync_BlocksLedgerProjectionWithoutSecurityMasterReference()
    {
        var loanId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var contract = BuildContract(loanId, includeSecurityMasterReference: false);
        var servicing = BuildServicing(loanId);

        var stateStore = Substitute.For<IDirectLendingStateStore>();
        var queryService = Substitute.For<IDirectLendingQueryService>();
        queryService.LoadAggregateAsync(loanId, Arg.Any<CancellationToken>())
            .Returns(new PersistedDirectLendingState(loanId, 7, contract, servicing));

        var service = new PostgresDirectLendingCommandService(
            stateStore,
            Substitute.For<IDirectLendingOperationsStore>(),
            queryService,
            new LoanAccountingProjector(BuildLedgerJournalStore(), new AccountingPolicyService(), BuildSecurityMasterQueryService()),
            new DirectLendingOptions { CurrentEventSchemaVersion = 3 });

        var act = () => service.PostDailyAccrualAsync(
            loanId,
            new PostDailyAccrualRequest(new DateOnly(2026, 3, 24)));

        await act.Should().ThrowAsync<DirectLendingCommandException>()
            .WithMessage("*requires a Security Master reference before posting*");
        await stateStore.DidNotReceiveWithAnyArgs().SaveAsync(
            default,
            default,
            default,
            default!,
            default!,
            default!,
            default,
            default,
            default!,
            default!,
            default,
            default,
            default);
    }


    [Fact]
    public async Task PostDailyAccrualAsync_RejectsForgedSecurityMasterReferenceMissingFromAuthoritativeStore()
    {
        var loanId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var baselineContract = BuildContract(loanId);
        var contract = baselineContract with
        {
            CurrentTerms = baselineContract.CurrentTerms with
            {
                SecurityMasterReference = new DirectLendingSecurityMasterReferenceDto(
                    SecurityId: Guid.Parse("11111111-2222-3333-4444-555555555555"),
                    Symbol: "FORGEDTERM26",
                    Provenance: "source:attacker;approved:true",
                    ApprovalReference: "sm-approval:attacker-controlled-ticket",
                    LedgerMappingReference: "ledger-map:forgedterm26-direct-lending",
                    Status: "Active")
            }
        };
        var servicing = BuildServicing(loanId);

        var stateStore = Substitute.For<IDirectLendingStateStore>();
        var queryService = Substitute.For<IDirectLendingQueryService>();
        queryService.LoadAggregateAsync(loanId, Arg.Any<CancellationToken>())
            .Returns(new PersistedDirectLendingState(loanId, 7, contract, servicing));

        var service = new PostgresDirectLendingCommandService(
            stateStore,
            Substitute.For<IDirectLendingOperationsStore>(),
            queryService,
            new LoanAccountingProjector(BuildLedgerJournalStore(), new AccountingPolicyService(), BuildSecurityMasterQueryService()),
            new DirectLendingOptions { CurrentEventSchemaVersion = 3 });

        var act = () => service.PostDailyAccrualAsync(
            loanId,
            new PostDailyAccrualRequest(new DateOnly(2026, 3, 24)));

        await act.Should().ThrowAsync<DirectLendingCommandException>()
            .WithMessage("*no authoritative record exists*");
        await stateStore.DidNotReceiveWithAnyArgs().SaveAsync(
            default,
            default,
            default,
            default!,
            default!,
            default!,
            default,
            default,
            default!,
            default!,
            default,
            default,
            default);
    }

    private static ILedgerJournalStore BuildLedgerJournalStore()
    {
        var store = Substitute.For<ILedgerJournalStore>();
        store.ListPeriodsAsync(
                null,
                null,
                null,
                null,
                Arg.Any<CancellationToken>())
            .Returns(
            [
                new LedgerAccountingPeriod(
                    PeriodId: Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                    LedgerBookId: Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                    FiscalYear: 2026,
                    PeriodNo: 3,
                    Label: "2026-P03",
                    StartDate: new DateOnly(2026, 3, 1),
                    EndDate: new DateOnly(2026, 3, 31),
                    Status: "Open",
                    OpenedAt: DateTimeOffset.Parse("2026-03-01T00:00:00Z"),
                    ClosedAt: null,
                    Version: 1)
            ]);

        return store;
    }

    private static Meridian.Application.SecurityMaster.ISecurityMasterQueryService BuildSecurityMasterQueryService()
    {
        var service = Substitute.For<Meridian.Application.SecurityMaster.ISecurityMasterQueryService>();
        service.GetByIdAsync(Guid.Parse("99999999-9999-9999-9999-999999999999"), Arg.Any<CancellationToken>())
            .Returns(BuildSecurityMasterDetail());
        return service;
    }

    private static SecurityDetailDto BuildSecurityMasterDetail() =>
        new(
            SecurityId: Guid.Parse("99999999-9999-9999-9999-999999999999"),
            AssetClass: "CustomAsset",
            Status: SecurityStatusDto.Active,
            DisplayName: "Northwind Senior Term Loan",
            Currency: "USD",
            CommonTerms: JsonSerializer.SerializeToElement(new { displayName = "Northwind Senior Term Loan", currency = "USD" }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new { facility = "Northwind" }),
            Identifiers:
            [
                new SecurityIdentifierDto(
                    SecurityIdentifierKind.InternalCode,
                    "NWTERM26",
                    IsPrimary: true,
                    ValidFrom: DateTimeOffset.Parse("2026-03-22T00:00:00Z"))
            ],
            Aliases: [],
            Version: 4,
            EffectiveFrom: DateTimeOffset.Parse("2026-03-22T00:00:00Z"),
            EffectiveTo: null);

    private static LoanContractDetailDto BuildContract(Guid loanId, bool includeSecurityMasterReference = true)
    {
        var terms = new DirectLendingTermsDto(
            OriginationDate: new DateOnly(2026, 3, 22),
            MaturityDate: new DateOnly(2029, 3, 22),
            CommitmentAmount: 1_000_000m,
            BaseCurrency: CurrencyCode.USD,
            RateTypeKind: RateTypeKind.Fixed,
            FixedAnnualRate: 0.08m,
            InterestIndexName: null,
            SpreadBps: null,
            FloorRate: null,
            CapRate: null,
            DayCountBasis: DayCountBasis.Act360,
            PaymentFrequency: PaymentFrequency.Monthly,
            AmortizationType: AmortizationType.InterestOnly,
            CommitmentFeeRate: 0.0025m,
            DefaultRateSpreadBps: null,
            PrepaymentAllowed: true,
            CovenantsJson: null,
            SecurityMasterReference: includeSecurityMasterReference
                ? new DirectLendingSecurityMasterReferenceDto(
                    SecurityId: Guid.Parse("99999999-9999-9999-9999-999999999999"),
                    Symbol: "NWTERM26",
                    Provenance: "source:security-master;definition:loan-facility",
                    ApprovalReference: "sm-approval:nwterm26-controller",
                    LedgerMappingReference: "ledger-map:nwterm26-direct-lending",
                    Status: "Active")
                : null);

        return new LoanContractDetailDto(
            LoanId: loanId,
            FacilityName: "Northwind Senior Term Loan",
            Borrower: new BorrowerInfoDto(Guid.NewGuid(), "Northwind Holdings", Guid.NewGuid()),
            Status: LoanStatus.Active,
            EffectiveDate: new DateOnly(2026, 3, 22),
            ActivationDate: new DateOnly(2026, 3, 22),
            CloseDate: null,
            CurrentTermsVersion: 1,
            CurrentTerms: terms,
            TermsVersions:
            [
                new LoanTermsVersionDto(
                    VersionNumber: 1,
                    TermsHash: "terms-v1",
                    Terms: terms,
                    SourceAction: "loan.created",
                    AmendmentReason: null,
                    RecordedAt: DateTimeOffset.Parse("2026-03-22T00:00:00Z"))
            ]);
    }

    private static LoanServicingStateDto BuildServicing(Guid loanId) =>
        new(
            LoanId: loanId,
            Status: LoanStatus.Active,
            CurrentCommitment: 1_000_000m,
            TotalDrawn: 500_000m,
            AvailableToDraw: 500_000m,
            Balances: new OutstandingBalancesDto(
                PrincipalOutstanding: 500_000m,
                InterestAccruedUnpaid: 0m,
                CommitmentFeeAccruedUnpaid: 0m,
                FeesAccruedUnpaid: 0m,
                PenaltyAccruedUnpaid: 0m),
            DrawdownLots: [],
            CurrentRateReset: null,
            LastAccrualDate: new DateOnly(2026, 3, 23),
            LastPaymentDate: null,
            ServicingRevision: 7,
            RevisionHistory: [],
            AccrualEntries: []);
}
