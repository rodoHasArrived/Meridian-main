using System.Net;
using System.Net.Http;
using System.Text;
using CommunityToolkit.Mvvm.Input;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class CredentialManagementViewModelTests
{
    private const string Connections = """
        [{"connectionId":"paper-a","providerFamilyId":"alpaca","displayName":"Paper","tenantId":"tenant-a","externalAccountId":"account-a","credentialEnvironment":"paper"},
         {"connectionId":"live-b","providerFamilyId":"alpaca","displayName":"Live","tenantId":"tenant-a","externalAccountId":"account-b","credentialEnvironment":"live"}]
        """;

    [Fact]
    public void SelectionAndSave_KeepAccountScopeWhenAnOlderStatusCompletesLate()
    {
        WpfTestThread.Run(async () =>
        {
            var first = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            var second = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            string? savedQuery = null;
            string? savedBody = null;
            using var handler = new Handler(async request =>
            {
                if (request.Method == HttpMethod.Put)
                {
                    savedQuery = request.RequestUri!.Query;
                    savedBody = await request.Content!.ReadAsStringAsync();
                    return Json("{\"providerId\":\"alpaca\",\"credentialState\":3}");
                }
                if (request.RequestUri!.AbsolutePath == "/api/provider-routing/connections")
                    return Json(Connections);
                return await (request.RequestUri.Query.Contains("paper-a") ? first.Task : second.Task);
            });
            using var api = new ApiClientService(new Factory(handler));
            using var viewModel = new CredentialManagementViewModel(new SettingsConfigurationService(api), Meridian.Wpf.Services.NotificationService.Instance);
            await viewModel.LoadCredentialsAsync();
            viewModel.SelectedCredential.Should().BeNull();
            viewModel.RemoveCredentialCommand.CanExecute(null).Should().BeFalse();
            var paper = viewModel.Credentials.Single(row => row.ConnectionId == "paper-a");
            var live = viewModel.Credentials.Single(row => row.ConnectionId == "live-b");
            paper.DisplayName.Should().Contain("account-a").And.Contain("paper");
            live.DisplayName.Should().Contain("account-b").And.Contain("live");
            viewModel.SelectedCredential = paper;
            var paperLoad = viewModel.SelectionStatusLoad;
            viewModel.SelectedCredential = live;
            var liveLoad = viewModel.SelectionStatusLoad;
            second.SetResult(Json("[{\"providerId\":\"alpaca\",\"credentialState\":3}]"));
            await liveLoad;
            first.SetResult(Json("[{\"providerId\":\"alpaca\",\"credentialState\":1}]"));
            await paperLoad;
            viewModel.SelectedCredential.Should().BeSameAs(live);
            live.HasCredentials.Should().BeTrue();
            live.StatusText.Should().Contain("Configured");
            viewModel.EditCredentialCommand.Execute(null);
            viewModel.EditFields.Should().OnlyContain(field => field.Value == string.Empty);
            foreach (var field in viewModel.EditFields)
                field.Value = "replacement-value";
            await ((IAsyncRelayCommand)viewModel.SaveCredentialCommand).ExecuteAsync(null);
            savedQuery.Should().Be("?connectionId=live-b");
            savedBody.Should().Contain("KeyId").And.Contain("SecretKey");
            viewModel.SelectedCredential.Should().BeNull();
            viewModel.IsBusy.Should().BeFalse();
        });
    }

    [Fact]
    public void FailedDiscovery_ClearsPreviouslyEditableConnections()
    {
        WpfTestThread.Run(async () =>
        {
            var refused = false;
            using var handler = new Handler(request => Task.FromResult(
                request.RequestUri!.AbsolutePath == "/api/provider-routing/connections"
                    ? Json(Connections, refused ? HttpStatusCode.Forbidden : HttpStatusCode.OK)
                    : Json("[{\"providerId\":\"alpaca\",\"credentialState\":3}]")));
            using var api = new ApiClientService(new Factory(handler));
            using var viewModel = new CredentialManagementViewModel(new SettingsConfigurationService(api), Meridian.Wpf.Services.NotificationService.Instance);
            await viewModel.LoadCredentialsAsync();
            viewModel.SelectedCredential = viewModel.Credentials.First();
            await viewModel.SelectionStatusLoad;
            refused = true;
            await viewModel.LoadCredentialsAsync();
            viewModel.Credentials.Should().BeEmpty();
            viewModel.SelectedCredential.Should().BeNull();
            viewModel.RemoveCredentialCommand.CanExecute(null).Should().BeFalse();
            viewModel.StatusMessage.Should().Contain("unavailable");
        });
    }

    [Fact]
    public void PendingSave_DisablesConflictingCommandsAndRefusalPreservesRetryFields()
    {
        WpfTestThread.Run(async () =>
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var saved = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var handler = new Handler(request =>
            {
                if (request.Method == HttpMethod.Put)
                {
                    started.SetResult();
                    return saved.Task;
                }
                return Task.FromResult(Json(request.RequestUri!.AbsolutePath == "/api/provider-routing/connections"
                    ? Connections : "[{\"providerId\":\"alpaca\",\"credentialState\":1}]"));
            });
            using var api = new ApiClientService(new Factory(handler));
            using var viewModel = new CredentialManagementViewModel(new SettingsConfigurationService(api), Meridian.Wpf.Services.NotificationService.Instance);
            await viewModel.LoadCredentialsAsync();
            var selected = viewModel.Credentials.First();
            viewModel.SelectedCredential = selected;
            await viewModel.SelectionStatusLoad;
            viewModel.EditCredentialCommand.Execute(null);
            foreach (var field in viewModel.EditFields)
                field.Value = "retry-value";
            var pending = ((IAsyncRelayCommand)viewModel.SaveCredentialCommand).ExecuteAsync(null);
            await started.Task;
            viewModel.IsBusy.Should().BeTrue();
            viewModel.SaveCredentialCommand.CanExecute(null).Should().BeFalse();
            viewModel.RemoveCredentialCommand.CanExecute(null).Should().BeFalse();
            viewModel.TestCredentialCommand.CanExecute(null).Should().BeFalse();
            viewModel.TestAllCredentialsCommand.CanExecute(null).Should().BeFalse();
            viewModel.EditCredentialCommand.CanExecute(null).Should().BeFalse();
            saved.SetResult(Json("{}", HttpStatusCode.Forbidden));
            await pending;
            viewModel.IsBusy.Should().BeFalse();
            viewModel.SelectedCredential.Should().BeSameAs(selected);
            viewModel.IsEditPanelVisible.Should().BeTrue();
            viewModel.EditFields.Should().NotBeEmpty().And.OnlyContain(field => field.Value == "retry-value");
            viewModel.SaveCredentialCommand.CanExecute(null).Should().BeTrue();
            viewModel.RemoveCredentialCommand.CanExecute(null).Should().BeTrue();
        });
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

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
