using System.Collections.Generic;
using System.Collections.ObjectModel;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Workstation.Commands;
using Meridian.Wpf.Workstation.Models;
using Meridian.Wpf.Workstation.State;
using Meridian.Wpf.Workstation.ViewModels.Base;

namespace Meridian.Wpf.Workstation.ViewModels;

public abstract class WorkspaceViewModelBase : Base.WorkspaceViewModelBase
{
    private static readonly WorkstationStateModel DefaultLoadingState = WorkstationStateModel.Loading(
        "Workspace loading",
        "The workstation is preparing the current workspace.");

    public WorkstationStateModel State
    {
        get => MapToStateModel(WorkspaceRegionState);
        protected set => WorkspaceRegionState = MapToRegionState(value);
    }

    private static WorkstationStateModel MapToStateModel(WorkstationRegionState source)
    {
        return source.Status switch
        {
            WorkstationRegionStatus.Empty => WorkstationStateModel.Empty(source.Title, source.Detail, source.ActionLabel, source.ActionId),
            WorkstationRegionStatus.Error => WorkstationStateModel.Error(source.Title, source.Detail, source.ActionLabel, source.ActionId),
            WorkstationRegionStatus.Success => WorkstationStateModel.Ready(source.Title, source.Detail, source.ActionLabel, source.ActionId),
            WorkstationRegionStatus.Warning => WorkstationStateModel.Recovery(
                source.Title,
                source.Detail,
                new WorkstationActionPostureModel("Review", "Review the current region state", string.Empty, string.Empty)),
            _ => WorkstationStateModel.Ready(source.Title, source.Detail, source.ActionLabel, source.ActionId)
        };
    }

    private static WorkstationRegionState MapToRegionState(WorkstationStateModel? source)
    {
        var resolved = source ?? DefaultLoadingState;

        return resolved.Kind switch
        {
            WorkstationStateKind.Loading => WorkstationRegionState.Loading(resolved.Title, resolved.Detail),
            WorkstationStateKind.Empty => WorkstationRegionState.Empty(resolved.Title, resolved.Detail),
            WorkstationStateKind.Error => WorkstationRegionState.Error(resolved.Title, resolved.Detail, resolved.TargetText, resolved.ActionText),
            WorkstationStateKind.Stale => WorkstationRegionState.Warning(resolved.Title, resolved.Detail),
            WorkstationStateKind.Blocked => WorkstationRegionState.Warning(resolved.Title, resolved.Detail),
            WorkstationStateKind.Ready => WorkstationRegionState.Success(resolved.Title, resolved.Detail),
            _ => WorkstationRegionState.Loading(resolved.Title, resolved.Detail)
        };
    }
}

public abstract class DetailWorkspaceViewModelBase : Base.DetailWorkspaceViewModelBase<InspectorPanelModel>
{
    private InspectorPanelModel _inspector = new();

    public InspectorPanelModel Inspector
    {
        get => _inspector;
        protected set => SetProperty(ref _inspector, value);
    }
}

public abstract class CommandHostViewModel : WorkspaceViewModelBase
{
    private WorkstationCommandGroupModel _commandGroup = new();

    public WorkstationCommandGroupModel CommandGroup
    {
        get => _commandGroup;
        set
        {
            if (!SetProperty(ref _commandGroup, value ?? new()))
            {
                return;
            }

            CommandDescriptors = BuildCommandDescriptors(_commandGroup);
        }
    }

    private static IReadOnlyList<CommandViewModel> BuildCommandDescriptors(WorkstationCommandGroupModel source)
    {
        var commands = new List<CommandViewModel>();
        foreach (var command in source.PrimaryCommands ?? Array.Empty<WorkstationCommandModel>())
        {
            commands.Add(ToCommandDescriptor(command));
        }

        foreach (var command in source.SecondaryCommands ?? Array.Empty<WorkstationCommandModel>())
        {
            commands.Add(ToCommandDescriptor(command));
        }

        return commands;
    }

    private static CommandViewModel ToCommandDescriptor(WorkstationCommandModel command)
    {
        return new CommandViewModel
        {
            Id = command.Id,
            Label = command.Label,
            IconGlyph = command.Glyph,
            Tone = command.Tone,
            Tooltip = string.IsNullOrWhiteSpace(command.Description) ? command.Label : command.Description,
            IsEnabled = command.IsEnabled
        };
    }
}

public class TableViewModel<TRow> : Base.TableViewModel<TRow>
{
    private TRow? _selectedRecord;

    public TRow? SelectedRecord
    {
        get => _selectedRecord;
        set => SetProperty(ref _selectedRecord, value);
    }
}

public class FilterableTableViewModel<TRow, TFilter> : TableViewModel<TRow>
{
    private TFilter? _filter;

    public TFilter? Filter
    {
        get => _filter;
        set => SetProperty(ref _filter, value);
    }
}

public class InspectorViewModel<TItem> : BindableBase
{
    private TItem? _selectedItem;

    public TItem? SelectedItem
    {
        get => _selectedItem;
        set => SetProperty(ref _selectedItem, value);
    }
}
