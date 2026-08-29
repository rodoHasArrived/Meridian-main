using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Application.SecurityMaster;
using Meridian.Ui.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// Bulk import of securities from a CSV or JSON file. Split out of the main view-model file so the
/// import flow -- including the actor it records every created security against -- reads in one
/// piece.
/// </summary>
public sealed partial class SecurityMasterViewModel
{
    // ── Bulk Import ──────────────────────────────────────────────────────────
    private async Task OnImportFromFile(CancellationToken ct = default)
    {
        var openDialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "CSV/JSON Files|*.csv;*.json",
            DefaultExt = ".csv",
            Title = "Import Securities"
        };

        if (openDialog.ShowDialog() != true)
            return;

        // Imported securities are recorded against the signed-in operator, so refuse an import this
        // session cannot attribute rather than stamping a placeholder across every created row.
        if (_authenticationSession is null || !_authenticationSession.TryGetAuthenticatedActor(out var importedBy))
        {
            _loggingService.LogWarning("Security Master import refused: no authenticated desktop operator to attribute the import to.");
            _notificationService.ShowNotification(
                "Security Master",
                "Sign in before importing: imported securities are recorded against the signed-in operator.",
                NotificationType.Error);
            return;
        }

        try
        {
            IsImporting = true;
            ImportTotal = 0;
            ImportProcessed = 0;
            ImportImported = 0;
            ImportFailed = 0;
            IsImportResultVisible = false;

            var fileContent = await System.IO.File.ReadAllTextAsync(openDialog.FileName, ct).ConfigureAwait(false);
            var fileExtension = System.IO.Path.GetExtension(openDialog.FileName);

            var progress = new Progress<SecurityMasterImportProgress>(p =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    ImportTotal = p.Total;
                    ImportProcessed = p.Processed;
                    ImportImported = p.Imported;
                    ImportFailed = p.Failed;
                    RaisePropertyChanged(nameof(ImportStatus));
                });
            });

            var result = await _importService.ImportAsync(fileContent, fileExtension, importedBy, progress, ct).ConfigureAwait(false);

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ImportTotal = result.Imported + result.Skipped + result.Failed;
                ImportImported = result.Imported;
                ImportFailed = result.Failed;

                var summary = $"Imported {result.Imported} securities, Skipped {result.Skipped}, Failed {result.Failed}.";
                if (result.Errors.Any())
                {
                    summary += $"\r\nErrors:\r\n{string.Join("\r\n", result.Errors.Take(10))}";
                    if (result.Errors.Count > 10)
                        summary += $"\r\n... and {result.Errors.Count - 10} more errors.";
                }

                ImportResultSummary = summary;
                IsImportResultVisible = true;
                RaisePropertyChanged(nameof(ImportStatus));

                _notificationService.ShowNotification(
                    "Security Master Import",
                    $"Import completed: {result.Imported} imported, {result.Failed} failed.",
                    result.Failed == 0 ? NotificationType.Success : NotificationType.Warning);
            });

            // Refresh search results
            _ = SearchAsync();
            _ = RefreshOperatorWorkflowAsync();
        }
        catch (OperationCanceledException)
        {
            _notificationService.ShowNotification("Security Master Import", "Import cancelled.", NotificationType.Info);
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Security Master import failed", ex);
            _notificationService.ShowNotification("Security Master Import", $"Import failed: {ex.Message}", NotificationType.Error);
        }
        finally
        {
            IsImporting = false;
        }
    }

    private void OnCloseImportResult()
    {
        IsImportResultVisible = false;
        ImportResultSummary = string.Empty;
    }
}
