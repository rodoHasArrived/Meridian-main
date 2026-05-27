using System;
using System.Linq;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Shell.ViewModels;

public sealed class OperatorInboxViewModel : BindableBase
{
    private OperatorInboxDto? _inbox;
    private string _error = "Operator queue has not refreshed yet.";
    private OperatorWorkItemDto? _primaryWorkItem;
    private string _targetPageTag = "NotificationCenter";

    public OperatorInboxDto? Inbox => _inbox;

    public OperatorWorkItemDto? PrimaryWorkItem => _primaryWorkItem;

    public string ButtonText
    {
        get
        {
            if (_inbox is null)
            {
                return "Queue";
            }

            if (_inbox.ReviewCount > 0)
            {
                return $"Queue ({_inbox.ReviewCount})";
            }

            return _inbox.Items.Count > 0
                ? $"Queue ({_inbox.Items.Count})"
                : "Queue";
        }
    }

    public string Summary => _inbox?.Summary ?? _error;

    public string PrimaryLabel => _primaryWorkItem?.Label ?? "No open operator work items";

    public string TargetText => _targetPageTag;

    public int ReviewCount => _inbox?.ReviewCount ?? 0;

    public string Tone => ResolveTone(_inbox);

    public void Apply(
        OperatorInboxDto? inbox,
        string? error,
        Func<OperatorWorkItemDto?, string?> resolveTargetPageTag)
    {
        ArgumentNullException.ThrowIfNull(resolveTargetPageTag);

        _inbox = inbox;
        _error = string.IsNullOrWhiteSpace(error)
            ? "Operator queue has not refreshed yet."
            : error;
        _primaryWorkItem = GetPrimaryWorkItem(inbox);
        _targetPageTag = resolveTargetPageTag(_primaryWorkItem) ?? "NotificationCenter";

        RaisePropertyChanged(nameof(Inbox));
        RaisePropertyChanged(nameof(PrimaryWorkItem));
        RaisePropertyChanged(nameof(ButtonText));
        RaisePropertyChanged(nameof(Summary));
        RaisePropertyChanged(nameof(PrimaryLabel));
        RaisePropertyChanged(nameof(TargetText));
        RaisePropertyChanged(nameof(ReviewCount));
        RaisePropertyChanged(nameof(Tone));
    }

    public static OperatorWorkItemDto? GetPrimaryWorkItem(OperatorInboxDto? inbox)
        => inbox?.Items
            .OrderByDescending(item => item.Tone == OperatorWorkItemToneDto.Critical)
            .ThenByDescending(item => item.Tone == OperatorWorkItemToneDto.Warning)
            .ThenBy(item => item.CreatedAt)
            .FirstOrDefault();

    public static string ResolveTone(OperatorInboxDto? inbox)
    {
        if (inbox is null)
        {
            return WorkspaceTone.Neutral;
        }

        if (inbox.CriticalCount > 0)
        {
            return WorkspaceTone.Danger;
        }

        if (inbox.WarningCount > 0)
        {
            return WorkspaceTone.Warning;
        }

        return inbox.ReviewCount > 0
            ? WorkspaceTone.Info
            : WorkspaceTone.Success;
    }
}
