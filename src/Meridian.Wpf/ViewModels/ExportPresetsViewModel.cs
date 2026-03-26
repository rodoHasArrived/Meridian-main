using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
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
    }

    public ObservableCollection<ExportPresetItem> Presets { get; }

    public ObservableCollection<string> Formats { get; }

    public ExportPresetItem? SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (SetProperty(ref _selectedPreset, value))
            {
                LoadSelectedPreset();
                RaisePropertyChanged(nameof(CanDelete));
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
            }
        }
    }

    public string DraftFormat
    {
        get => _draftFormat;
        set => SetProperty(ref _draftFormat, value);
    }

    public string DraftNotes
    {
        get => _draftNotes;
        set => SetProperty(ref _draftNotes, value);
    }

    public bool CanDelete => SelectedPreset != null && !SelectedPreset.IsBuiltIn;

    public string ValidationSummary
    {
        get => _validationSummary;
        private set => SetProperty(ref _validationSummary, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

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
    }

    public void SavePreset()
    {
        UpdateValidationSummary();
        if (!string.IsNullOrEmpty(ValidationSummary))
        {
            StatusMessage = "Resolve validation errors before saving.";
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
