using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Package Manager page.
/// Owns all create, validate, import, and list operations plus their status
/// display state, so the code-behind is thinned to constructor DI and lifecycle wiring only.
/// </summary>
public sealed class PackageManagerViewModel : BindableBase
{
    private readonly PortablePackagerService _packagerService;
    private readonly WpfServices.NotificationService _notificationService;

    // ── Create package ────────────────────────────────────────────────────────────────
    private string _createStatusText = string.Empty;
    public string CreateStatusText { get => _createStatusText; private set => SetProperty(ref _createStatusText, value); }

    private Brush _createStatusForeground = Brushes.Transparent;
    public Brush CreateStatusForeground { get => _createStatusForeground; private set => SetProperty(ref _createStatusForeground, value); }

    // ── Validate package ──────────────────────────────────────────────────────────────
    private string _validateResultText = string.Empty;
    public string ValidateResultText { get => _validateResultText; private set => SetProperty(ref _validateResultText, value); }

    private Brush _validateResultForeground = Brushes.Transparent;
    public Brush ValidateResultForeground { get => _validateResultForeground; private set => SetProperty(ref _validateResultForeground, value); }

    // ── Import package ────────────────────────────────────────────────────────────────
    private string _importResultText = string.Empty;
    public string ImportResultText { get => _importResultText; private set => SetProperty(ref _importResultText, value); }

    private Brush _importResultForeground = Brushes.Transparent;
    public Brush ImportResultForeground { get => _importResultForeground; private set => SetProperty(ref _importResultForeground, value); }

    // ── Package list ──────────────────────────────────────────────────────────────────
    private string _packageListStatusText = string.Empty;
    public string PackageListStatusText { get => _packageListStatusText; private set => SetProperty(ref _packageListStatusText, value); }

    private Brush _packageListStatusForeground = Brushes.Transparent;
    public Brush PackageListStatusForeground { get => _packageListStatusForeground; private set => SetProperty(ref _packageListStatusForeground, value); }

    private IReadOnlyList<object>? _packageItems;
    public IReadOnlyList<object>? PackageItems { get => _packageItems; private set => SetProperty(ref _packageItems, value); }

    // ── Commands ──────────────────────────────────────────────────────────────────────
    public IAsyncRelayCommand RefreshPackagesCommand { get; }

    public PackageManagerViewModel(
        PortablePackagerService packagerService,
        WpfServices.NotificationService notificationService)
    {
        _packagerService     = packagerService;
        _notificationService = notificationService;

        RefreshPackagesCommand = new AsyncRelayCommand(LoadRecentPackagesAsync);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────────────

    public async Task LoadAsync() => await LoadRecentPackagesAsync();

    // ── Commands ──────────────────────────────────────────────────────────────────────

    public async Task CreatePackageAsync(
        string symbolsText, string fromDateText, string toDateText)
    {
        if (string.IsNullOrWhiteSpace(symbolsText))
        {
            CreateStatusText       = "Please enter at least one symbol.";
            CreateStatusForeground = GetResource("WarningColorBrush", Brushes.Orange);
            return;
        }

        var symbols = symbolsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        DateOnly? fromDate = DateOnly.TryParse(fromDateText, out var f) ? f : null;
        DateOnly? toDate   = DateOnly.TryParse(toDateText,   out var t) ? t : null;

        CreateStatusText       = "Creating package...";
        CreateStatusForeground = GetResource("ConsoleTextMutedBrush", Brushes.Gray);

        try
        {
            var options = new PackageCreationOptions
            {
                Name               = $"package-{DateTime.UtcNow:yyyyMMdd-HHmmss}",
                Symbols            = symbols.ToList(),
                FromDate           = fromDate,
                ToDate             = toDate,
                IncludeMetadata    = true,
                GenerateChecksums  = true
            };

            var result = await _packagerService.CreatePackageAsync(options);

            if (result.Success)
            {
                CreateStatusText       = $"Package created: {result.PackagePath}";
                CreateStatusForeground = GetResource("SuccessColorBrush", Brushes.LimeGreen);
                _notificationService.NotifySuccess("Package Created", $"Package with {symbols.Length} symbol(s) created.");
                await LoadRecentPackagesAsync();
            }
            else
            {
                CreateStatusText       = $"Package creation failed: {result.Error}";
                CreateStatusForeground = GetResource("ErrorColorBrush", Brushes.Red);
            }
        }
        catch (Exception ex)
        {
            CreateStatusText       = $"Failed: {ex.Message}";
            CreateStatusForeground = GetResource("ErrorColorBrush", Brushes.Red);
        }
    }

    public async Task ValidatePackageAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            ValidateResultText       = "Please enter a package file path.";
            ValidateResultForeground = GetResource("WarningColorBrush", Brushes.Orange);
            return;
        }

