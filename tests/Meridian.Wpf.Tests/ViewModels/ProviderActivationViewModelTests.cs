using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Configuration;
using Meridian.Ui.Services;
using Meridian.Ui.Services.ProviderDiagnostics;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class ProviderActivationViewModelTests
{
    [Fact]
    public async Task ConfirmRestart_WhenPendingPathsChanged_RequiresAnotherReview()
    {
        var reads = 0;
        var posts = 0;
        using var api = new ApiClientService(new Factory(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                posts++;
                return new HttpResponseMessage(HttpStatusCode.Accepted);
            }

            return ActivationResponse(canRestart: true, ++reads == 1 ? "Alpaca.Feed" : "ProviderQuotas.TwelveData");
        }));
        var viewModel = new ProviderActivationViewModel(new ProviderConfigurationApiClient(api), _ => Task.CompletedTask);

        await viewModel.RefreshAsync();
        viewModel.RequestRestartCommand.Execute(null);
        Assert.True(viewModel.IsConfirming);
        Assert.Equal("Alpaca.Feed", Assert.Single(viewModel.PendingPaths));
        await viewModel.ConfirmRestartCommand.ExecuteAsync(null);

        Assert.Equal(0, posts);
        Assert.False(viewModel.IsConfirming);
        Assert.Equal("ProviderQuotas.TwelveData", Assert.Single(viewModel.PendingPaths));
        Assert.Contains("Review", viewModel.Message);
    }

    [Fact]
    public async Task Refresh_UnsupervisedHostShowsInstructionsAndKeepsRestartUnavailable()
    {
        using var api = new ApiClientService(new Factory(_ => ActivationResponse(canRestart: false, "Alpaca.Feed")));
        var viewModel = new ProviderActivationViewModel(new ProviderConfigurationApiClient(api), _ => Task.CompletedTask);

        await viewModel.RefreshAsync();

        Assert.False(viewModel.RequestRestartCommand.CanExecute(null));
        Assert.Contains("Restart Meridian manually", viewModel.Message);
    }

    private static HttpResponseMessage ActivationResponse(bool canRestart, string path)
    {
        var response = new ProviderActivationResponse([], canRestart, [path], "Restart Meridian manually.", "session-a", IsReady: true);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(response, DesktopJsonOptions.Api), Encoding.UTF8, "application/json")
        };
    }

    private sealed class Factory(Func<HttpRequestMessage, HttpResponseMessage> send) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(send));
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(send(request));
    }
}
