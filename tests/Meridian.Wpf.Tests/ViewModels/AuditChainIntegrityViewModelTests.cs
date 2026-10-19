using Meridian.Contracts.Ledger;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels.Accounting;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class AuditChainIntegrityViewModelTests
{
    [Fact]
    public void DoesNothingUntilAsked_BecauseVerificationWalksEveryChain()
    {
        var client = new StubClient(new AuditIntegrityCheckResult(AuditIntegrityCheckOutcome.Busy));

        var viewModel = new AuditChainIntegrityViewModel(client);

        client.Calls.Should().Be(0);
        viewModel.Rows.Should().BeEmpty();
        viewModel.VerifyCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task ShowsEachChainsVerdict_AndABreakOverridesAVerifiedChain()
    {
        var viewModel = new AuditChainIntegrityViewModel(new StubClient(Completed(false,
            new(AuditChainIdentifiers.AccountingActions, AuditChainIntegrityStatus.Broken, 2, 0, "BrokenLink", null, 2),
            new(AuditChainIdentifiers.LedgerEvents, AuditChainIntegrityStatus.Verified, 40, 3))));

        await viewModel.VerifyAsync();

        viewModel.VerdictTone.Should().Be("danger");
        viewModel.VerdictTitle.Should().Be("Audit chain integrity failure");
        viewModel.Rows.Should().HaveCount(2);
        viewModel.Rows[0].Name.Should().Be("Accounting actions");
        viewModel.Rows[0].Evidence.Should().Be("BrokenLink at sequence 2");
        viewModel.Rows[1].Detail.Should().Be("40 chained entries checked; 3 earlier records predate chaining and are not covered");
    }

    [Fact]
    public async Task NeverRollsAnUncheckedChainIntoAPassingVerdict()
    {
        var viewModel = new AuditChainIntegrityViewModel(new StubClient(Completed(false,
            new(AuditChainIdentifiers.AccountingActions, AuditChainIntegrityStatus.Verified, 5, 0),
            new(AuditChainIdentifiers.LedgerEvents, AuditChainIntegrityStatus.Unavailable, 0, 0, "UnsupportedSchemaVersion"))));

        await viewModel.VerifyAsync();

        viewModel.VerdictTone.Should().Be("warning");
        viewModel.VerdictTitle.Should().Be("Audit chains partly checked");
        viewModel.Rows[1].Status.Should().Be("Not checked — store unavailable");
    }

    [Fact]
    public async Task DoesNotCallARunPartlyCheckedWhenNoChainVerified()
    {
        var viewModel = new AuditChainIntegrityViewModel(new StubClient(Completed(false,
            new AuditChainIntegrityResultDto(AuditChainIdentifiers.AccountingActions, AuditChainIntegrityStatus.Unavailable, 0, 0, "IOException"),
            new AuditChainIntegrityResultDto(AuditChainIdentifiers.LedgerEvents, AuditChainIntegrityStatus.NotConfigured, 0, 0))));

        await viewModel.VerifyAsync();

        viewModel.VerdictTone.Should().Be("warning");
        viewModel.VerdictTitle.Should().Be("No audit chain verified");
        viewModel.StatusText.Should().StartWith("Verification ran");
    }

    [Fact]
    public async Task DoesNotCertifyAChainThatHasNotBegunChaining()
    {
        var viewModel = new AuditChainIntegrityViewModel(new StubClient(Completed(false,
            new AuditChainIntegrityResultDto(AuditChainIdentifiers.AccountingActions, AuditChainIntegrityStatus.NotStarted, 0, 4,
                Detail: "Chaining has not begun in this store, so no record is protected yet."),
            new AuditChainIntegrityResultDto(AuditChainIdentifiers.LedgerEvents, AuditChainIntegrityStatus.Verified, 9, 0))));

        await viewModel.VerifyAsync();

        viewModel.VerdictTone.Should().Be("warning");
        viewModel.VerdictTitle.Should().Be("Audit chains partly checked");
        viewModel.VerdictDetail.Should().Be("Accounting actions has not begun chaining, so none of its records are protected yet.");
        viewModel.Rows[0].Status.Should().Be("Nothing chained yet");
        viewModel.Rows[0].Detail.Should().Be("0 chained entries checked; 4 earlier records predate chaining and are not covered");
    }

    [Fact]
    public async Task ReportsEveryConfiguredChainVerified()
    {
        var viewModel = new AuditChainIntegrityViewModel(new StubClient(Completed(true,
            new(AuditChainIdentifiers.AccountingActions, AuditChainIntegrityStatus.Verified, 1, 0),
            new(AuditChainIdentifiers.LedgerEvents, AuditChainIntegrityStatus.NotConfigured, 0, 0, Detail: "No durable ledger journal store is composed."))));

        await viewModel.VerifyAsync();

        viewModel.VerdictTone.Should().Be("success");
        viewModel.VerdictDetail.Should().Be("Every configured audit chain verified (1 of 2).");
        viewModel.Rows[0].Detail.Should().Be("1 chained entry checked");
        viewModel.Rows[1].Detail.Should().Be("No durable ledger journal store is composed.");
    }

    [Theory]
    [InlineData(AuditIntegrityCheckOutcome.Forbidden, "Requires maintenance authority")]
    [InlineData(AuditIntegrityCheckOutcome.Busy, "Verification already running")]
    [InlineData(AuditIntegrityCheckOutcome.RateLimited, "Rate limited")]
    [InlineData(AuditIntegrityCheckOutcome.Failed, "Audit integrity check failed")]
    public async Task ExplainsWhyNothingWasChecked_WithoutInventingAVerdict(AuditIntegrityCheckOutcome outcome, string status)
    {
        var viewModel = new AuditChainIntegrityViewModel(new StubClient(new AuditIntegrityCheckResult(outcome, Error: "HTTP 503")));

        await viewModel.VerifyAsync();

        viewModel.StatusText.Should().StartWith(status);
        viewModel.VerdictTitle.Should().BeEmpty();
        viewModel.Rows.Should().BeEmpty();
        viewModel.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task WithdrawsThePreviousVerdictWhileAFreshVerificationRuns()
    {
        var client = new SequenceClient(Completed(true,
            new AuditChainIntegrityResultDto(AuditChainIdentifiers.AccountingActions, AuditChainIntegrityStatus.Verified, 4, 0)));
        var viewModel = new AuditChainIntegrityViewModel(client);
        await viewModel.VerifyAsync();
        viewModel.VerdictTitle.Should().Be("Audit chains verified");

        var rerun = viewModel.VerifyAsync();

        viewModel.IsBusy.Should().BeTrue();
        viewModel.VerdictTitle.Should().BeEmpty();
        viewModel.Rows.Should().BeEmpty();
        viewModel.Cancel();
        await rerun;
    }

    [Fact]
    public async Task CancelAbandonsARunningVerification()
    {
        var client = new BlockingClient();
        var viewModel = new AuditChainIntegrityViewModel(client);

        var run = viewModel.VerifyAsync();
        viewModel.IsBusy.Should().BeTrue();
        viewModel.Cancel();
        await run;

        client.Token.IsCancellationRequested.Should().BeTrue();
        viewModel.IsBusy.Should().BeFalse();
        viewModel.Rows.Should().BeEmpty();
        viewModel.StatusText.Should().NotStartWith("Audit integrity check failed");
    }

    [Fact]
    public void IsUnavailableWithoutAClient()
    {
        var viewModel = new AuditChainIntegrityViewModel(client: null);

        viewModel.VerifyCommand.CanExecute(null).Should().BeFalse();
        viewModel.StatusText.Should().Contain("unavailable");
    }

    private static AuditIntegrityCheckResult Completed(bool allVerified, params AuditChainIntegrityResultDto[] chains)
        => new(AuditIntegrityCheckOutcome.Completed, new AccountingAuditIntegrityReportDto(DateTimeOffset.UnixEpoch, allVerified, chains));

    private sealed class StubClient(AuditIntegrityCheckResult result) : IAccountingAuditIntegrityApiClient
    {
        public int Calls { get; private set; }

        public Task<AuditIntegrityCheckResult> VerifyAsync(CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    /// <summary>Answers the first request, then blocks until cancelled.</summary>
    private sealed class SequenceClient(AuditIntegrityCheckResult first) : IAccountingAuditIntegrityApiClient
    {
        private int _calls;

        public async Task<AuditIntegrityCheckResult> VerifyAsync(CancellationToken ct = default)
        {
            if (_calls++ == 0)
            {
                return first;
            }

            await Task.Delay(Timeout.Infinite, ct);
            return first;
        }
    }

    private sealed class BlockingClient : IAccountingAuditIntegrityApiClient
    {
        public CancellationToken Token { get; private set; }

        public async Task<AuditIntegrityCheckResult> VerifyAsync(CancellationToken ct = default)
        {
            Token = ct;
            await Task.Delay(Timeout.Infinite, ct);
            return new AuditIntegrityCheckResult(AuditIntegrityCheckOutcome.Failed);
        }
    }
}
