using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Embeds the Meridian React dashboard in a WPF page via WebView2.
/// Navigates to the backend's /workstation/index.html once the WebView2 runtime
/// is confirmed available; degrades gracefully if the Evergreen runtime is absent.
/// </summary>
public partial class DashboardWebPage : Page
{
    private static readonly string WebView2DownloadUrl =
        "https://developer.microsoft.com/en-us/microsoft-edge/webview2/#download-section";

    private readonly ConnectionService _connectionService;
    private bool _isInitialized;

    public DashboardWebPage(ConnectionService connectionService)
    {
        InitializeComponent();
        _connectionService = connectionService;
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized)
            return;

        _isInitialized = true;
        await InitializeWebViewAsync();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        // WebView2 lifetime follows the Page lifetime; no explicit teardown needed.
    }

    // ── Initialisation ─────────────────────────────────────────────────────

    private async System.Threading.Tasks.Task InitializeWebViewAsync()
    {
        try
        {
            // EnsureCoreWebView2Async will throw if the runtime is not installed.
            await DashboardWebView.EnsureCoreWebView2Async(null);

            var baseUrl = _connectionService.ServiceUrl.TrimEnd('/');
            var dashboardUrl = $"{baseUrl}/workstation/index.html";

            UrlText.Text = dashboardUrl;

            DashboardWebView.Source = new Uri(dashboardUrl);
            DashboardWebView.Visibility = Visibility.Visible;
            FallbackPanel.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) when (IsWebView2RuntimeMissingException(ex))
        {
            ShowRuntimeMissingFallback();
        }
        catch (Exception ex)
        {
            ShowErrorFallback(ex.Message);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static bool IsWebView2RuntimeMissingException(Exception ex)
    {
        // WebView2 throws WebView2RuntimeNotFoundException or a COMException with HRESULT
        // 0x80004005 when the Evergreen runtime is not installed.
        return ex.GetType().Name.Contains("WebView2RuntimeNotFoundException", StringComparison.Ordinal)
            || ex.Message.Contains("WebView2 Runtime", StringComparison.OrdinalIgnoreCase)
            || (ex is System.Runtime.InteropServices.COMException com && com.HResult == unchecked((int)0x80004005));
    }

    private void ShowRuntimeMissingFallback()
    {
        FallbackHeading.Text = "WebView2 Runtime Not Installed";
        FallbackDetail.Text =
            "The Microsoft Edge WebView2 Runtime is required to display the embedded dashboard. " +
            "Click the button below to download and install it, then restart Meridian.";
        InstallRuntimeButton.Visibility = Visibility.Visible;
        FallbackPanel.Visibility = Visibility.Visible;
        DashboardWebView.Visibility = Visibility.Collapsed;

        LoggingService.Instance.LogWarning(
            "[DashboardWebPage] WebView2 Runtime is not installed — showing installation prompt.");
    }

    private void ShowErrorFallback(string message)
    {
        FallbackHeading.Text = "Dashboard Unavailable";
        FallbackDetail.Text = $"Could not load the dashboard: {message}";
        FallbackPanel.Visibility = Visibility.Visible;
        DashboardWebView.Visibility = Visibility.Collapsed;

        LoggingService.Instance.LogError($"[DashboardWebPage] WebView2 init failed: {message}");
    }

    // ── Button handlers ───────────────────────────────────────────────────

    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        if (DashboardWebView.CoreWebView2 is not null)
        {
            DashboardWebView.CoreWebView2.Reload();
        }
        else
        {
            _isInitialized = false;
            await InitializeWebViewAsync();
        }
    }

    private void OnOpenInBrowserClicked(object sender, RoutedEventArgs e)
    {
        var baseUrl = _connectionService.ServiceUrl.TrimEnd('/');
        var dashboardUrl = $"{baseUrl}/workstation/index.html";
        Process.Start(new ProcessStartInfo(dashboardUrl) { UseShellExecute = true });
    }

    private void OnInstallRuntimeClicked(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo(WebView2DownloadUrl) { UseShellExecute = true });
    }
}