        ValidateResultText       = "Validating...";
        ValidateResultForeground = GetResource("ConsoleTextMutedBrush", Brushes.Gray);

        try
        {
            var result = await _packagerService.ValidatePackageAsync(path);

            if (result.IsValid)
            {
                ValidateResultText       = $"Package is valid.\nValid files: {result.ValidFileCount}, Size: {FormatHelpers.FormatBytes(result.TotalSizeBytes)}";
                ValidateResultForeground = GetResource("SuccessColorBrush", Brushes.LimeGreen);
            }
            else
            {
                var issues = result.Issues.Count > 0
                    ? string.Join("; ", result.Issues.Select(i => i.Message).Take(3))
                    : "Unknown validation error";
                ValidateResultText       = $"Package validation failed: {issues}";
                ValidateResultForeground = GetResource("ErrorColorBrush", Brushes.Red);
            }
        }
        catch (Exception ex)
        {
            ValidateResultText       = $"Validation failed: {ex.Message}";
            ValidateResultForeground = GetResource("ErrorColorBrush", Brushes.Red);
        }
    }

    public async Task ImportPackageAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            ImportResultText       = "Please enter a package file path.";
            ImportResultForeground = GetResource("WarningColorBrush", Brushes.Orange);
            return;
        }

        ImportResultText       = "Importing package...";
        ImportResultForeground = GetResource("ConsoleTextMutedBrush", Brushes.Gray);

        try
        {
            var options = new PackageImportOptions
            {
                PackagePath       = path,
                OverwriteExisting = false
            };

            var result = await _packagerService.ImportPackageAsync(options);

            if (result.Success)
            {
                ImportResultText       = $"Package imported. Files: {result.FilesImported}";
                ImportResultForeground = GetResource("SuccessColorBrush", Brushes.LimeGreen);
                _notificationService.NotifySuccess("Package Imported", $"Imported {result.FilesImported} files.");
            }
            else
            {
                ImportResultText       = $"Import failed: {result.Error}";
                ImportResultForeground = GetResource("ErrorColorBrush", Brushes.Red);
            }
        }
        catch (Exception ex)
        {
            ImportResultText       = $"Import failed: {ex.Message}";
            ImportResultForeground = GetResource("ErrorColorBrush", Brushes.Red);
        }
    }

    // ── Data loading ──────────────────────────────────────────────────────────────────

    private async Task LoadRecentPackagesAsync()
    {
        PackageListStatusText       = "Loading...";
        PackageListStatusForeground = GetResource("ConsoleTextMutedBrush", Brushes.Gray);

        try
        {
            var packages = await _packagerService.GetRecentPackagesAsync();

            if (packages is null || packages.Count == 0)
            {
                PackageListStatusText       = "No packages found.";
                PackageListStatusForeground = GetResource("ConsoleTextMutedBrush", Brushes.Gray);
                PackageItems = null;
                return;
            }

            PackageListStatusText       = $"{packages.Count} package(s) found";
            PackageListStatusForeground = GetResource("ConsoleTextPrimaryBrush", Brushes.White);
            PackageItems = packages.Cast<object>().ToList();
        }
        catch (Exception ex)
        {
            PackageListStatusText       = $"Failed to load packages: {ex.Message}";
            PackageListStatusForeground = GetResource("ErrorColorBrush", Brushes.Red);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────

    private static Brush GetResource(string key, Brush fallback) =>
        System.Windows.Application.Current?.TryFindResource(key) as Brush ?? fallback;
}
