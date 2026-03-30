using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.Input;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.ViewModels;

public sealed class SplitPaneViewModel : BindableBase
{
    private PaneLayout _selectedLayout = PaneLayouts.Single;
    private int _activePaneIndex;

    public IReadOnlyList<PaneLayout> Layouts => PaneLayouts.All;

    public PaneLayout SelectedLayout
    {
        get => _selectedLayout;
        private set => SetProperty(ref _selectedLayout, value);
    }

    public int ActivePaneIndex
    {
        get => _activePaneIndex;
        private set => SetProperty(ref _activePaneIndex, value);
    }

    public IRelayCommand<PaneLayout?> SelectLayoutCommand { get; }
    public IRelayCommand<int?> SetActivePaneCommand { get; }

    public event EventHandler<PaneLayout>? LayoutChanged;
    public event EventHandler<int>? ActivePaneChanged;

    public SplitPaneViewModel()
    {
        SelectLayoutCommand = new RelayCommand<PaneLayout?>(OnSelectLayout);
        SetActivePaneCommand = new RelayCommand<int?>(OnSetActivePane);
    }

    private void OnSelectLayout(PaneLayout? layout)
    {
        if (layout is null) return;
        SelectedLayout = layout;
        LayoutChanged?.Invoke(this, layout);
    }

    private void OnSetActivePane(int? index)
    {
        if (index is null or < 0) return;
        ActivePaneIndex = index.Value;
        ActivePaneChanged?.Invoke(this, index.Value);
    }
}
