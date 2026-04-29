using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Export Presets page. Manages the list of presets and draft-edit state.
/// Loads initial presets from <see cref="ExportPresetService"/> on first call to <see cref="InitializeAsync"/>.
/// </summary>
public sealed class ExportPresetsViewModel : BindableBase, IDataErrorInfo
{
    private readonly ExportPresetService _presetService;

    private ExportPresetItem? _selectedPreset;
    private string _draftName = string.Empty;
    private string _draftFormat = "CSV";
    private string _draftNotes = string.Empty;
    private string _validationSummary = string.Empty;
    private string _statusMessage = string.Empty;

    public ExportPresetsViewModel()
        : this(ExportPresetService.Instance)
    {
    }

    internal ExportPresetsViewModel(ExportPresetService presetService)
    {
        _presetService = presetService;
        Presets = new ObservableCollection<ExportPresetItem>();
        Formats = new ObservableCollection<string> { "CSV", "Parquet", "JSONL", "Excel", "Lean" };

        SavePresetCommand = new RelayCommand(SavePreset, () => CanSavePreset);
        DeletePresetCommand = new RelayCommand(DeletePreset, () => CanDelete);

        Presets.CollectionChanged += (_, _) => RefreshPresentationState();
    }

    public ObservableCollection<ExportPresetItem> Presets { get; }

    public ObservableCollection<string> Formats { get; }

    public IRelayCommand SavePresetCommand { get; }

    public IRelayCommand DeletePresetCommand { get; }

