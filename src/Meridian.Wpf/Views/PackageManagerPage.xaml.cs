using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Meridian.Ui.Services;
using Meridian.Wpf.Services;
using NotificationService = Meridian.Wpf.Services.NotificationService;
using PackageCreationOptions = Meridian.Ui.Services.PackageCreationOptions;
using PackageImportOptions = Meridian.Ui.Services.PackageImportOptions;
using PortablePackagerService = Meridian.Ui.Services.PortablePackagerService;

namespace Meridian.Wpf.Views;

public partial class PackageManagerPage : Page
{
    private readonly NavigationService _navigationService;
    private readonly NotificationService _notificationService;
    private readonly PortablePackagerService _packagerService;

    public PackageManagerPage(
        NavigationService navigationService,
        NotificationService notificationService)
    {
        InitializeComponent();

        _navigationService = navigationService;
        _notificationService = notificationService;
        _packagerService = PortablePackagerService.Instance;

        // Set default dates
        var today = DateTime.UtcNow;
        PackageFromInput.Text = today.AddDays(-30).ToString("yyyy-MM-dd");
        PackageToInput.Text = today.ToString("yyyy-MM-dd");
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        await LoadRecentPackagesAsync();
    }

    private async void RefreshPackages_Click(object sender, RoutedEventArgs e)
    {
        await LoadRecentPackagesAsync();
    }

    private async void CreatePackage_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var symbolsText = PackageSymbolsInput.Text?.Trim();
            if (string.IsNullOrEmpty(symbolsText))
            {
                CreatePackageStatus.Text = "Please enter at least one symbol.";
                CreatePackageStatus.Foreground = (Brush)FindResource("WarningColorBrush");
                return;
            }

            var symbols = symbolsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            DateOnly? fromDate = null;
            DateOnly? toDate = null;

            if (!string.IsNullOrEmpty(PackageFromInput.Text?.Trim()) &&
                DateOnly.TryParse(PackageFromInput.Text.Trim(), out var from))
            {
                fromDate = from;
            }

            if (!string.IsNullOrEmpty(PackageToInput.Text?.Trim()) &&
                DateOnly.TryParse(PackageToInput.Text.Trim(), out var to))
            {
                toDate = to;
            }

            CreatePackageStatus.Text = "Creating package...";
            CreatePackageStatus.Foreground = (Brush)FindResource("ConsoleTextMutedBrush");

            var options = new PackageCreationOptions
            {
                Name = $"package-{DateTime.UtcNow:yyyyMMdd-HHmmss}",
                Symbols = symbols.ToList(),
                FromDate = fromDate,
                ToDate = toDate,
                IncludeMetadata = true,
                GenerateChecksums = true
            };

            var result = await _packagerService.CreatePackageAsync(options);

            if (result.Success)
            {
                CreatePackageStatus.Text = $"Package created: {result.PackagePath}";
                CreatePackageStatus.Foreground = (Brush)FindResource("SuccessColorBrush");
                _notificationService.NotifySuccess("Package Created", $"Package with {symbols.Length} symbol(s) created.");
                await LoadRecentPackagesAsync();
            }
            else
            {
                CreatePackageStatus.Text = $"Package creation failed: {result.Error}";
                CreatePackageStatus.Foreground = (Brush)FindResource("ErrorColorBrush");
            }
        }
        catch (Exception ex)
        {
            CreatePackageStatus.Text = $"Failed: {ex.Message}";
            CreatePackageStatus.Foreground = (Brush)FindResource("ErrorColorBrush");
        }
    }

    private async void ValidatePackage_Click(object sender, RoutedEventArgs e)
    {
        var path = ValidatePackageInput.Text?.Trim();
        if (string.IsNullOrEmpty(path))
        {
            ValidatePackageResult.Text = "Please enter a package file path.";
            ValidatePackageResult.Foreground = (Brush)FindResource("WarningColorBrush");
            return;
        }

        try
        {
            ValidatePackageResult.Text = "Validating...";
            ValidatePackageResult.Foreground = (Brush)FindResource("ConsoleTextMutedBrush");

            var result = await _packagerService.ValidatePackageAsync(path);

            if (result.IsValid)
            {
                ValidatePackageResult.Text = $"Package is valid.\nValid files: {result.ValidFileCount}, Size: {FormatHelpers.FormatBytes(result.TotalSizeBytes)}";
                ValidatePackageResult.Foreground = (Brush)FindResource("SuccessColorBrush");
            }
            else
            {
                var issues = result.Issues.Count > 0
                    ? string.Join("; ", result.Issues.Select(i => i.Message).Take(3))
                    : "Unknown validation error";
                ValidatePackageResult.Text = $"Package validation failed: {issues}";
                ValidatePackageResult.Foreground = (Brush)FindResource("ErrorColorBrush");
            }
        }
        catch (Exception ex)
        {
            ValidatePackageResult.Text = $"Validation failed: {ex.Message}";
            ValidatePackageResult.Foreground = (Brush)FindResource("ErrorColorBrush");
        }
    }

    private async void ImportPackage_Click(object sender, RoutedEventArgs e)
    {
        var path = ImportPackageInput.Text?.Trim();
        if (string.IsNullOrEmpty(path))
        {
            ImportPackageResult.Text = "Please enter a package file path.";
            ImportPackageResult.Foreground = (Brush)FindResource("WarningColorBrush");
            return;
        }

        try
        {
            ImportPackageResult.Text = "Importing package...";
            ImportPackageResult.Foreground = (Brush)FindResource("ConsoleTextMutedBrush");

            var options = new PackageImportOptions
            {
                PackagePath = path,
                OverwriteExisting = false
            };

            var result = await _packagerService.ImportPackageAsync(options);

            if (result.Success)
            {
                ImportPackageResult.Text = $"Package imported. Files: {result.FilesImported}";
                ImportPackageResult.Foreground = (Brush)FindResource("SuccessColorBrush");
                _notificationService.NotifySuccess("Package Imported", $"Imported {result.FilesImported} files.");
            }
            else
            {
                ImportPackageResult.Text = $"Import failed: {result.Error}";
                ImportPackageResult.Foreground = (Brush)FindResource("ErrorColorBrush");
            }
        }
        catch (Exception ex)
        {
            ImportPackageResult.Text = $"Import failed: {ex.Message}";
            ImportPackageResult.Foreground = (Brush)FindResource("ErrorColorBrush");
        }
    }

    private async System.Threading.Tasks.Task LoadRecentPackagesAsync()
    {
        try
        {
            PackageListStatus.Text = "Loading...";
            var packages = await _packagerService.GetRecentPackagesAsync();

            if (packages == null || packages.Count == 0)
            {
                PackageListStatus.Text = "No packages found.";
                PackageList.ItemsSource = null;
                return;
            }

            PackageListStatus.Text = $"{packages.Count} package(s) found";
            PackageList.ItemsSource = packages;
        }
        catch (Exception ex)
        {
            PackageListStatus.Text = $"Failed to load packages: {ex.Message}";
            PackageListStatus.Foreground = (Brush)FindResource("ErrorColorBrush");
        }
    }

}
