using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows.Controls;
using Meridian.Contracts.Configuration;
using ProviderIdentity = Meridian.Infrastructure.Adapters.Core.ProviderIdentity;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Views;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Views;

public sealed class AddProviderWizardPageTests
{
    [Theory]
    [InlineData("alpaca")]
    [InlineData("nasdaqdatalink")]
    public void TestThenSave_PreservesVerificationWithoutReplacingCredentials(string providerId)
    {
        WpfTestThread.Run(async () =>
        {
            using var fixture = await Fixture.CreateAsync(providerId);
            fixture.EnterCredentials();

            await fixture.Page.TestProviderConnectionAsync();

            fixture.Key.Text.Should().BeEmpty();
            fixture.Secret.Secret.Should().BeEmpty();
            fixture.ViewModel.ConnectionTestStatusText.Should().Contain("Credentials verified");

            await fixture.Page.SaveProviderAsync();

            fixture.Writes.Should().ContainSingle();
            fixture.Writes[0].ProviderId.Should().Be(ProviderIdentity.NormalizeId(providerId));
            fixture.VerificationCalls.Should().Be(1);
            fixture.StatusQueries.Should().HaveCount(2).And.OnlyContain(query => query == "?scope=provider");
            fixture.ViewModel.ConnectionTestStatusText.Should().Contain("Credentials verified");
            fixture.ViewModel.SaveStatusText.Should().Contain("configured successfully");
            fixture.Records[ProviderIdentity.NormalizeId(providerId)].Verified.Should().BeTrue();
        });
    }

    [Fact]
    public void TestThenEditThenSave_PersistsRotationAndRequiresAnotherTest()
    {
        WpfTestThread.Run(async () =>
        {
            using var fixture = await Fixture.CreateAsync();
            fixture.EnterCredentials();
            await fixture.Page.TestProviderConnectionAsync();

            fixture.Secret.Secret = "rotated-fixture-secret";
            fixture.ViewModel.ConnectionTestStatusText.Should().Contain("Credentials changed");
            await fixture.Page.SaveProviderAsync();

            fixture.Writes.Should().HaveCount(2);
            fixture.Writes[1].Fields.Should().ContainSingle().Which.Key.Should().Be("SecretKey");
            fixture.ViewModel.ConnectionTestStatusText.Should().Contain("not verified");
            fixture.Records["alpaca"].Verified.Should().BeFalse();

            await fixture.Page.TestProviderConnectionAsync();
            await fixture.Page.SaveProviderAsync();

            fixture.Writes.Should().HaveCount(2);
            fixture.VerificationCalls.Should().Be(2);
            fixture.ViewModel.ConnectionTestStatusText.Should().Contain("Credentials verified");
        });
    }

    [Fact]
    public void ProviderSwitch_DoesNotReuseThePreviousProvidersVerifiedState()
    {
        WpfTestThread.Run(async () =>
        {
            using var fixture = await Fixture.CreateAsync();
            fixture.EnterCredentials();
            await fixture.Page.TestProviderConnectionAsync();

            fixture.Page.SelectProvider("polygon");
            fixture.ViewModel.ConnectionTestStatusText.Should().Be("Not tested yet");
            await fixture.Page.SaveProviderAsync();

            fixture.Writes.Should().ContainSingle();
            fixture.ViewModel.SaveStatusText.Should().Contain("Failed to save");
            fixture.ViewModel.ConnectionTestStatusText.Should().NotContain("Credentials verified");
        });
    }

    [Fact]
    public void EditsDuringTest_PreserveNewValuesAndRejectOverlappingCommands()
    {
        WpfTestThread.Run(async () =>
        {
            using var fixture = await Fixture.CreateAsync();
            fixture.EnterCredentials();
            fixture.PendingWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var test = fixture.Page.TestProviderConnectionAsync();
            await fixture.WriteStarted.Task;

            fixture.Secret.Secret = "newer-fixture-secret";
            fixture.Page.SelectProvider("polygon");
            await fixture.Page.SaveProviderAsync();
            await fixture.Page.TestProviderConnectionAsync();
            fixture.ViewModel.SelectedProviderName.Should().Be("Alpaca Markets");
            fixture.Writes.Should().ContainSingle();

            fixture.PendingWrite.SetResult();
            await test;

            fixture.Key.Text.Should().BeEmpty();
            fixture.Secret.Secret.Should().Be("newer-fixture-secret");
            fixture.ViewModel.ConnectionTestStatusText.Should().Contain("Credentials changed");
            fixture.PendingWrite = null;
            await fixture.Page.SaveProviderAsync();

            fixture.Writes.Should().HaveCount(2);
            fixture.Writes[1].Fields.Should().ContainSingle().Which.Value.Should().Be("newer-fixture-secret");
            fixture.ViewModel.ConnectionTestStatusText.Should().Contain("not verified");
        });
    }

