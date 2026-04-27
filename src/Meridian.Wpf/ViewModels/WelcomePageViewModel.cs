using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Copy;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Welcome page.
/// Owns overview data loading and navigation commands so that the code-behind is
/// thinned to constructor DI and lifecycle wiring only.
/// </summary>
public sealed class WelcomePageViewModel : BindableBase
{
    private const string DocumentationUrl = "https://github.com/rodoHasArrived/Meridian-main/blob/main/docs/HELP.md";

    private readonly WpfServices.NavigationService _navigationService;
    private readonly WpfServices.NotificationService _notificationService;
    private readonly WpfServices.StatusService _statusService;
    private readonly WpfServices.ConnectionService _connectionService;
    private readonly WpfServices.ConfigService _configService;

    private string _connectionStatusText = "Disconnected";
    public string ConnectionStatusText { get => _connectionStatusText; private set => SetProperty(ref _connectionStatusText, value); }

    private Brush _connectionStatusDotFill = Brushes.Gray;
    public Brush ConnectionStatusDotFill { get => _connectionStatusDotFill; private set => SetProperty(ref _connectionStatusDotFill, value); }

    private string _connectionProviderText = "No provider connected";
    public string ConnectionProviderText { get => _connectionProviderText; private set => SetProperty(ref _connectionProviderText, value); }

    private string _symbolsCountText = "0";
    public string SymbolsCountText { get => _symbolsCountText; private set => SetProperty(ref _symbolsCountText, value); }

    private string _storagePathText = "./data";
    public string StoragePathText { get => _storagePathText; private set => SetProperty(ref _storagePathText, value); }

    public IReadOnlyList<WelcomeWorkspaceCard> WorkspaceCards { get; }
    private IReadOnlyList<WelcomeReadinessItem> _readinessItems = Array.Empty<WelcomeReadinessItem>();
    public IReadOnlyList<WelcomeReadinessItem> ReadinessItems
    {
        get => _readinessItems;
        private set => SetProperty(ref _readinessItems, value);
    }

    private string _readinessProgressText = "0 of 3 checks ready";
    public string ReadinessProgressText { get => _readinessProgressText; private set => SetProperty(ref _readinessProgressText, value); }

    private string _readinessSummaryText = "Connection, symbol inventory, and storage posture are still being checked.";
    public string ReadinessSummaryText { get => _readinessSummaryText; private set => SetProperty(ref _readinessSummaryText, value); }

    private WelcomeNextAction _nextAction = CreateDefaultNextAction();
    public WelcomeNextAction NextAction
    {
        get => _nextAction;
        private set => SetProperty(ref _nextAction, value);
    }

    public IRelayCommand<string> NavigateToPageCommand { get; }
    public IRelayCommand<string> NavigateToWorkspaceCommand { get; }
    public IRelayCommand NavigateToProviderCommand { get; }
    public IRelayCommand NavigateToSymbolsCommand { get; }
    public IRelayCommand NavigateToStorageCommand { get; }
    public IRelayCommand NavigateToDataOperationsWorkspaceCommand { get; }
    public IRelayCommand NavigateToDataQualityCommand { get; }
    public IRelayCommand OpenDocumentationCommand { get; }

    public WelcomePageViewModel(
        WpfServices.NavigationService navigationService,
        WpfServices.NotificationService notificationService,
        WpfServices.StatusService statusService,
        WpfServices.ConnectionService connectionService,
        WpfServices.ConfigService configService)
    {
        _navigationService = navigationService;
        _notificationService = notificationService;
        _statusService = statusService;
        _connectionService = connectionService;
        _configService = configService;

        WorkspaceCards = CreateWorkspaceCards();

        NavigateToPageCommand = new RelayCommand<string>(NavigateToPage);
        NavigateToWorkspaceCommand = new RelayCommand<string>(NavigateToWorkspace);
        NavigateToProviderCommand = new RelayCommand(() => _navigationService.NavigateTo("Provider"));
        NavigateToSymbolsCommand = new RelayCommand(() => _navigationService.NavigateTo("Symbols"));
        NavigateToStorageCommand = new RelayCommand(() => _navigationService.NavigateTo("Storage"));
        NavigateToDataOperationsWorkspaceCommand = new RelayCommand(() => _navigationService.NavigateTo("DataOperationsShell"));
        NavigateToDataQualityCommand = new RelayCommand(() => _navigationService.NavigateTo("DataQuality"));
        OpenDocumentationCommand = new RelayCommand(OpenDocumentation);

        ApplyOverviewSnapshot(
            isConnected: false,
            connectionProviderText: "No provider connected",
            symbolCount: 0,
            storagePath: "./data",
            configuredDataRoot: "data");
    }

