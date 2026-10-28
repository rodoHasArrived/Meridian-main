using System;
using Meridian.Ui.Services;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Shell.Root;

/// <summary>
/// Classifies dropped files and routes supported file types to the relevant desktop workflow.
/// </summary>
public sealed class FileDropRouter
{
    private readonly NavigationService _navigationService;
    private readonly DropImportService _dropImportService;

    public FileDropRouter(NavigationService navigationService)
        : this(navigationService, DropImportService.Instance)
    {
    }

    public FileDropRouter(NavigationService navigationService, DropImportService dropImportService)
    {
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _dropImportService = dropImportService ?? throw new ArgumentNullException(nameof(dropImportService));
    }

    public string GetFriendlyFileType(string filePath)
    {
        var fileType = _dropImportService.DetectFileType(filePath);
        return _dropImportService.GetTypeFriendlyName(fileType);
    }

    public void RouteFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        var fileType = _dropImportService.DetectFileType(filePath);
        var pageKey = _dropImportService.GetTargetPageKey(fileType);
        _navigationService.NavigateTo(pageKey, filePath);
    }
}
