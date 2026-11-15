using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Ledger;
using Meridian.PortfolioRecords.Accounts;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Ui;

/// <summary>Exercises actual retained broker normalization and independent paper OMS capture.</summary>
public sealed class ThreeWayReconciliationSourceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-three-way-source-" + Guid.NewGuid().ToString("N"));
    private readonly Guid _account = Guid.NewGuid(), _book = Guid.NewGuid(), _period = Guid.NewGuid(), _entity = Guid.NewGuid(),
        _fund = Guid.NewGuid(), _security = Guid.NewGuid(), _position = Guid.NewGuid(), _lot = Guid.NewGuid();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualOmsAndRetainedBroker_RequireFullCanonicalLotCoverage(bool missingCanonicalLots)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var now = DateTimeOffset.UtcNow.AddSeconds(-2);
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var options = new ThreeWayReconciliationOptions
        {
            RootDirectory = Path.Combine(_root, "comparisons"),
            BrokerCostBasisConvention = ThreeWayReconciliationOptions.SupportedBasisConvention
        };
        var evidence = new FileThreeWayReconciliationEvidenceStore(options);
        var brokerage = await BrokerAsync(services, now, missingCanonicalLots);
        var accounts = Accounts(now);
        var ledger = Ledger(now, out var captured);
        if (missingCanonicalLots)
        {
            var original = await captured.CaptureOpenPositionReportingSnapshotAsync(new(LedgerBookId: _book, OccurredTo: now), _period);
            captured.CaptureOpenPositionReportingSnapshotAsync(Arg.Any<LedgerJournalEntryQuery>(), _period, Arg.Any<CancellationToken>())
                .Returns(call => original with
                {
                    OpenPositions = new(DateOnly.FromDateTime(call.Arg<LedgerJournalEntryQuery>().OccurredTo!.Value.UtcDateTime), [], [])
                    { CutoffUtc = call.Arg<LedgerJournalEntryQuery>().OccurredTo }
                });
        }
        var tenancy = Substitute.For<IFundProfileTenancyRegistry>();
        tenancy.ResolveAsync("profile", Arg.Any<CancellationToken>()).Returns(new FundProfileOwnership("profile", "tenant", "company"));
        var resolver = Substitute.For<ISecurityResolver>();
        resolver.ResolveAsync(Arg.Any<ResolveSecurityRequest>(), Arg.Any<CancellationToken>()).Returns((Guid?)_security);
        var paper = new PaperTradingPortfolio([new AccountDefinition(_account.ToString("D"), "Synthetic account", AccountKind.Brokerage, missingCanonicalLots ? 999m : 2_000m)],
            null, (ILivePositionCorporateActionAdjuster?)null);
        if (!missingCanonicalLots)
            paper.ApplyFill(_account.ToString("D"), new ExecutionReport
            {
                OrderId = "synthetic-buy",
                Symbol = "TEST",
                Side = OrderSide.Buy,
                ReportType = ExecutionReportType.Fill,
                OrderStatus = Meridian.Execution.Sdk.OrderStatus.Filled,
                FilledQuantity = 10m,
                FillPrice = 100m,
                Commission = 1m,
                Timestamp = now
            });
        var reader = new ThreeWayReconciliationSourceReader(brokerage, evidence, options, accounts,
            ledger, tenancy, paper, resolver);
        var queue = new FileReconciliationBreakQueueRepository(Path.Combine(_root, "queue"),
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        var service = new ThreeWayReconciliationService(reader, evidence, queue, options);

        var result = await service.RunAsync(_account, new("tenant", "company"), new(_book, _period, "synthetic-e2e"));

        result.Status.Should().Be(missingCanonicalLots ? "Blocked" : "Matched");
        result.DataProvenanceToken.Should().Be("simulated");
        result.Sources.Should().HaveCount(3).And.OnlyContain(source => source.Cash == 999m);
        if (missingCanonicalLots)
            result.Sources.Single(source => source.Source == "ledger").CoverageFailures
                .Should().Contain(failure => failure.Contains("canonical historical open-lot basis", StringComparison.Ordinal));
        else
            result.Sources.Should().OnlyContain(source => source.Positions.Single().Quantity == 10m
                && source.Positions.Single().CostBasis == 1_000m);
        result.Sources.Single(source => source.Source == "ledger").DataProvenanceToken.Should().Be("seeded");
        if (missingCanonicalLots)
            result.BreakIds.Should().NotBeEmpty();
        else
            result.BreakIds.Should().BeEmpty();
        await captured.Received(1).CaptureOpenPositionReportingSnapshotAsync(
            Arg.Is<LedgerJournalEntryQuery>(query => query.LedgerBookId == _book && query.OccurredTo == result.CutoffUtc
                && query.EffectiveTo == DateOnly.FromDateTime(result.CutoffUtc.UtcDateTime)), _period, Arg.Any<CancellationToken>());
        // Decimal strings preserve exact values for inspection without browser-number conversion.
        var json = JsonSerializer.Serialize(result, JsonOptions);
        json.Should().Contain("\"cash\":\"999\"");
    }

    [Fact]
    public async Task DefaultReader_RefusesForeignOwnershipAndMissingCanonicalCapture()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var now = DateTimeOffset.UtcNow;
        var brokerage = await BrokerAsync(services, now);
        var accounts = Accounts(now);
        var ledger = Ledger(now, out _);
        var tenancy = Substitute.For<IFundProfileTenancyRegistry>();
        tenancy.ResolveAsync("profile", Arg.Any<CancellationToken>()).Returns(new FundProfileOwnership("profile", "foreign", "company"));
        var options = new ThreeWayReconciliationOptions { RootDirectory = Path.Combine(_root, "evidence") };
        var evidence = new FileThreeWayReconciliationEvidenceStore(options);
        var reader = new ThreeWayReconciliationSourceReader(brokerage, evidence, options, accounts, ledger, tenancy);
        var foreign = () => reader.AuthorizeAsync(_account, new("tenant", "company"), _book, _period);
        await foreign.Should().ThrowAsync<UnauthorizedAccessException>();
        tenancy.ResolveAsync("profile", Arg.Any<CancellationToken>()).Returns(new FundProfileOwnership("profile", "tenant", "company"));
        var unsupported = Substitute.For<ILedgerJournalStore>();
        var retainedBook = await ledger.GetLedgerBookAsync(_book);
        var retainedPeriod = await ledger.GetPeriodAsync(_period);
        unsupported.GetLedgerBookAsync(_book, Arg.Any<CancellationToken>()).Returns(retainedBook);
        unsupported.GetPeriodAsync(_period, Arg.Any<CancellationToken>()).Returns(retainedPeriod);
        reader = new ThreeWayReconciliationSourceReader(brokerage, evidence, options, accounts, unsupported, tenancy);
        var scope = await reader.AuthorizeAsync(_account, new("tenant", "company"), _book, _period);
        var source = await reader.ReadAsync(scope, now);
        source.Single(snapshot => snapshot.Source == "ledger").CoverageFailures.Should().NotBeEmpty();
        source.Single(snapshot => snapshot.Source == "ledger").Cash.Should().BeNull();
    }

    private IAccountQueryService Accounts(DateTimeOffset now)
    {
        var accounts = Substitute.For<IAccountQueryService>();
        accounts.GetAccountAsync(_account, Arg.Any<CancellationToken>()).Returns(new AccountSummaryDto(_account,
            AccountTypeDto.Brokerage, _entity, _fund, null, null, "SYNTH", "Synthetic account", "USD", "synthetic",
            true, now.AddDays(-1), null, null, null, null, "paper-run"));
        return accounts;
    }

    private ILedgerJournalStore Ledger(DateTimeOffset now, out ILedgerOpenPositionReportingSnapshotSource source)
    {
        var ledger = Substitute.For<ILedgerJournalStore, ILedgerOpenPositionReportingSnapshotSource>();
        source = (ILedgerOpenPositionReportingSnapshotSource)ledger;
        var date = DateOnly.FromDateTime(now.UtcDateTime);
        ledger.GetLedgerBookAsync(_book, Arg.Any<CancellationToken>()).Returns(new LedgerBookRecord(_book,
            "profile", _fund, FundStructureNodeKindDto.Fund, "Synthetic USD book", "USD", now, now));
        var period = new LedgerAccountingPeriod(_period, _book, date.Year, date.Month, "Synthetic period",
            date.AddDays(-1), date.AddDays(1), "Open", now, null, 1);
        ledger.GetPeriodAsync(_period, Arg.Any<CancellationToken>()).Returns(period);
        var cash = LedgerAccounts.CashFor(_account.ToString("D"));
        var carrying = LedgerAccounts.Securities("TEST", _account.ToString("D"));
        var journals = new[]
        {
            Journal(1, now.AddMinutes(-1), cash, 2_000m, LedgerAccounts.CapitalAccountFor(_account.ToString("D"))),
            Journal(2, now, carrying, 1_000m, cash),
            Journal(3, now, LedgerAccounts.CommissionExpenseFor(_account.ToString("D")), 1m, cash)
        };
        var acquiredEvidence = new RetainedEvidenceIdentityDto("acquisition", "urn:synthetic:acquisition", new string('a', 64),
            "synthetic", "fill-1", "Accepted", "reviewer", now, date, 1, now, "operator", "OpenLotAcquisition", _lot.ToString("D"));
        var lot = new LedgerTaxLotRecord(_lot, _book, carrying, "lot-1", date, 10m, 10m, 100m, "USD", now, now,
            journals[1].Entry.JournalEntryId, "urn:synthetic:acquisition", 1, Guid.NewGuid(), Guid.NewGuid(), _security, _position,
            Acquisition: new OpenLotAcquisitionDto(LotQuantityBasis.Units, "USD", "USD", 1m, 1_000m, 1_000m,
                date, null, [acquiredEvidence]));
        source.CaptureOpenPositionReportingSnapshotAsync(Arg.Any<LedgerJournalEntryQuery>(), _period, Arg.Any<CancellationToken>())
            .Returns(call => new LedgerReportingSnapshot(journals, [], period)
            {
                OpenPositions = new(DateOnly.FromDateTime(call.Arg<LedgerJournalEntryQuery>().OccurredTo!.Value.UtcDateTime), [lot], [])
                { CutoffUtc = call.Arg<LedgerJournalEntryQuery>().OccurredTo }
            });
        return ledger;
    }

    private LedgerJournalEntryRecord Journal(long sequence, DateTimeOffset time, LedgerAccount debit, decimal amount, LedgerAccount credit)
    {
        var id = Guid.NewGuid();
        const string description = "Synthetic accounting source";
        var dimensions = new LedgerLineDimensionSet(InstrumentId: _security, AccountId: _account.ToString("D")) { PositionId = _position };
        var entry = new JournalEntry(id, time, description,
        [
            new(Guid.NewGuid(), id, time, debit, amount, 0m, description, dimensions, new("USD", "USD", amount, 0m, 1m)),
            new(Guid.NewGuid(), id, time, credit, 0m, amount, description, dimensions, new("USD", "USD", 0m, amount, 1m))
        ], new(FinancialAccountId: _account.ToString("D"), SecurityId: _security,
            Tags: new Dictionary<string, string> { ["dataProvenance"] = "SEEDED" }));
        return new(entry, _book, _period, null, null, sequence, time);
    }

    private async Task<BrokeragePortfolioSyncService> BrokerAsync(IServiceProvider services, DateTimeOffset now, bool withoutPositions = false)
    {
        var path = Path.Combine(_root, "broker", "projections", _account.ToString("N"), "current.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var status = new WorkstationBrokerageSyncStatusDto(_account, "synthetic", "external-1", WorkstationBrokerageSyncHealth.Healthy,
            true, false, now, now, null, 1, 0, 1, 0, 0, []);
        var projection = new FundAccountBrokerageSyncActivityDto(_account,
            new(_account, "synthetic", "external-1", "Synthetic account", now), status, new(999m, 1_999m, 999m, "USD", 0m),
            [new("TEST", 10m, 100m, 100m, 1_000m, 0m, "us_equity",
                new(_security, "Synthetic security", "Equity", "USD", SecurityStatusDto.Active, "TEST"), Currency: "USD")],
            [], [], [], now, "urn:synthetic:broker-observation", path);
        if (withoutPositions)
            projection = projection with { Positions = [] };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(projection, JsonOptions));
        return new(new BrokeragePortfolioSyncOptions(Path.Combine(_root, "broker"), TimeSpan.FromMinutes(30), "synthetic"),
            [], [], [], services, NullLogger<BrokeragePortfolioSyncService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