    public async Task LoadAsync()
    {
        await UpdateSystemOverviewAsync();
    }

    private async Task UpdateSystemOverviewAsync()
    {
        var isConnected = _connectionService.State == ConnectionState.Connected;
        var providerText = "No provider connected";
        var symbolCount = 0;
        var storagePath = "./data";
        var configuredDataRoot = "data";

        if (isConnected)
        {
            try
            {
                var providerInfo = await _statusService.GetProviderStatusAsync();
                providerText = providerInfo?.ActiveProvider is { Length: > 0 }
                    ? providerInfo.ActiveProvider
                    : "Provider connected";
            }
            catch (Exception)
            {
                providerText = "Provider connected";
            }
        }

        try
        {
            var symbols = await _configService.GetSymbolsAsync();
            symbolCount = symbols?.Length ?? 0;
        }
        catch (Exception)
        {
            symbolCount = 0;
        }

        try
        {
            var config = await _configService.LoadConfigAsync();
            configuredDataRoot = string.IsNullOrWhiteSpace(config?.DataRoot)
                ? "data"
                : config.DataRoot;
            storagePath = _configService.ResolveDataRoot(config);
        }
        catch (Exception)
        {
            storagePath = "./data";
            configuredDataRoot = "data";
        }

        ApplyOverviewSnapshot(isConnected, providerText, symbolCount, storagePath, configuredDataRoot);
    }

    internal void ApplyOverviewSnapshotForTests(
        bool isConnected,
        string connectionProviderText,
        int symbolCount,
        string storagePath,
        string configuredDataRoot = "data")
    {
        ApplyOverviewSnapshot(isConnected, connectionProviderText, symbolCount, storagePath, configuredDataRoot);
    }

    private void ApplyOverviewSnapshot(
        bool isConnected,
        string connectionProviderText,
        int symbolCount,
        string storagePath,
        string configuredDataRoot)
    {
        var successBrush = GetResource("SuccessColorBrush", Brushes.LimeGreen);
        var mutedBrush = GetResource("ConsoleTextMutedBrush", Brushes.Gray);
        var normalizedProviderText = string.IsNullOrWhiteSpace(connectionProviderText)
            ? (isConnected ? "Provider connected" : "No provider connected")
            : connectionProviderText;
        var normalizedStoragePath = string.IsNullOrWhiteSpace(storagePath)
            ? "./data"
            : storagePath;

        ConnectionStatusText = isConnected ? "Connected" : "Disconnected";
        ConnectionStatusDotFill = isConnected ? successBrush : mutedBrush;
        ConnectionProviderText = normalizedProviderText;
        SymbolsCountText = symbolCount.ToString();
        StoragePathText = normalizedStoragePath;

        ReadinessItems = BuildReadinessItems(
            isConnected,
            normalizedProviderText,
            symbolCount,
            normalizedStoragePath,
            configuredDataRoot);
        var readinessSummary = BuildReadinessSummary(isConnected, symbolCount, configuredDataRoot);
        ReadinessProgressText = readinessSummary.ProgressText;
        ReadinessSummaryText = readinessSummary.SummaryText;
        NextAction = BuildNextAction(
            isConnected,
            symbolCount,
            normalizedStoragePath,
            configuredDataRoot);
    }

    private void NavigateToPage(string? pageTag)
    {
        if (string.IsNullOrWhiteSpace(pageTag))
        {
            return;
        }

        _navigationService.NavigateTo(pageTag);
    }

