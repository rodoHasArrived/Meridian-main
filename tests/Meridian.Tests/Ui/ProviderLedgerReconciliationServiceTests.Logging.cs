using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Services;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Strategies.Services;
using Meridian.Tests.TestHelpers;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed partial class ProviderLedgerReconciliationServiceTests
{
    [Fact]
    public async Task UnexpectedFailure_LogsCorrelationAndTypeWithoutAccountOrExceptionDetails_AndRetainsRun()
    {
        const string secret = "confidential-provider-backend-detail";
        const string operationId = "provider-reconciliation-logging\r\nforged-failure";
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(root, includeSecurityLookup: true);
            var accounts = Substitute.For<IFundAccountService>();
            accounts.GetAccountAsync(fixture.AccountId, Arg.Any<CancellationToken>())
                .Returns(_ => fixture.AccountService.GetAccountAsync(fixture.AccountId));
            accounts.GetLatestBalanceSnapshotAsync(fixture.AccountId, Arg.Any<CancellationToken>())
                .Returns(Task.FromException<AccountBalanceSnapshotDto?>(
                    new IOException($"{secret}: account {fixture.AccountId:D}")));
            var logger = new RecordingLogger<ProviderLedgerReconciliationService>();
            var service = CreateLoggingService(fixture, logger, accounts);

            var failed = await service.RunAsync(fixture.AccountId, fixture.QueueScope,
                new ProviderLedgerReconciliationRequestDto(OperationId: operationId));

            failed.Outcome!.State.Should().Be(OperationTerminalState.Failed);
            failed.Summary.AccountId.Should().Be(fixture.AccountId);
            failed.Outcome.CorrelationId.Should().Be(fixture.AccountId.ToString("D"));
            failed.Outcome.OperationId.Should().Be(operationId);
            var error = logger.Entries.Should().ContainSingle(entry => entry.LogLevel == LogLevel.Error).Subject;
            AssertPrivateDiagnostic(error, fixture.AccountId, secret);
            error.Message.Should().Contain(Sha256Digest.ComputeUtf8(operationId))
                .And.Contain(nameof(IOException)).And.NotContain(operationId);
            var retained = await service.GetLatestAsync(fixture.AccountId, fixture.QueueScope);
            retained.Should().BeEquivalentTo(failed);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task AuthorityFailure_LogsOnlyFailureType_AndPreservesScopeRefusal()
    {
        const string secret = "confidential-authority-backend-detail";
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(root, includeSecurityLookup: true);
            var books = Substitute.For<ILedgerBookService>();
            books.ListBooksAsync(Arg.Any<LedgerBookQuery>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromException<IReadOnlyList<LedgerBookDto>>(
                    new InvalidOperationException($"{secret}: fund {fixture.AccountId:D}")));
            var logger = new RecordingLogger<ProviderLedgerReconciliationService>();
            var service = CreateLoggingService(fixture, logger, ledgerBooks: books);

            var refused = await service.RunAsync(fixture.AccountId, fixture.QueueScope);

            refused.Outcome!.State.Should().Be(OperationTerminalState.Blocked);
            refused.Outcome.Issues.Should().Contain(issue => issue.Code == "PROVIDER_RECONCILIATION_AUTHORITY_UNAVAILABLE");
            refused.Summary.AccountId.Should().Be(fixture.AccountId);
            var warning = logger.Entries.Should().ContainSingle().Subject;
            warning.LogLevel.Should().Be(LogLevel.Warning);
            AssertPrivateDiagnostic(warning, fixture.AccountId, secret);
            warning.Message.Should().Contain(nameof(InvalidOperationException));
            Directory.Exists(Path.Combine(root, "reconciliation")).Should().BeFalse();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task IntentUpdateFailure_LogsCorrelationAndTypeWithoutStorageScope_AndRetainsTerminalRun()
    {
        const string secret = "confidential-intent-storage-root";
        const string operationId = "provider-reconciliation-intent\r\nforged-logging";
        var root = Path.Combine(CreateTempRoot(), secret);
        Directory.CreateDirectory(root);
        try
        {
            var cases = new Dictionary<string, ReconciliationBreakQueueItem>(StringComparer.Ordinal);
            var repository = Substitute.For<IReconciliationBreakQueueRepository>();
            repository.GetAllAsync(Arg.Any<ReconciliationBreakQueueScope>(),
                    Arg.Any<ReconciliationBreakQueueStatus?>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult<IReadOnlyList<ReconciliationBreakQueueItem>>(cases.Values.ToArray()));
            repository.GetByIdAsync(Arg.Any<ReconciliationBreakQueueScope>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult(cases.GetValueOrDefault(call.ArgAt<string>(1))));
            repository.CreateIfMissingAsync(Arg.Any<ReconciliationBreakQueueScope>(),
                    Arg.Any<ReconciliationBreakQueueItem>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var item = call.ArgAt<ReconciliationBreakQueueItem>(1);
                    cases[item.BreakId] = item;
                    var attempts = Path.Combine(root, "reconciliation", item.FundAccountId!.Replace("-", string.Empty, StringComparison.Ordinal),
                        "operations", Sha256Digest.ComputeUtf8(operationId), "attempts");
                    if (Directory.Exists(attempts))
                    {
                        Directory.Delete(attempts, recursive: true);
                        File.WriteAllText(attempts, "Synthetic storage fault after the initial recovery anchor was retained.");
                    }
                    return Task.FromResult(true);
                });
            await using var fixture = await CreateFixtureAsync(root, includeSecurityLookup: true,
                internalCash: 49_900m, includeBreakQueue: true, breakQueueRepository: repository);
            var logger = new RecordingLogger<ProviderLedgerReconciliationService>();
            var service = CreateLoggingService(fixture, logger);

            var completed = await service.RunAsync(fixture.AccountId, fixture.QueueScope,
                new ProviderLedgerReconciliationRequestDto(OperationId: operationId));

            completed.Outcome!.State.Should().Be(OperationTerminalState.CompletedWithWarnings);
            completed.Summary.AccountId.Should().Be(fixture.AccountId);
            completed.Outcome.OperationId.Should().Be(operationId);
            cases.Should().NotBeEmpty();
            var error = logger.Entries.Should().ContainSingle(entry => entry.LogLevel == LogLevel.Error).Subject;
            AssertPrivateDiagnostic(error, fixture.AccountId, secret);
            error.Message.Should().Contain(Sha256Digest.ComputeUtf8(operationId))
                .And.Contain(nameof(IOException)).And.NotContain(operationId);
            var retained = await service.GetLatestAsync(fixture.AccountId, fixture.QueueScope);
            retained.Should().BeEquivalentTo(completed);
        }
        finally
        {
            DeleteTempRoot(Path.GetDirectoryName(root)!);
        }
    }

    private static ProviderLedgerReconciliationService CreateLoggingService(
        TestFixture fixture,
        RecordingLogger<ProviderLedgerReconciliationService> logger,
        IFundAccountService? accounts = null,
        ILedgerBookService? ledgerBooks = null)
        => new(fixture.BrokerageSync, accounts ?? fixture.AccountService,
            fixture.Services.GetRequiredService<BrokeragePortfolioSyncOptions>(), logger,
            securityReferenceLookup: fixture.Services.GetService<ISecurityReferenceLookup>(),
            breakQueueRepository: fixture.Services.GetService<IReconciliationBreakQueueRepository>(),
            ledgerBookService: ledgerBooks ?? fixture.Services.GetRequiredService<ILedgerBookService>(),
            fundProfileTenancyRegistry: fixture.Services.GetRequiredService<IFundProfileTenancyRegistry>());

    private static void AssertPrivateDiagnostic(RecordedLogEntry entry, Guid accountId, string secret)
    {
        entry.Exception.Should().BeNull("exception objects can retain confidential backend messages and scope");
        entry.Message.Should().NotContain(secret)
            .And.NotContain(accountId.ToString("D"))
            .And.NotContain(accountId.ToString("N"))
            .And.NotContain("\r").And.NotContain("\n");
    }
}
