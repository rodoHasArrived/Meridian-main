using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

public partial class ExportPresetsPage : Page
{
    private readonly ExportPresetsViewModel _viewModel = new();

    public ExportPresetsPage()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel.Initialize();
    }

    private void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SavePreset();
    }

    private void DeletePreset_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.DeletePreset();
    }
}

public sealed class ExportPresetsViewModel : BindableBase, IDataErrorInfo
{
    private ExportPreset? _selectedPreset;
    private string _draftName = string.Empty;
    private string _draftFormat = "CSV";
    private string _draftNotes = string.Empty;
    private string _validationSummary = string.Empty;
    private string _statusMessage = string.Empty;

    public ExportPresetsViewModel()
    {
        Presets = new ObservableCollection<ExportPreset>();
        Formats = new ObservableCollection<string> { "CSV", "Parquet", "JSON", "Excel" };
    }

    public ObservableCollection<ExportPreset> Presets { get; }

    public ObservableCollection<string> Formats { get; }

    public ExportPreset? SelectedPreset
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

    public bool CanDelete => SelectedPreset != null;

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

    public string Error => string.Empty;

    public string this[string columnName]
    {
        get
        {
            return columnName switch
            {
                nameof(DraftName) when string.IsNullOrWhiteSpace(DraftName) => "Preset name is required.",
                _ => string.Empty
            };
        }
    }

    public void Initialize()
    {
        if (Presets.Count == 0)
        {
            Presets.Add(new ExportPreset("Monthly Research Pack", "CSV", "Standard metrics with charts."));
            Presets.Add(new ExportPreset("Operations Snapshot", "Excel", "Daily oversight export."));
        }

        if (SelectedPreset == null)
        {
            SelectedPreset = Presets.FirstOrDefault();
        }
    }

    public void SavePreset()
    {
        UpdateValidationSummary();
        if (!string.IsNullOrEmpty(ValidationSummary))
        {
            StatusMessage = "Resolve validation errors before saving.";
            return;
        }

        if (SelectedPreset == null)
        {
            var newPreset = new ExportPreset(DraftName.Trim(), DraftFormat, DraftNotes.Trim());
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
        if (SelectedPreset == null)
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

public sealed class ExportPreset : BindableBase
{
    private string _name;
    private string _format;
    private string _notes;
    private string _updatedAt;

    public ExportPreset(string name, string format, string notes)
    {
        _name = name;
        _format = format;
        _notes = notes;
        _updatedAt = DateTime.Now.ToString("MMM dd, yyyy HH:mm");
    }

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
