using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Meridian.Contracts.Lifecycle;
using Meridian.Ui.Services;
using Meridian.Wpf.Contracts;

namespace Meridian.Wpf.Services;

public interface ILifecycleControlClient
{
    Task<RuntimeLifecycleSnapshotDto?> GetStartupSnapshotAsync(CancellationToken cancellationToken = default);

    Task<bool> AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default);

    Task<RuntimeLifecycleSnapshotDto?> GetSnapshotAsync(CancellationToken cancellationToken = default);

    Task<LifecycleShutdownReceiptDto?> GetLatestReceiptAsync(CancellationToken cancellationToken = default);

    Task<LifecycleShutdownAcceptedDto?> RequestShutdownAsync(
        LifecycleShutdownRequestDto request,
        CancellationToken cancellationToken = default);
}

public sealed class LifecycleControlClient : ILifecycleControlClient, IDisposable
{
    private const string StartupEndpoint = "/startupz";
    private const string LoginEndpoint = "/api/auth/login";
    private const string LifecycleEndpoint = "/api/system/lifecycle";
    private const string ShutdownEndpoint = "/api/system/shutdown";
    private const string LatestReceiptEndpoint = "/api/system/shutdown/receipts/latest";
    private const string CsrfCookieName = "mdc-csrf";
    private const string SessionCookieName = "mdc-session";
    private const string CsrfHeaderName = "X-CSRF-Token";
    private readonly IRemoteWorkstationClient _remote;
    private readonly CookieContainer _cookies = new();
    private readonly HttpClient _http;
    private readonly DesktopAuthenticationSession? _authenticationSession;
    private bool _disposed;

    public LifecycleControlClient(
        IRemoteWorkstationClient remote,
        DesktopAuthenticationSession? authenticationSession = null)
    {
        _remote = remote ?? throw new ArgumentNullException(nameof(remote));
        _authenticationSession = authenticationSession;
        if (_authenticationSession is not null)
            _authenticationSession.SignedOut += OnDesktopSignedOut;
        _http = new HttpClient(new HttpClientHandler
        {
            CookieContainer = _cookies,
            UseCookies = true
        })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public Task<RuntimeLifecycleSnapshotDto?> GetStartupSnapshotAsync(
        CancellationToken cancellationToken = default)
        => GetAsync<RuntimeLifecycleSnapshotDto>(StartupEndpoint, cancellationToken);

    public async Task<bool> AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        try
        {
            using var response = await _http.PostAsJsonAsync(
                BuildUri(LoginEndpoint),
                new { username, password, returnUrl = "/workstation/" },
                DesktopJsonOptions.Api,
                cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    public Task<RuntimeLifecycleSnapshotDto?> GetSnapshotAsync(CancellationToken cancellationToken = default)
        => GetAsync<RuntimeLifecycleSnapshotDto>(LifecycleEndpoint, cancellationToken);

    public Task<LifecycleShutdownReceiptDto?> GetLatestReceiptAsync(CancellationToken cancellationToken = default)
        => GetAsync<LifecycleShutdownReceiptDto>(LatestReceiptEndpoint, cancellationToken);

    public async Task<LifecycleShutdownAcceptedDto?> RequestShutdownAsync(
        LifecycleShutdownRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var message = new HttpRequestMessage(HttpMethod.Post, BuildUri(ShutdownEndpoint))
        {
            Content = JsonContent.Create(request, options: DesktopJsonOptions.Api)
        };
        var csrf = _cookies.GetCookies(BuildUri("/"))[CsrfCookieName]?.Value;
        if (!string.IsNullOrWhiteSpace(csrf))
            message.Headers.TryAddWithoutValidation(CsrfHeaderName, csrf);

        using var response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<LifecycleShutdownAcceptedDto>(
            DesktopJsonOptions.Api,
            cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        if (_authenticationSession is not null)
            _authenticationSession.SignedOut -= OnDesktopSignedOut;
        _http.Dispose();
        _disposed = true;
    }

    private void OnDesktopSignedOut(object? sender, EventArgs e)
    {
        try
        {
            var origin = BuildUri("/");
            ExpireCookie(origin, SessionCookieName);
            ExpireCookie(origin, CsrfCookieName);
        }
        catch (Exception ex) when (ex is UriFormatException or CookieException)
        {
            // Local sign-out must remain fail-safe even if the remote endpoint is misconfigured.
        }
    }

    private void ExpireCookie(Uri origin, string name)
        => _cookies.Add(origin, new Cookie(name, string.Empty, "/")
        {
            Expires = DateTime.UtcNow.AddDays(-1),
            Expired = true
        });

    private async Task<T?> GetAsync<T>(string endpoint, CancellationToken ct) where T : class
    {
        using var response = await _http.GetAsync(BuildUri(endpoint), ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<T>(DesktopJsonOptions.Api, ct).ConfigureAwait(false);
    }

    private Uri BuildUri(string endpoint)
    {
        var baseUrl = _remote.BaseUrl.TrimEnd('/') + "/";
        return new Uri(new Uri(baseUrl, UriKind.Absolute), endpoint.TrimStart('/'));
    }
}