    [Fact]
    public void EditsDuringVerification_DoNotInheritTheEarlierCredentialsTest()
    {
        WpfTestThread.Run(async () =>
        {
            using var fixture = await Fixture.CreateAsync();
            fixture.EnterCredentials();
            fixture.PendingVerification = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var test = fixture.Page.TestProviderConnectionAsync();
            await fixture.VerificationStarted.Task;

            fixture.Secret.Secret = "edited-during-verification";
            fixture.PendingVerification.SetResult();
            await test;

            fixture.Secret.Secret.Should().Be("edited-during-verification");
            fixture.ViewModel.ConnectionTestStatusText.Should().Contain("Credentials changed");
        });
    }

    [Fact]
    public void EditsDuringSave_RemainUnsavedUntilTheNextSave()
    {
        WpfTestThread.Run(async () =>
        {
            using var fixture = await Fixture.CreateAsync();
            fixture.EnterCredentials();
            fixture.PendingWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var save = fixture.Page.SaveProviderAsync();
            await fixture.WriteStarted.Task;

            fixture.Key.Text = "newer-fixture-key";
            fixture.PendingWrite.SetResult();
            await save;

            fixture.Key.Text.Should().Be("newer-fixture-key");
            fixture.Secret.Secret.Should().BeEmpty();
            fixture.ViewModel.SaveStatusText.Should().Contain("New credential edits remain unsaved");
            fixture.ViewModel.ConnectionTestStatusText.Should().Contain("Credentials changed");

            fixture.PendingWrite = null;
            await fixture.Page.SaveProviderAsync();

            fixture.Writes.Should().HaveCount(2);
            fixture.Writes[1].Fields.Should().ContainSingle().Which.Value.Should().Be("newer-fixture-key");
            fixture.Key.Text.Should().BeEmpty();
        });
    }

    [Fact]
    public void FailedVerification_KeepsConfirmedPersistenceWithoutClaimingReadiness()
    {
        WpfTestThread.Run(async () =>
        {
            using var fixture = await Fixture.CreateAsync();
            fixture.EnterCredentials();
            fixture.FailVerification = true;

            await fixture.Page.TestProviderConnectionAsync();
            await fixture.Page.SaveProviderAsync();

            fixture.Writes.Should().ContainSingle();
            fixture.Key.Text.Should().BeEmpty();
            fixture.Secret.Secret.Should().BeEmpty();
            fixture.ViewModel.ConnectionTestStatusText.Should().Contain("not verified");
            fixture.ViewModel.SaveStatusText.Should().Contain("configured successfully");
        });
    }

