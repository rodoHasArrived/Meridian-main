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
    // Preserve real notification suppression behavior without inheriting another fixture's
    // settings, deduplication window, or process-wide per-minute quota.
    private sealed class TestNotificationService : NotificationServiceBase { }

    private const string Connections = """
        [{"connectionId":"paper-a","providerFamilyId":"alpaca","displayName":"Paper","tenantId":"tenant-a","externalAccountId":"account-a","credentialEnvironment":"paper"},
         {"connectionId":"live-b","providerFamilyId":"alpaca","displayName":"Live","tenantId":"tenant-a","externalAccountId":"account-b","credentialEnvironment":"live"}]
        """;

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Verification_RetainsTheSelectedSchemaBeforeAndAfterVerification(
        bool testAll,
        bool statusCompletesDuringVerification)
    {
        WpfTestThread.Run(async () =>
        {
            const string configuredStatus = """
                [{"providerId":"alpaca","credentialState":3,"credentialFields":[
                  {"name":"KeyId","label":"Key ID","required":true,"inputKind":1},
                  {"name":"SecretKey","label":"Secret key","required":true,"inputKind":1}]}]
                """;
            const string verificationResult = """
                {"providerId":"alpaca","success":true,"verificationState":2,"lastVerifiedAt":"2026-09-28T12:00:00Z"}
                """;
            var statusBody = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var verificationBody = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var verificationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var verifiedConnections = new List<string>();
            using var handler = new Handler(async request =>
            {
                if (request.Method == HttpMethod.Post)
                {
                    verifiedConnections.Add(request.RequestUri!.Query);
                    if (request.RequestUri.Query.Contains("live-b"))
                    {
                        verificationStarted.TrySetResult();
                        return Json(await verificationBody.Task);
                    }

                    return Json(verificationResult);
                }

                if (request.RequestUri!.AbsolutePath == "/api/provider-routing/connections")
                    return Json(Connections);
                return Json(request.RequestUri.Query.Contains("live-b")
                    ? await statusBody.Task : configuredStatus);
            });
            using var api = new ApiClientService(new Factory(handler));
            using var viewModel = new CredentialManagementViewModel(new SettingsConfigurationService(api), new TestNotificationService());
            await viewModel.LoadCredentialsAsync();
            if (!testAll)
                viewModel.SelectedCredential = viewModel.Credentials.Single(row => row.ConnectionId == "live-b");
            var verification = ((IAsyncRelayCommand)(testAll ? viewModel.TestAllCredentialsCommand : viewModel.TestCredentialCommand)).ExecuteAsync(null);
            try
            {
                await verificationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                viewModel.SelectedCredential!.ConnectionId.Should().Be("live-b");
                if (statusCompletesDuringVerification)
                {
                    statusBody.SetResult(configuredStatus);
                    await viewModel.SelectionStatusLoad;
                    viewModel.SelectedCredential.IsTesting.Should().BeTrue();
                }

                verificationBody.SetResult(verificationResult);
                await verification;
                viewModel.SelectedCredential.StatusText.Should().Be("Verified");
                if (!statusCompletesDuringVerification)
                {
                    statusBody.SetResult(configuredStatus);
                    await viewModel.SelectionStatusLoad;
                }

                verifiedConnections.Should().Equal(testAll
                    ? new[] { "?connectionId=paper-a", "?connectionId=live-b" }
                    : new[] { "?connectionId=live-b" });
                viewModel.SelectedCredential.StatusText.Should().Be("Verified");
                viewModel.SelectedCredential.ServiceFields.Should().NotBeNull(
                    "verification must preserve the in-flight schema for the selected connection");
                viewModel.EditCredentialCommand.Execute(null);
                viewModel.EditFields.Select(field => field.FieldName).Should().Equal("KeyId", "SecretKey");
                viewModel.EditFields.Should().OnlyContain(field => field.Value == string.Empty);
            }
            finally
            {
                statusBody.TrySetResult(configuredStatus);
                verificationBody.TrySetResult(verificationResult);
                await verification;
                await viewModel.SelectionStatusLoad;
            }
        });
    }

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
            using var viewModel = new CredentialManagementViewModel(new SettingsConfigurationService(api), new TestNotificationService());
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
            second.SetResult(Json("[{\"providerId\":\"alpaca\",\"credentialState\":3,\"credentialFields\":[{\"name\":\"KeyId\",\"label\":\"Key ID\",\"required\":true,\"inputKind\":1},{\"name\":\"SecretKey\",\"label\":\"Secret key\",\"required\":true,\"inputKind\":1}]}]"));
            await liveLoad;
            first.SetResult(Json("[{\"providerId\":\"alpaca\",\"credentialState\":1,\"credentialFields\":[{\"name\":\"KeyId\",\"label\":\"Key ID\",\"required\":true,\"inputKind\":1},{\"name\":\"SecretKey\",\"label\":\"Secret key\",\"required\":true,\"inputKind\":1}]}]"));
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
                    : Json("[{\"providerId\":\"alpaca\",\"credentialState\":3,\"credentialFields\":[{\"name\":\"KeyId\",\"label\":\"Key ID\",\"required\":true,\"inputKind\":1},{\"name\":\"SecretKey\",\"label\":\"Secret key\",\"required\":true,\"inputKind\":1}]}]")));
            using var api = new ApiClientService(new Factory(handler));
            using var viewModel = new CredentialManagementViewModel(new SettingsConfigurationService(api), new TestNotificationService());
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
                    ? Connections : "[{\"providerId\":\"alpaca\",\"credentialState\":1,\"credentialFields\":[{\"name\":\"KeyId\",\"label\":\"Key ID\",\"required\":true,\"inputKind\":1},{\"name\":\"SecretKey\",\"label\":\"Secret key\",\"required\":true,\"inputKind\":1}]}]"));
            });
            using var api = new ApiClientService(new Factory(handler));
            using var viewModel = new CredentialManagementViewModel(new SettingsConfigurationService(api), new TestNotificationService());
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

    [Fact]
    public void Save_SubmitsOnlyFieldsTheOperatorFilledSoRetainedValuesSurvive()
    {
        WpfTestThread.Run(async () =>
        {
            var puts = new List<string>();
            using var handler = new Handler(async request =>
            {
                if (request.Method == HttpMethod.Put)
                {
                    puts.Add(await request.Content!.ReadAsStringAsync());
                    return Json("{\"providerId\":\"alpaca\",\"credentialState\":3}");
                }
                return Json(request.RequestUri!.AbsolutePath == "/api/provider-routing/connections"
                    ? Connections : "[{\"providerId\":\"alpaca\",\"credentialState\":3,\"credentialFields\":[{\"name\":\"KeyId\",\"label\":\"Key ID\",\"required\":true,\"inputKind\":1},{\"name\":\"SecretKey\",\"label\":\"Secret key\",\"required\":true,\"inputKind\":1}]}]");
            });
            using var api = new ApiClientService(new Factory(handler));
            using var viewModel = new CredentialManagementViewModel(new SettingsConfigurationService(api), new TestNotificationService());
            await viewModel.LoadCredentialsAsync();
            viewModel.SelectedCredential = viewModel.Credentials.Single(row => row.ConnectionId == "paper-a");
            await viewModel.SelectionStatusLoad;

            viewModel.EditCredentialCommand.Execute(null);
            await ((IAsyncRelayCommand)viewModel.SaveCredentialCommand).ExecuteAsync(null);
            puts.Should().BeEmpty("an untouched editor must not submit blanks that the vault treats as deletions");
            viewModel.IsEditPanelVisible.Should().BeTrue();

            viewModel.EditFields.Single(field => field.FieldName.Contains("Secret", StringComparison.OrdinalIgnoreCase)).Value = "rotated-secret";
            await ((IAsyncRelayCommand)viewModel.SaveCredentialCommand).ExecuteAsync(null);
            puts.Should().ContainSingle();
            puts[0].Should().Contain("SecretKey").And.Contain("rotated-secret");
            puts[0].Should().NotContain("KeyId", "the untouched key ID must be retained, not cleared");
        });
    }

    [Fact]
    public void Save_ReportsAPartialRecordAsSavedButIncompleteRatherThanFailed()
    {
        WpfTestThread.Run(async () =>
        {
            var notifications = new List<Meridian.Ui.Services.Services.NotificationEventArgs>();
            void Capture(object? sender, Meridian.Ui.Services.Services.NotificationEventArgs args) => notifications.Add(args);
            using var handler = new Handler(request => Task.FromResult(request.Method == HttpMethod.Put
                ? Json("{\"providerId\":\"alpaca\",\"credentialState\":2}")
                : Json(request.RequestUri!.AbsolutePath == "/api/provider-routing/connections"
                    ? Connections : "[{\"providerId\":\"alpaca\",\"credentialState\":1,\"credentialFields\":[{\"name\":\"KeyId\",\"label\":\"Key ID\",\"required\":true,\"inputKind\":1},{\"name\":\"SecretKey\",\"label\":\"Secret key\",\"required\":true,\"inputKind\":1}]}]")));
            using var api = new ApiClientService(new Factory(handler));
            var notificationService = new TestNotificationService();
            using var viewModel = new CredentialManagementViewModel(new SettingsConfigurationService(api), notificationService);
            notificationService.NotificationReceived += Capture;
            try
            {
                await viewModel.LoadCredentialsAsync();
                viewModel.SelectedCredential = viewModel.Credentials.Single(row => row.ConnectionId == "paper-a");
                await viewModel.SelectionStatusLoad;
                viewModel.EditCredentialCommand.Execute(null);
                viewModel.EditFields.Single(field => field.FieldName == "KeyId").Value = "only-the-key";

                await ((IAsyncRelayCommand)viewModel.SaveCredentialCommand).ExecuteAsync(null);

                viewModel.IsEditPanelVisible.Should().BeFalse("the partial record was durably saved");
                notifications.Should().Contain(item => item.Title == "Credentials Incomplete" && item.Type == NotificationType.Warning);
                notifications.Should().NotContain(item => item.Title == "Save Failed");
            }
            finally
            {
                notificationService.NotificationReceived -= Capture;
            }
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Edit_UsesTheServiceFieldSchemaInsteadOfTheLocalCatalog(bool schemaReported)
    {
        WpfTestThread.Run(async () =>
        {
            // The server's vault schema deliberately differs from the local catalog's KeyId/SecretKey,
            // the way Tiingo's vault field is ApiKey while the local catalog calls it Token.
            var status = schemaReported
                ? "[{\"providerId\":\"alpaca\",\"credentialState\":1,\"credentialFields\":[{\"name\":\"ApiKey\",\"label\":\"API key\",\"required\":true,\"inputKind\":1}]}]"
                : "[{\"providerId\":\"alpaca\",\"credentialState\":1}]";
            var puts = new List<string>();
            using var handler = new Handler(async request =>
            {
                if (request.Method == HttpMethod.Put)
                {
                    puts.Add(await request.Content!.ReadAsStringAsync());
                    return Json("{\"providerId\":\"alpaca\",\"credentialState\":3}");
                }
                return Json(request.RequestUri!.AbsolutePath == "/api/provider-routing/connections" ? Connections : status);
            });
            using var api = new ApiClientService(new Factory(handler));
            using var viewModel = new CredentialManagementViewModel(new SettingsConfigurationService(api), new TestNotificationService());
            await viewModel.LoadCredentialsAsync();
            viewModel.SelectedCredential = viewModel.Credentials.First();
            await viewModel.SelectionStatusLoad;

            viewModel.EditCredentialCommand.Execute(null);
            foreach (var field in viewModel.EditFields)
                field.Value = "entered-value";
            await ((IAsyncRelayCommand)viewModel.SaveCredentialCommand).ExecuteAsync(null);

            if (schemaReported)
            {
                viewModel.EditFields.Should().BeEmpty("a confirmed save closes the editor");
                puts.Should().ContainSingle().Which.Should().Contain("ApiKey").And.NotContain("KeyId");
            }
            else
            {
                viewModel.EditFields.Should().OnlyContain(field => field.FieldName == string.Empty,
                    "without a service schema there are no field names the vault would accept");
                puts.Should().BeEmpty();
            }
        });
    }

    [Fact]
    public void Edit_BeforeTheStatusReadCompletes_RebuildsTheEditorWhenTheSchemaArrives()
    {
        WpfTestThread.Run(async () =>
        {
            var status = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var handler = new Handler(async request =>
            {
                if (request.RequestUri!.AbsolutePath == "/api/provider-routing/connections")
                    return Json(Connections);
                var response = await status.Task;
                return Json(await response.Content.ReadAsStringAsync());
            });
            using var api = new ApiClientService(new Factory(handler));
            using var viewModel = new CredentialManagementViewModel(new SettingsConfigurationService(api), new TestNotificationService());
            await viewModel.LoadCredentialsAsync();
            viewModel.SelectedCredential = viewModel.Credentials.First();

            viewModel.EditCredentialCommand.Execute(null);
            viewModel.IsEditPanelVisible.Should().BeTrue();
            viewModel.EditFields.Should().OnlyContain(field => field.FieldName == string.Empty,
                "no field names are known until the service reports the schema");

            status.SetResult(Json("[{\"providerId\":\"alpaca\",\"credentialState\":1,\"credentialFields\":[{\"name\":\"ApiKey\",\"label\":\"API key\",\"required\":true,\"inputKind\":1}]}]"));
            await viewModel.SelectionStatusLoad;

            viewModel.IsEditPanelVisible.Should().BeTrue();
            viewModel.EditFields.Should().ContainSingle().Which.FieldName.Should().Be("ApiKey",
                "the open editor must be rebuilt once the schema arrives instead of staying inert");
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
