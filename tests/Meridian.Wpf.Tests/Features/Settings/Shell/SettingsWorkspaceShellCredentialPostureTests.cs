using System.Net;
using System.Net.Http;
using System.Text;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Features.Settings.Shell;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Tests.Features.Settings.Shell;

public sealed class SettingsWorkspaceShellCredentialPostureTests
{
    [Fact]
    public async Task LoadAsync_RefusedStatusRead_CountsProvidersAsUnavailableNotMissing()
    {
        using var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        }));
        using var api = new ApiClientService(new Factory(handler));
        var service = new SettingsWorkspaceShellSnapshotService(new SettingsConfigurationService(api));

        var snapshot = await service.LoadAsync();

        snapshot.ProviderCount.Should().BeGreaterThan(0);
        snapshot.UnavailableCredentialCount.Should().Be(snapshot.ProviderCount);
        snapshot.MissingCredentialCount.Should().Be(0, "a refused read is missing evidence, not an asserted credential gap");
        snapshot.ConfiguredCredentialCount.Should().Be(0);
    }

    [Fact]
    public void Build_AllStatusesUnavailable_ReportsUnknownPostureInsteadOfSetupGaps()
    {
        var presentation = new SettingsWorkspaceShellPresentationService().Build(new SettingsWorkspaceShellSnapshot
        {
            ProviderCount = 4,
            UnavailableCredentialCount = 4
        });

        presentation.HeroBadgeText.Should().Be("Unknown");
        presentation.HeroBadgeTone.Should().Be(WorkspaceTone.Warning);
        presentation.HeroFocusText.Should().Be("Credential status unavailable");
        presentation.HeroSummaryText.Should().Contain("could not be read").And.NotContain("need setup");
        presentation.Context.ReviewStateValue.Should().Be("Status unavailable");
        presentation.Context.CriticalValue.Should().Be("Status unavailable");
        presentation.OperationsItems[0].StatusLabel.Should().Be("Status unavailable");
    }

    [Fact]
    public void Build_MissingAndUnavailable_ReportsBothWithoutMergingThem()
    {
        var presentation = new SettingsWorkspaceShellPresentationService().Build(new SettingsWorkspaceShellSnapshot
        {
            ProviderCount = 5,
            ConfiguredCredentialCount = 2,
            MissingCredentialCount = 1,
            UnavailableCredentialCount = 2
        });

        presentation.HeroBadgeText.Should().Be("Review");
        presentation.HeroSummaryText.Should().Contain("1 provider credential path(s) need setup")
            .And.Contain("2 provider credential status(es) could not be read");
        presentation.Context.CriticalValue.Should().Be("2/5 ready");
    }

    [Fact]
    public void Build_AllConfigured_RemainsReady()
    {
        var presentation = new SettingsWorkspaceShellPresentationService().Build(new SettingsWorkspaceShellSnapshot
        {
            ProviderCount = 3,
            ConfiguredCredentialCount = 3
        });

        presentation.HeroBadgeText.Should().Be("Ready");
        presentation.HeroBadgeTone.Should().Be(WorkspaceTone.Success);
        presentation.Context.ReviewStateValue.Should().Be("Ready");
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request);
    }
}