    [Fact]
    public void RefusedSave_RetainsEditorsAndAllowsRetry()
    {
        WpfTestThread.Run(async () =>
        {
            using var fixture = await Fixture.CreateAsync();
            fixture.EnterCredentials();
            fixture.RefuseWrites = true;

            await fixture.Page.TestProviderConnectionAsync();

            fixture.Key.Text.Should().Be("fixture-key");
            fixture.Secret.Secret.Should().Be("fixture-secret");
            fixture.VerificationCalls.Should().Be(0);
            fixture.ViewModel.ConnectionTestStatusText.Should().Contain("refused");

            fixture.RefuseWrites = false;
            await fixture.Page.TestProviderConnectionAsync();
            await fixture.Page.SaveProviderAsync();

            fixture.Writes.Should().HaveCount(2);
            fixture.ViewModel.ConnectionTestStatusText.Should().Contain("Credentials verified");
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Save_RechecksRetainedStatusInsteadOfKeepingStaleTestSuccess(bool configured, bool verifiedWithoutDate)
    {
        WpfTestThread.Run(async () =>
        {
            using var fixture = await Fixture.CreateAsync();
            fixture.EnterCredentials();
            await fixture.Page.TestProviderConnectionAsync();
            fixture.Records["alpaca"] = new(configured, verifiedWithoutDate, null);

            await fixture.Page.SaveProviderAsync();

            fixture.Writes.Should().ContainSingle();
            fixture.ViewModel.ConnectionTestStatusText.Should().NotContain("Credentials verified");
            if (!configured)
                fixture.ViewModel.SaveStatusText.Should().Contain("Failed to save");
            else
                fixture.ViewModel.ConnectionTestStatusText.Should().Contain("not verified");
        });
    }

    [Fact]
    public void UnavailableStatusAfterTest_FailsClosedWithoutReplacingVerifiedCredentials()
    {
        WpfTestThread.Run(async () =>
        {
            using var fixture = await Fixture.CreateAsync();
            fixture.EnterCredentials();
            await fixture.Page.TestProviderConnectionAsync();
            fixture.RefuseStatus = true;

            await fixture.Page.SaveProviderAsync();

            fixture.Writes.Should().ContainSingle();
            fixture.ViewModel.SaveStatusText.Should().Contain("Failed to save");
            fixture.ViewModel.ConnectionTestStatusText.Should().NotContain("Credentials verified");
        });
    }

    private sealed record RetainedCredentials(bool Configured, bool Verified, DateTimeOffset? LastVerifiedAt);
    private sealed record Write(string ProviderId, Dictionary<string, string?> Fields);

    private sealed class Fixture : IDisposable
    {
        private readonly Handler _handler;
        private readonly ApiClientService _api;

        private Fixture()
        {
            _handler = new Handler(SendAsync);
            _api = new ApiClientService(new Factory(_handler));
            Page = new AddProviderWizardPage(WpfServices.NavigationService.Instance,
                WpfServices.NotificationService.Instance, new SettingsConfigurationService(_api));
        }

        public AddProviderWizardPage Page { get; }
        public AddProviderWizardViewModel ViewModel => (AddProviderWizardViewModel)Page.DataContext;
        private StackPanel Editors => (StackPanel)Page.FindName("CredentialFieldsPanel");
        public TextBox Key => Editors.Children.OfType<TextBox>().Single();
        public SecretInputControl Secret => Editors.Children.OfType<SecretInputControl>().Single();
        public List<Write> Writes { get; } = [];
        public List<string> StatusQueries { get; } = [];
        public Dictionary<string, RetainedCredentials> Records { get; } = new()
        {
            ["alpaca"] = new(false, false, null),
            ["polygon"] = new(false, false, null),
            ["nasdaq"] = new(false, false, null)
        };
        public int VerificationCalls { get; private set; }
        public bool RefuseWrites { get; set; }
        public bool RefuseStatus { get; set; }
        public bool FailVerification { get; set; }
        public TaskCompletionSource? PendingWrite { get; set; }
        public TaskCompletionSource? PendingVerification { get; set; }
        public TaskCompletionSource WriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource VerificationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static async Task<Fixture> CreateAsync(string selectedProvider = "alpaca")
        {
            var fixture = new Fixture();
            await fixture.Page.LoadProviderCatalogAsync();
            // This credential regression has no backfill configuration side effects.
            fixture.ViewModel.LoadProviderCatalog(SettingsConfigurationService.Instance.GetProviderCatalog()
                .Where(provider => fixture.Records.ContainsKey(ProviderIdentity.NormalizeId(provider.Id)))
                .Select(provider => provider with { SupportsHistorical = false }), []);
            fixture.Page.SelectProvider(selectedProvider);
            return fixture;
        }

        public void EnterCredentials()
        {
            Key.Text = "fixture-key";
            Secret.Secret = "fixture-secret";
        }

        private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
        {
            if (request.Method == HttpMethod.Get)
            {
                StatusQueries.Add(request.RequestUri!.Query);
                if (RefuseStatus)
                    return Json(new { }, HttpStatusCode.Forbidden);
                return Json(Records.Select(pair => new
                {
                    providerId = pair.Key,
                    displayName = pair.Key,
                    credentialState = pair.Value.Configured ? ProviderCredentialStateDto.Configured : ProviderCredentialStateDto.Missing,
                    verificationState = pair.Value.Verified ? ProviderVerificationStateDto.Verified : ProviderVerificationStateDto.NotVerified,
                    lastVerifiedAt = pair.Value.LastVerifiedAt,
                    credentialFields = new[]
                    {
                        new ProviderCredentialFieldMetadataDto("KeyId", "Key ID", true, ProviderCredentialInputKindDto.Text),
                        new ProviderCredentialFieldMetadataDto("SecretKey", "Secret key", true, ProviderCredentialInputKindDto.Password)
                    }
                }));
            }

            var providerId = request.RequestUri!.Segments[^2].Trim('/');
            if (request.Method == HttpMethod.Put)
            {
                var payload = JsonSerializer.Deserialize<ProviderCredentialUpsertRequestDto>(
                    await request.Content!.ReadAsStringAsync(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                Writes.Add(new(providerId, new Dictionary<string, string?>(payload.Credentials!)));
                WriteStarted.TrySetResult();
                if (PendingWrite is not null)
                    await PendingWrite.Task;
                if (RefuseWrites)
                    return Json(new { }, HttpStatusCode.Forbidden);
                Records[providerId] = new(true, false, null);
                return Json(new { providerId, credentialState = ProviderCredentialStateDto.Configured });
            }

            VerificationCalls++;
            VerificationStarted.TrySetResult();
            if (PendingVerification is not null)
                await PendingVerification.Task;
            if (FailVerification)
                return Json(new { providerId, success = false, verificationState = ProviderVerificationStateDto.Failed });
            var verifiedAt = DateTimeOffset.UtcNow;
            Records[providerId] = new(true, true, verifiedAt);
            return Json(new
            {
                providerId,
                success = true,
                verificationState = ProviderVerificationStateDto.Verified,
                lastVerifiedAt = verifiedAt
            });
        }

        public void Dispose()
        {
            _api.Dispose();
            _handler.Dispose();
        }
    }

    private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

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