    public ExportPresetItem? SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (SetProperty(ref _selectedPreset, value))
            {
                LoadSelectedPreset();
                RefreshPresentationState();
            }
        }
    }

    public string DraftName
    {
        get => _draftName;
        set
        {
            if (SetProperty(ref _draftName, value))
            {
                UpdateValidationSummary();
                RefreshPresentationState();
            }
        }
    }

    public string DraftFormat
    {
        get => _draftFormat;
        set
        {
            if (SetProperty(ref _draftFormat, value))
            {
                RefreshPresentationState();
            }
        }
    }

    public string DraftNotes
    {
        get => _draftNotes;
        set => SetProperty(ref _draftNotes, value);
    }

    public bool CanDelete => SelectedPreset != null && !SelectedPreset.IsBuiltIn;

    public bool CanSavePreset => !string.IsNullOrWhiteSpace(DraftName);

    public bool HasPresets => Presets.Count > 0;

    public bool IsPresetLibraryEmpty => !HasPresets;

    public string PresetLibraryStateText => Presets.Count switch
    {
        0 => "No export presets loaded yet.",
        1 => "1 preset available for reporting handoffs.",
        _ => $"{Presets.Count} presets available for reporting handoffs."
    };

    public string PresetReadinessTitle => CanSavePreset
        ? "Preset ready"
        : "Preset setup incomplete";

    public string PresetReadinessDetail
    {
        get
        {
            if (!CanSavePreset)
            {
                return "Enter a preset name before saving a reporting export preset.";
            }

            var format = string.IsNullOrWhiteSpace(DraftFormat) ? "selected" : DraftFormat;
            if (SelectedPreset?.IsBuiltIn == true)
            {
                return $"Saving creates a custom {format} preset from the selected built-in template.";
            }

            return SelectedPreset == null
                ? $"Ready to add a reusable {format} export preset."
                : $"Ready to update the selected {format} export preset.";
        }
    }

    public string ValidationSummary
    {
        get => _validationSummary;
        private set => SetProperty(ref _validationSummary, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                RaisePropertyChanged(nameof(IsStatusVisible));
            }
        }
    }

    public bool IsStatusVisible => !string.IsNullOrWhiteSpace(StatusMessage);

    // IDataErrorInfo
    public string Error => string.Empty;

    public string this[string columnName] => columnName switch
    {
        nameof(DraftName) when string.IsNullOrWhiteSpace(DraftName) => "Preset name is required.",
        _ => string.Empty
    };

    /// <summary>
    /// Loads presets from <see cref="ExportPresetService"/> and populates the preset list.
    /// Safe to call multiple times; subsequent calls refresh the list.
    /// </summary>
    public async Task InitializeAsync()
    {
        await _presetService.InitializeAsync();

        Presets.Clear();
        foreach (var preset in _presetService.Presets)
        {
            Presets.Add(new ExportPresetItem(
                preset.Name,
                preset.Format.ToString(),
                preset.Description ?? string.Empty,
                isBuiltIn: preset.IsBuiltIn,
                updatedAt: preset.UpdatedAt.ToString("MMM dd, yyyy HH:mm")));
        }

        SelectedPreset = Presets.FirstOrDefault();
        RefreshPresentationState();
    }

    public void SavePreset()
    {
        UpdateValidationSummary();
        if (!string.IsNullOrEmpty(ValidationSummary))
        {
            StatusMessage = "Resolve validation errors before saving.";
            RefreshPresentationState();
            return;
        }

        if (SelectedPreset == null || SelectedPreset.IsBuiltIn)
        {
            var newPreset = new ExportPresetItem(DraftName.Trim(), DraftFormat, DraftNotes.Trim());
            Presets.Insert(0, newPreset);
            SelectedPreset = newPreset;
            StatusMessage = $"Preset \"{DraftName}\" added.";
        }
        else
        {
            SelectedPreset.Name = DraftName.Trim();
            SelectedPreset.Format = DraftFormat;
            SelectedPreset.Notes = DraftNotes.Trim();
            SelectedPreset.UpdatedAt = DateTime.Now.ToString("MMM dd, yyyy HH:mm");
            StatusMessage = $"Preset \"{DraftName}\" updated.";
        }

        RefreshPresentationState();
    }

    public void DeletePreset()
    {
        if (SelectedPreset == null || SelectedPreset.IsBuiltIn)
        {
            return;
        }

        var removed = SelectedPreset;
        Presets.Remove(removed);
        SelectedPreset = Presets.FirstOrDefault();
        StatusMessage = $"Preset \"{removed.Name}\" removed.";
        RefreshPresentationState();
    }

    private void LoadSelectedPreset()
    {
        if (SelectedPreset == null)
        {
            DraftName = string.Empty;
            DraftFormat = Formats.FirstOrDefault() ?? "CSV";
            DraftNotes = string.Empty;
            return;
        }

        DraftName = SelectedPreset.Name;
        DraftFormat = SelectedPreset.Format;
        DraftNotes = SelectedPreset.Notes;
    }

    private void UpdateValidationSummary()
    {
        ValidationSummary = this[nameof(DraftName)];
    }

    private void RefreshPresentationState()
    {
        RaisePropertyChanged(nameof(CanDelete));
        RaisePropertyChanged(nameof(CanSavePreset));
        RaisePropertyChanged(nameof(HasPresets));
        RaisePropertyChanged(nameof(IsPresetLibraryEmpty));
        RaisePropertyChanged(nameof(PresetLibraryStateText));
        RaisePropertyChanged(nameof(PresetReadinessTitle));
        RaisePropertyChanged(nameof(PresetReadinessDetail));

        SavePresetCommand.NotifyCanExecuteChanged();
        DeletePresetCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>
/// UI model for a single export preset row in the Export Presets page.
/// Distinct from <c>Meridian.Contracts.Export.ExportPreset</c>, which is the storage contract.
/// </summary>
public sealed class ExportPresetItem : BindableBase
{
    private string _name;
    private string _format;
    private string _notes;
    private string _updatedAt;

    public ExportPresetItem(string name, string format, string notes, bool isBuiltIn = false, string? updatedAt = null)
    {
        _name = name;
        _format = format;
        _notes = notes;
        _updatedAt = updatedAt ?? DateTime.Now.ToString("MMM dd, yyyy HH:mm");
        IsBuiltIn = isBuiltIn;
    }

    /// <summary>Whether this preset originates from the built-in service defaults (non-deletable).</summary>
    public bool IsBuiltIn { get; }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string Format
    {
        get => _format;
        set => SetProperty(ref _format, value);
    }

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    public string UpdatedAt
    {
        get => _updatedAt;
        set => SetProperty(ref _updatedAt, value);
    }
}
