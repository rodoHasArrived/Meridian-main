using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using ISmQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;
using ISmService = Meridian.Contracts.SecurityMaster.ISecurityMasterService;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

public sealed partial class SecurityMasterViewModel
{
    private void StartWorkflowPolling()
    {
        _workflowCts?.Cancel();
        _workflowCts?.Dispose();
        _workflowCts = new CancellationTokenSource();
        var token = _workflowCts.Token;

        _workflowPollingTask = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                await RefreshOperatorWorkflowAsync(token).ConfigureAwait(false);

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, token);
    }

    private async Task RefreshOperatorWorkflowAsync(CancellationToken ct = default)
    {
        try
        {
            var statusTask = _workflowClient.GetIngestStatusAsync(ct);
            var conflictsTask = _workflowClient.GetOpenConflictsAsync(ct);
            await Task.WhenAll(statusTask, conflictsTask).ConfigureAwait(false);

            var status = await statusTask.ConfigureAwait(false);
            var conflicts = (await conflictsTask.ConfigureAwait(false))
                .OrderBy(conflict => conflict.DetectedAt)
                .ToArray();
            await PrimeConflictSecurityContextCacheAsync(conflicts, ct).ConfigureAwait(false);

            var previousSelectedConflictId = SelectedConflict?.ConflictId;
            var previousSelectedGroupSecurityId = SelectedConflictGroup?.SecurityId;
            var preferredSecurityId = SelectedSecurity?.SecurityId;

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _suppressConflictLaneSelectionSync = true;
                _suppressConflictDrivenSelectionLoad = true;

                try
                {
                    OpenConflictCount = conflicts.Length;
                    OpenConflicts.Clear();
                    foreach (var conflict in conflicts)
                    {
                        OpenConflicts.Add(conflict);
                    }

                    RebuildConflictLaneGroups();

                    var selectedGroup = ConflictGroups.FirstOrDefault(group =>
                            previousSelectedGroupSecurityId.HasValue && group.SecurityId == previousSelectedGroupSecurityId.Value)
                        ?? ConflictGroups.FirstOrDefault(group =>
                            preferredSecurityId.HasValue && group.SecurityId == preferredSecurityId.Value)
                        ?? ConflictGroups.FirstOrDefault();

                    SelectedConflictGroup = selectedGroup;
                    SelectedConflictEntry = selectedGroup?.Conflicts.FirstOrDefault(entry =>
                            previousSelectedConflictId.HasValue && entry.Conflict.ConflictId == previousSelectedConflictId.Value)
                        ?? selectedGroup?.Conflicts.FirstOrDefault();
                    SelectedConflict = SelectedConflictEntry?.Conflict;
                }
                finally
                {
                    _suppressConflictLaneSelectionSync = false;
                    _suppressConflictDrivenSelectionLoad = false;
                }

                if (SelectedSecurity is not null)
                {
                    AlignConflictLaneToSelectedSecurity();
                }

                RebuildFilteredConflicts();
                ApplyIngestStatus(status);
                NotifyConflictWorkflowCommandsChanged();
            });
        }
        catch (OperationCanceledException)
        {
            // Cancellation is expected when the view is torn down mid-operation; benign.
            _loggingService.LogDebug(
                "Security Master operation cancelled.",
                ("view", GetType().Name));
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to refresh Security Master operator workflow status", ex);

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                WorkflowStatusText = "Operator workflow polling surface is unavailable.";
                WorkflowRetrievedAtText = "-";
            });
        }
    }

    private void ApplyIngestStatus(SecurityMasterIngestStatusResponse? status)
    {
        if (status is null)
        {
            WorkflowStatusText = "Ingest polling surface unavailable.";
            WorkflowRetrievedAtText = "-";
            ImportSessionSummary = "Security Master ingest status is unavailable from the workstation service.";
            return;
        }

        WorkflowRetrievedAtText = FormatHelpers.FormatUtcTimestamp(status.RetrievedAtUtc);
        if (status.IsImportActive && status.ActiveImport is not null)
        {
            WorkflowStatusText = $"Active ingest {status.ActiveImport.Processed}/{status.ActiveImport.Total} via {status.ActiveImport.FileExtension}.";
            ImportSessionSummary =
                $"Active ingest: {status.ActiveImport.Processed}/{status.ActiveImport.Total} processed • {status.ActiveImport.Imported} imported • {status.ActiveImport.Skipped} skipped • {status.ActiveImport.Failed} failed.";
            return;
        }

        if (status.LastCompleted is not null)
        {
            WorkflowStatusText = $"Last ingest completed {FormatHelpers.FormatUtcTimestamp(status.LastCompleted.CompletedAtUtc)}.";
            ImportSessionSummary =
                $"Last ingest: {status.LastCompleted.Imported} imported • {status.LastCompleted.Skipped} skipped • {status.LastCompleted.Failed} failed • {status.LastCompleted.ConflictsDetected} conflicts.";
            return;
        }

        WorkflowStatusText = "No ingest activity has been recorded yet.";
        ImportSessionSummary = "No Security Master ingest has completed yet.";
    }
}
