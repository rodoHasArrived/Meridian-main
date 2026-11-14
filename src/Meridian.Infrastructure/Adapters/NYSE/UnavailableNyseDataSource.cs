using System.Reactive.Linq;
using Meridian.Infrastructure.DataSources;
using Meridian.ProviderSdk;

namespace Meridian.Infrastructure.Adapters.NYSE;

/// <summary>Credential-free diagnostics for a NYSE capability that cannot be activated.</summary>
internal sealed class UnavailableNyseDataSource : IDataSource, IProviderRateLimitDiagnosticsSource, IDisposable
{
    public string Id => "nyse";
    public string DisplayName => "NYSE Direct";
    public string Description => NyseCredentialAccessPolicy.UnavailableMessage;
    public DataSourceType Type => DataSourceType.Hybrid;
    public DataSourceCategory Category => DataSourceCategory.Exchange;
    public int Priority => int.MaxValue;
    public DataSourceCapabilities Capabilities => DataSourceCapabilities.None;
    public DataSourceCapabilityInfo CapabilityInfo => DataSourceCapabilityInfo.Default(Capabilities);
    public IReadOnlySet<string> SupportedMarkets { get; } = new HashSet<string>();
    public IReadOnlySet<AssetClass> SupportedAssetClasses { get; } = new HashSet<AssetClass>();
    public DataSourceHealth Health => DataSourceHealth.Unhealthy(Description);
    public DataSourceStatus Status => DataSourceStatus.Unavailable;
    public RateLimitState RateLimitState => new(false, 0, 0);
    public IObservable<DataSourceHealthChanged> HealthChanges => Observable.Empty<DataSourceHealthChanged>();

    public Task InitializeAsync(CancellationToken ct = default)
        => Task.FromException(new InvalidOperationException(Description));
    public Task<bool> ValidateCredentialsAsync(CancellationToken ct = default) => Task.FromResult(false);
    public Task<bool> TestConnectivityAsync(CancellationToken ct = default) => Task.FromResult(false);
    public void Dispose() { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public ProviderRateLimitDiagnosticSnapshot GetRateLimitDiagnosticsSnapshot()
        => new(Id, ProviderRateLimitSurfaces.DataSource, DateTimeOffset.UtcNow, 0, 0, TimeSpan.Zero,
            false, null, 0, "credential-ownership-unavailable", StateAvailable: false);
}