    private void NavigateToWorkspace(string? pageTag) => NavigateToPage(pageTag);

    private void OpenDocumentation()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = DocumentationUrl,
                UseShellExecute = true
            });
        }
        catch
        {
            _notificationService.ShowNotification(
                "Error",
                "Could not open the documentation link. Please try again.",
                NotificationType.Error);
        }
    }

    private static IReadOnlyList<WelcomeWorkspaceCard> CreateWorkspaceCards() =>
    [
        CreateWorkspaceCard(
            WorkspaceCopyCatalog.Research.Descriptor,
            "ResearchShell",
            "\uEC35",
            "Open Studio",
            "InfoColorBrush",
            "ConsoleAccentBlueAlpha10Brush",
            Brushes.DodgerBlue,
            new SolidColorBrush(Color.FromArgb(0x33, 0x2D, 0x9C, 0xDB))),
        CreateWorkspaceCard(
            WorkspaceCopyCatalog.Trading.Descriptor,
            "TradingShell",
            "\uE945",
            "Open Desk",
            "SuccessColorBrush",
            "ConsoleAccentGreenAlpha10Brush",
            Brushes.LimeGreen,
            new SolidColorBrush(Color.FromArgb(0x33, 0x2D, 0xD4, 0xA4))),
        CreateWorkspaceCard(
            WorkspaceCopyCatalog.DataOperations.Descriptor,
            "DataOperationsShell",
            "\uEE94",
            "Open Workspace",
            "WarningColorBrush",
            "ConsoleAccentOrangeAlpha10Brush",
            Brushes.DarkOrange,
            new SolidColorBrush(Color.FromArgb(0x33, 0xF6, 0xB4, 0x4B))),
        CreateWorkspaceCard(
            WorkspaceCopyCatalog.Governance.Descriptor,
            "GovernanceShell",
            "\uE8D7",
            "Open Review",
            "AccentPurpleBrush",
            "ConsoleAccentPurpleAlpha10Brush",
            Brushes.MediumPurple,
            new SolidColorBrush(Color.FromArgb(0x33, 0x9B, 0x7C, 0xFF)))
    ];

    private static WelcomeNextAction CreateDefaultNextAction()
    {
        var accentBrush = GetResource("WarningColorBrush", Brushes.DarkOrange);
        var accentBackgroundBrush = GetResource(
            "ConsoleAccentOrangeAlpha10Brush",
            new SolidColorBrush(Color.FromArgb(0x33, 0xF6, 0xB4, 0x4B)));

        return new WelcomeNextAction(
            "Setup gap",
            "Finish setup in Data Operations",
            "The desktop landing page starts in setup mode until connection, symbol coverage, and storage posture are known.",
            "Open Data Operations",
            "DataOperationsShell",
            "Open Provider",
            "Provider",
            accentBrush,
            accentBackgroundBrush);
    }

    private static IReadOnlyList<WelcomeReadinessItem> BuildReadinessItems(
        bool isConnected,
        string connectionProviderText,
        int symbolCount,
        string storagePath,
        string configuredDataRoot)
    {
        var isDefaultStorage = IsDefaultStoragePath(configuredDataRoot);
        var connectedBrush = GetResource("SuccessColorBrush", Brushes.LimeGreen);
        var connectedBackgroundBrush = GetResource(
            "ConsoleAccentGreenAlpha10Brush",
            new SolidColorBrush(Color.FromArgb(0x33, 0x2D, 0xD4, 0xA4)));
        var infoBrush = GetResource("InfoColorBrush", Brushes.DodgerBlue);
        var infoBackgroundBrush = GetResource(
            "ConsoleAccentBlueAlpha10Brush",
            new SolidColorBrush(Color.FromArgb(0x33, 0x2D, 0x9C, 0xDB)));
        var warningBrush = GetResource("WarningColorBrush", Brushes.DarkOrange);
        var warningBackgroundBrush = GetResource(
            "ConsoleAccentOrangeAlpha10Brush",
            new SolidColorBrush(Color.FromArgb(0x33, 0xF6, 0xB4, 0x4B)));
        var dangerBrush = GetResource("ErrorColorBrush", Brushes.IndianRed);
        var dangerBackgroundBrush = GetResource(
            "ConsoleAccentRedAlpha10Brush",
            new SolidColorBrush(Color.FromArgb(0x33, 0xE8, 0x5B, 0x5B)));

        return
        [
            new WelcomeReadinessItem(
                "Provider session",
                isConnected ? "Connected" : "Blocked",
                isConnected
                    ? $"{connectionProviderText} is currently reachable from the workstation host."
                    : "The workstation is disconnected from the local Meridian host or its active provider session.",
                "Inspect Provider",
                "Provider",
                "\uE968",
                isConnected ? connectedBrush : dangerBrush,
                isConnected ? connectedBackgroundBrush : dangerBackgroundBrush),
            new WelcomeReadinessItem(
                "Symbol inventory",
                symbolCount > 0 ? $"{symbolCount} configured" : "Needs symbols",
                symbolCount > 0
                    ? $"{symbolCount} configured subscription(s) are available for collection, freshness, and workspace routing flows."
                    : "No collection symbols are configured yet, so queue and quality workflows will stay mostly empty.",
                symbolCount > 0 ? "Review Symbols" : "Add Symbols",
                "Symbols",
                "\uEA37",
                symbolCount > 0 ? infoBrush : warningBrush,
                symbolCount > 0 ? infoBackgroundBrush : warningBackgroundBrush),
            new WelcomeReadinessItem(
                "Storage target",
                isDefaultStorage ? "Default path" : "Custom path",
                isDefaultStorage
                    ? $"Collection currently resolves under {storagePath}. Confirm that default target before broad collection or export work."
                    : $"Collection and export artifacts currently resolve under {storagePath}.",
                "Open Storage",
                "Storage",
                "\uE7F1",
                isDefaultStorage ? warningBrush : infoBrush,
                isDefaultStorage ? warningBackgroundBrush : infoBackgroundBrush)
        ];
    }

    private static WelcomeReadinessSummary BuildReadinessSummary(
        bool isConnected,
        int symbolCount,
        string configuredDataRoot)
    {
        var readyChecks = 0;
        if (isConnected)
        {
            readyChecks++;
        }

        if (symbolCount > 0)
        {
            readyChecks++;
        }

        if (!IsDefaultStoragePath(configuredDataRoot))
        {
            readyChecks++;
        }

        var summaryText = readyChecks switch
        {
            3 => "Provider, symbol, and storage checks are clear. Move into the shell that owns the next operator decision.",
            _ when !isConnected => "Provider connectivity is blocking live workstation flows. Start with provider recovery before checking downstream queues.",
            _ when symbolCount == 0 => "Provider connectivity is available, but no symbol inventory is configured for collection or quality workflows.",
            _ => "Provider and symbol coverage are available; confirm the default storage target before broader collection or export work."
        };

        return new WelcomeReadinessSummary($"{readyChecks} of 3 checks ready", summaryText);
    }

    private static WelcomeNextAction BuildNextAction(
        bool isConnected,
        int symbolCount,
        string storagePath,
        string configuredDataRoot)
    {
        var successBrush = GetResource("SuccessColorBrush", Brushes.LimeGreen);
        var successBackgroundBrush = GetResource(
            "ConsoleAccentGreenAlpha10Brush",
            new SolidColorBrush(Color.FromArgb(0x33, 0x2D, 0xD4, 0xA4)));
        var warningBrush = GetResource("WarningColorBrush", Brushes.DarkOrange);
        var warningBackgroundBrush = GetResource(
            "ConsoleAccentOrangeAlpha10Brush",
            new SolidColorBrush(Color.FromArgb(0x33, 0xF6, 0xB4, 0x4B)));
        var dangerBrush = GetResource("ErrorColorBrush", Brushes.IndianRed);
        var dangerBackgroundBrush = GetResource(
            "ConsoleAccentRedAlpha10Brush",
            new SolidColorBrush(Color.FromArgb(0x33, 0xE8, 0x5B, 0x5B)));

        if (!isConnected)
        {
            return new WelcomeNextAction(
                "Connection blocker",
                "Restore provider connectivity first",
                "Reconnect the workstation host or verify provider settings before moving into live desk, queue, or freshness workflows.",
                "Open Provider",
                "Provider",
                "Open Data Operations",
                "DataOperationsShell",
                dangerBrush,
                dangerBackgroundBrush);
        }

        if (symbolCount == 0)
        {
            return new WelcomeNextAction(
                "Setup gap",
                "Add the first symbol set",
                "Connectivity is healthy, but no subscriptions are configured yet. Seed symbols before running collection or quality review.",
                "Add Symbols",
                "Symbols",
                "Open Data Operations",
                "DataOperationsShell",
                warningBrush,
                warningBackgroundBrush);
        }

        if (IsDefaultStoragePath(configuredDataRoot))
        {
            return new WelcomeNextAction(
                "Storage review",
                "Confirm the collection target before scaling up",
                $"The desktop is usable, but storage still resolves to the default target at {storagePath}. Confirm retention and export posture before broader runs.",
                "Open Storage",
                "Storage",
                "Open Data Operations",
                "DataOperationsShell",
                warningBrush,
                warningBackgroundBrush);
        }

        return new WelcomeNextAction(
            "Ready for operator flow",
            "Collection posture is usable",
            "Connection, symbols, and storage are all in place. Move into the shell that owns the next operator decision instead of staying on the landing page.",
            "Open Trading Desk",
            "TradingShell",
            "Open Research Workspace",
            "ResearchShell",
            successBrush,
            successBackgroundBrush);
    }

    private static WelcomeWorkspaceCard CreateWorkspaceCard(
        WorkspaceDescriptorCopy descriptor,
        string pageTag,
        string iconGlyph,
        string actionLabel,
        string accentBrushKey,
        string accentBackgroundBrushKey,
        Brush accentFallback,
        Brush accentBackgroundFallback)
    {
        return new WelcomeWorkspaceCard(
            descriptor.Title,
            descriptor.ShellDisplayName,
            descriptor.Description,
            descriptor.TileSummary,
            pageTag,
            actionLabel,
            iconGlyph,
            GetResource(accentBrushKey, accentFallback),
            GetResource(accentBackgroundBrushKey, accentBackgroundFallback));
    }

    private static bool IsDefaultStoragePath(string? configuredDataRoot) =>
        string.IsNullOrWhiteSpace(configuredDataRoot)
        || string.Equals(configuredDataRoot, "data", StringComparison.OrdinalIgnoreCase)
        || string.Equals(configuredDataRoot, "./data", StringComparison.OrdinalIgnoreCase)
        || string.Equals(configuredDataRoot, ".\\data", StringComparison.OrdinalIgnoreCase);

    private static Brush GetResource(string key, Brush fallback) =>
        System.Windows.Application.Current?.TryFindResource(key) as Brush ?? fallback;
}

public sealed record WelcomeWorkspaceCard(
    string Title,
    string ShellDisplayName,
    string Description,
    string TileSummary,
    string PageTag,
    string ActionLabel,
    string IconGlyph,
    Brush AccentBrush,
    Brush AccentBackgroundBrush);

public sealed record WelcomeReadinessItem(
    string Title,
    string StatusLabel,
    string Detail,
    string ActionLabel,
    string ActionPageTag,
    string IconGlyph,
    Brush AccentBrush,
    Brush AccentBackgroundBrush);

public sealed record WelcomeReadinessSummary(
    string ProgressText,
    string SummaryText);

public sealed record WelcomeNextAction(
    string ToneLabel,
    string Title,
    string Summary,
    string PrimaryActionLabel,
    string PrimaryActionPageTag,
    string SecondaryActionLabel,
    string SecondaryActionPageTag,
    Brush AccentBrush,
    Brush AccentBackgroundBrush);
