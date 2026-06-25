using FluentAssertions;
using Meridian.Contracts.Etl;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Plaid;
using Meridian.Infrastructure.Etl;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Storage.Etl;
using Meridian.Ui.Shared.Services;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class BankFeedTransportServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-bank-feed-transport-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RunIfDueAsync_LocalFileSchedule_ShouldImportBankStatementEvidenceWithoutLedgerMutation()
    {
        var inputDirectory = Path.Combine(_root, "input");
        Directory.CreateDirectory(inputDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "bank-statement.csv"),
            """
            transaction_date,value_date,amount,currency,transaction_type,description,reference,closing_balance
            2026-06-01,2026-06-03,-50000.00,USD,Wire,Payment to broker,WIRE-20260601-001,950000.00
            2026-06-02,,1250.25,USD,Interest,Deposit interest,INT-20260602,951250.25
            """);

        var accountService = new InMemoryFundAccountService();
        var account = await accountService.CreateAccountAsync(CreateBankAccountRequest());
        var service = new BankFeedTransportService(
            accountService,
            [new LocalFileSourceReader(new EtlStagingStore(_root))]);
        var schedule = new BankFeedScheduleDefinition(
            ScheduleId: "daily-bank-csv",
            TransportKind: BankFeedTransportKind.LocalFile,
            Interval: TimeSpan.FromDays(1),
            LastRunAtUtc: null,
            AccountId: account.AccountId,
            BankName: "JPMorgan",
            Source: new EtlSourceDefinition
            {
                Kind = EtlSourceKind.Local,
                Location = inputDirectory,
                FilePattern = "*.csv"
            },
            StatementDate: new DateOnly(2026, 6, 30),
            RequestedBy: "scheduler");

        var result = await service.RunIfDueAsync(schedule, DateTimeOffset.Parse("2026-07-01T01:00:00Z"));

        result.Ran.Should().BeTrue();
        result.Success.Should().BeTrue();
        result.FilesProcessed.Should().Be(1);
        result.BankStatementLinesImported.Should().Be(2);
        result.ImportedBatchIds.Should().ContainSingle();

        var lines = await accountService.GetBankStatementLinesAsync(account.AccountId);
        lines.Should().HaveCount(2);
        lines.Should().Contain(line =>
            line.Reference == "WIRE-20260601-001" &&
            line.Amount == -50000m &&
            line.ClosingBalance == 950000m);
        lines.Should().Contain(line =>
            line.Reference == "INT-20260602" &&
            line.ValueDate == line.TransactionDate &&
            line.Amount == 1250.25m);

        var balances = await accountService.GetBalanceHistoryAsync(account.AccountId);
        balances.Should().BeEmpty("bank feed transport imports retained statement evidence, not ledger or balance records");
    }

    [Fact]
    public async Task RunIfDueAsync_SftpSchedule_ShouldUseRegisteredSftpReaderAndImportBankStatementEvidence()
    {
        var stagedPath = Path.Combine(_root, "staged", "sftp-bank-statement.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
        await File.WriteAllTextAsync(
            stagedPath,
            """
            transaction_date,value_date,amount,currency,transaction_type,description,reference,closing_balance
            2026-06-05,2026-06-05,-2500.00,USD,ACH,Management fee,FEE-20260605,948750.25
            """);

        var accountService = new InMemoryFundAccountService();
        var account = await accountService.CreateAccountAsync(CreateBankAccountRequest());
        var remoteFile = new EtlRemoteFile
        {
            Path = "/inbound/sftp-bank-statement.csv",
            Name = "sftp-bank-statement.csv",
            SizeBytes = new FileInfo(stagedPath).Length,
            LastModifiedUtc = DateTimeOffset.Parse("2026-06-05T23:00:00Z")
        };
        var sftpReader = new StubEtlSourceReader(EtlSourceKind.Sftp, remoteFile, stagedPath);
        var service = new BankFeedTransportService(accountService, [sftpReader]);
        var source = new EtlSourceDefinition
        {
            Kind = EtlSourceKind.Sftp,
            Location = "sftp://bank.example.com/inbound",
            FilePattern = "*.csv",
            Username = "feed-user",
            SecretRef = "secret-ref"
        };
        var schedule = new BankFeedScheduleDefinition(
            ScheduleId: "sftp-bank-csv",
            TransportKind: BankFeedTransportKind.Sftp,
            Interval: TimeSpan.FromDays(1),
            LastRunAtUtc: null,
            AccountId: account.AccountId,
            BankName: "JPMorgan",
            Source: source,
            StatementDate: new DateOnly(2026, 6, 30),
            RequestedBy: "scheduler");

        var result = await service.RunIfDueAsync(schedule, DateTimeOffset.Parse("2026-07-01T01:00:00Z"));

        result.Ran.Should().BeTrue();
        result.Success.Should().BeTrue();
        result.FilesProcessed.Should().Be(1);
        result.BankStatementLinesImported.Should().Be(1);
        sftpReader.ListCalls.Should().Be(1);
        sftpReader.StageCalls.Should().Be(1);
        sftpReader.LastSource.Should().BeSameAs(source);

        var lines = await accountService.GetBankStatementLinesAsync(account.AccountId);
        lines.Should().ContainSingle(line =>
            line.Reference == "FEE-20260605" &&
            line.Amount == -2500m &&
            line.ClosingBalance == 948750.25m);
    }

    [Fact]
    public async Task RunIfDueAsync_PlaidApiSchedule_ShouldDelegateToPlaidSync()
    {
        var now = DateTimeOffset.Parse("2026-07-01T01:00:00Z");
        var plaid = Substitute.For<IPlaidIngestionService>();
        plaid.SyncItemAsync(Arg.Any<PlaidSyncRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PlaidSyncResult(
                "item-1",
                BalanceSnapshotsRecorded: 1,
                BankStatementLinesIngested: 3,
                InvestmentHoldingsRecorded: 0,
                InvestmentTransactionsRecorded: 0,
                IdentityRecordsUpdated: 0,
                SyncedAt: now,
                Warnings: []));
        var service = new BankFeedTransportService(new InMemoryFundAccountService(), [], plaid);
        var schedule = new BankFeedScheduleDefinition(
            ScheduleId: "plaid-hourly",
            TransportKind: BankFeedTransportKind.PlaidApi,
            Interval: TimeSpan.FromHours(1),
            LastRunAtUtc: now.AddHours(-2),
            PlaidItemId: "item-1",
            RequestedBy: "scheduler");

        var result = await service.RunIfDueAsync(schedule, now);

        result.Ran.Should().BeTrue();
        result.Success.Should().BeTrue();
        result.ApiItemsSynced.Should().Be(1);
        result.BankStatementLinesImported.Should().Be(3);
        await plaid.Received(1).SyncItemAsync(
            Arg.Is<PlaidSyncRequest>(request => request.ItemId == "item-1" && request.RequestedBy == "scheduler"),
            Arg.Any<CancellationToken>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static CreateAccountRequest CreateBankAccountRequest()
        => new(
            AccountId: Guid.NewGuid(),
            AccountType: AccountTypeDto.Bank,
            AccountCode: $"BANK-{Guid.NewGuid():N}",
            DisplayName: "Operating Cash",
            BaseCurrency: "USD",
            EffectiveFrom: DateTimeOffset.UtcNow,
            CreatedBy: "test",
            BankDetails: new BankAccountDetailsDto(
                AccountNumber: "123456789",
                BankName: "JPMorgan",
                BranchName: null,
                Iban: null,
                BicSwift: "CHASUS33",
                RoutingNumber: "021000021",
                SortCode: null,
                IntermediaryBankBic: null,
                IntermediaryBankName: null,
                BeneficiaryName: "Meridian Fund",
                BeneficiaryAddress: null));

    private sealed class StubEtlSourceReader : IEtlSourceReader
    {
        private readonly EtlRemoteFile _remoteFile;
        private readonly string _stagedPath;

        public StubEtlSourceReader(EtlSourceKind kind, EtlRemoteFile remoteFile, string stagedPath)
        {
            Kind = kind;
            _remoteFile = remoteFile;
            _stagedPath = stagedPath;
        }

        public EtlSourceKind Kind { get; }
        public int ListCalls { get; private set; }
        public int StageCalls { get; private set; }
        public EtlSourceDefinition? LastSource { get; private set; }

        public Task<IReadOnlyList<EtlRemoteFile>> ListFilesAsync(EtlSourceDefinition source, CancellationToken ct = default)
        {
            ListCalls++;
            LastSource = source;
            return Task.FromResult<IReadOnlyList<EtlRemoteFile>>([_remoteFile]);
        }

        public Task<EtlStagedFile> StageFileAsync(
            string jobId,
            EtlSourceDefinition source,
            EtlRemoteFile file,
            CancellationToken ct = default)
        {
            StageCalls++;
            LastSource = source;
            return Task.FromResult(new EtlStagedFile
            {
                OriginalPath = file.Path,
                StagedPath = _stagedPath,
                FileName = file.Name,
                ChecksumSha256 = "stub-checksum",
                SizeBytes = new FileInfo(_stagedPath).Length
            });
        }

        public Task PostProcessFileAsync(EtlSourceDefinition source, EtlRemoteFile file, bool succeeded, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
