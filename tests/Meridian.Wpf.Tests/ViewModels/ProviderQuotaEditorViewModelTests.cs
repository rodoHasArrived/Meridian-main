using Meridian.Contracts.Api;
using Meridian.Ui.Services.ProviderDiagnostics;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class ProviderQuotaEditorViewModelTests
{
    [Fact]
    public async Task PaidTierRequiresDailyCapAndShowsTheRunningLimitsAfterSave()
    {
        var client = new StubClient();
        var viewModel = new ProviderQuotaEditorViewModel(client);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.Tier = viewModel.Tiers.Single(tier => tier.Id == "grow");
        viewModel.SaveCommand.CanExecute(null).Should().BeFalse();
        viewModel.Daily = "3500";
        viewModel.Pacing = "30";
        await viewModel.SaveCommand.ExecuteAsync(null);

        client.LastEdit.Should().Be(new ProviderQuotaEditDto("grow", 55, 3500, 30));
        viewModel.SavedLimits.Should().Contain("3500 credits/day");
        viewModel.RunningLimits.Should().Contain("800 credits/day");
        viewModel.Notice.Should().Contain("after host restart");
        viewModel.DailyRollups.Should().ContainSingle().Which.ThrottleCount.Should().Be(2);
    }

    [Fact]
    public async Task RejectedSaveRetainsTheDraft()
    {
        var client = new StubClient { Reject = true };
        var viewModel = new ProviderQuotaEditorViewModel(client);
        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.Daily = "500";
        await viewModel.SaveCommand.ExecuteAsync(null);

        viewModel.Error.Should().Be("Server validation rejected the edit.");
        viewModel.Daily.Should().Be("500");
        viewModel.SavedLimits.Should().Contain("800 credits/day");
    }

    private sealed class StubClient : IProviderQuotaApiClient
    {
        private static readonly ProviderQuotaResponse Basic = new("twelvedata", "Twelve Data", "credits", "utc-midnight",
            Editable: true, CanEdit: true, "account-quota", new("free", 8, 800, 8), new(8, 60, 800, 7.5), false,
            [new("free", "Basic", 8, 800, 800), new("grow", "Grow", 55, 79_200, null)]);

        public bool Reject { get; init; }
        public ProviderQuotaEditDto? LastEdit { get; private set; }

        public Task<ProviderQuotaResponse> ReadAsync(string providerId, CancellationToken ct = default)
            => Task.FromResult(Basic);

        public Task<ProviderQuotaResponse> SaveAsync(string providerId, ProviderQuotaEditDto edit, CancellationToken ct = default)
        {
            LastEdit = edit;
            return Reject
                ? Task.FromException<ProviderQuotaResponse>(new InvalidOperationException("Server validation rejected the edit."))
                : Task.FromResult(Basic with
                {
                    Saved = new(edit.TierId, edit.UnitsPerMinute, edit.DailyLimit, edit.RequestsPerMinute), PendingRestart = true
                });
        }

        public Task<ProviderRateLimitHistoryResponse> ReadHistoryAsync(string providerId, CancellationToken ct = default)
            => Task.FromResult(new ProviderRateLimitHistoryResponse(providerId, 24, [], DateTimeOffset.UtcNow, true, "")
            {
                DailyRollupsAvailable = true,
                DailyRollups = [new(new DateOnly(2026, 11, 20), 2, 1)]
            });
    }
}
