using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;

namespace Meridian.Wpf.ViewModels;

public sealed class DataSamplingViewModel : BindableBase, IDataErrorInfo
{
    private string _sampleName = string.Empty;
    private string _description = string.Empty;
    private string _symbolInput = string.Empty;
    private DateTime? _fromDate;
    private DateTime? _toDate;
    private int _sampleSize = 500;
    private bool _includeTrades = true;
    private bool _includeQuotes = true;
    private string _selectedPreset = string.Empty;
    private string _validationSummary = string.Empty;
    private string _statusMessage = string.Empty;
    private string _samplingReadinessTitle = "Sample setup incomplete";
    private string _samplingReadinessDetail = "Name the sample and add at least one symbol before generating a data subset.";
    private string _symbolScopeText = "No symbols added";
    private string _recentSamplesStateText = "No sample runs retained in this session yet.";

    public DataSamplingViewModel()
    {
        Symbols = new ObservableCollection<string>();
        RecentSamples = new ObservableCollection<SampleSummary>();
        Presets = new ObservableCollection<string> { "Exploratory", "Regression", "Volume Spike", "Gap Check" };
        SelectedPreset = Presets[0];
        AddSymbolCommand = new RelayCommand(AddSymbol, CanAddSymbol);
        GenerateSampleCommand = new RelayCommand(GenerateSample, CanGenerateSample);
        SavePresetCommand = new RelayCommand(SavePreset, CanSavePreset);

        Symbols.CollectionChanged += (_, _) => RefreshValidationState();
        RecentSamples.CollectionChanged += (_, _) => RefreshRecentSamplesState();
        Presets.CollectionChanged += (_, _) => RefreshPresentationState();

        RefreshPresentationState();
    }

    public ObservableCollection<string> Symbols { get; }

    public ObservableCollection<SampleSummary> RecentSamples { get; }

    public ObservableCollection<string> Presets { get; }

    public RelayCommand AddSymbolCommand { get; }

    public RelayCommand GenerateSampleCommand { get; }

    public RelayCommand SavePresetCommand { get; }

    public string SampleName
    {
        get => _sampleName;
        set
        {
            if (SetProperty(ref _sampleName, value))
            {
                RefreshValidationState();
            }
        }
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public string SymbolInput
    {
        get => _symbolInput;
        set
        {
            if (SetProperty(ref _symbolInput, value))
            {
                AddSymbolCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public DateTime? FromDate
    {
        get => _fromDate;
        set
        {
            if (SetProperty(ref _fromDate, value))
            {
                RefreshValidationState();
            }
        }
    }

    public DateTime? ToDate
    {
        get => _toDate;
        set
        {
            if (SetProperty(ref _toDate, value))
            {
                RefreshValidationState();
            }
        }
    }

    public int SampleSize
    {
        get => _sampleSize;
        set
        {
            if (SetProperty(ref _sampleSize, value))
            {
                RefreshValidationState();
            }
        }
    }

    public bool IncludeTrades
    {
        get => _includeTrades;
        set
        {
            if (SetProperty(ref _includeTrades, value))
            {
                RefreshValidationState();
            }
        }
    }

    public bool IncludeQuotes
    {
        get => _includeQuotes;
        set
        {
            if (SetProperty(ref _includeQuotes, value))
            {
                RefreshValidationState();
            }
        }
    }

    public string SelectedPreset
    {
        get => _selectedPreset;
        set => SetProperty(ref _selectedPreset, value);
    }

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

    public string SamplingReadinessTitle
    {
        get => _samplingReadinessTitle;
        private set => SetProperty(ref _samplingReadinessTitle, value);
    }

    public string SamplingReadinessDetail
    {
        get => _samplingReadinessDetail;
        private set => SetProperty(ref _samplingReadinessDetail, value);
    }

    public string SymbolScopeText
    {
        get => _symbolScopeText;
        private set => SetProperty(ref _symbolScopeText, value);
    }

    public string RecentSamplesStateText
    {
        get => _recentSamplesStateText;
        private set => SetProperty(ref _recentSamplesStateText, value);
    }

    public string Error => string.Empty;

    public string this[string columnName]
    {
        get
        {
            return columnName switch
            {
                nameof(SampleName) when string.IsNullOrWhiteSpace(SampleName) => "Sample name is required.",
                nameof(SampleSize) when SampleSize <= 0 => "Sample size must be greater than zero.",
                nameof(ToDate) when FromDate.HasValue && ToDate.HasValue && FromDate > ToDate => "Start date must be before the end date.",
                nameof(IncludeTrades) when !IncludeTrades && !IncludeQuotes => "Select at least one data type.",
                nameof(IncludeQuotes) when !IncludeTrades && !IncludeQuotes => "Select at least one data type.",
                _ => string.Empty
            };
        }
    }

    public void Initialize()
    {
        if (RecentSamples.Count == 0)
        {
            RecentSamples.Add(new SampleSummary
            {
                Name = "Tech Momentum",
                SymbolCount = 6,
                DateRange = "Last 30 days",
                CreatedAt = DateTime.Today.AddDays(-1).ToString("MMM dd, yyyy")
            });
            RecentSamples.Add(new SampleSummary
            {
                Name = "Liquidity Scan",
                SymbolCount = 12,
                DateRange = "Last 7 days",
                CreatedAt = DateTime.Today.AddDays(-3).ToString("MMM dd, yyyy")
            });
        }
    }

    public bool CanAddSymbol()
    {
        var symbol = NormalizeSymbol(SymbolInput);
        return symbol.Length > 0 && !Symbols.Contains(symbol);
    }

    public void AddSymbol()
    {
        var symbol = NormalizeSymbol(SymbolInput);
        if (!string.IsNullOrWhiteSpace(symbol) && !Symbols.Contains(symbol))
        {
            Symbols.Add(symbol);
            SymbolInput = string.Empty;
            StatusMessage = $"Added {symbol} to the sample scope.";
        }
    }

    public bool CanGenerateSample() => !GetValidationErrors(includeSymbolRequirement: true).Any();

    public void GenerateSample()
    {
        RefreshValidationState(includeSymbolRequirement: true);
        if (!CanGenerateSample())
        {
            StatusMessage = "Resolve validation errors before generating the sample.";
            return;
        }

        var symbolsCount = Symbols.Count;
        if (symbolsCount == 0)
        {
            StatusMessage = "Add at least one symbol to generate a sample.";
            ValidationSummary = "At least one symbol is required.";
            return;
        }

        RecentSamples.Insert(0, new SampleSummary
        {
            Name = SampleName.Trim(),
            SymbolCount = symbolsCount,
            DateRange = FormatDateRange(),
            CreatedAt = DateTime.Now.ToString("MMM dd, yyyy HH:mm")
        });

        StatusMessage = $"Sample \"{SampleName}\" queued with {symbolsCount} symbols.";
    }

    public bool CanSavePreset()
    {
        var presetName = SampleName.Trim();
        return presetName.Length > 0 && !ContainsPreset(presetName);
    }

    public void SavePreset()
    {
        var presetName = SampleName.Trim();
        if (presetName.Length == 0)
        {
            StatusMessage = "Name the sample before saving a preset.";
            return;
        }

        if (ContainsPreset(presetName))
        {
            StatusMessage = $"Preset \"{presetName}\" already exists.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(SampleName) && !Presets.Contains(SampleName))
        {
            Presets.Add(presetName);
            SelectedPreset = presetName;
            StatusMessage = $"Preset \"{presetName}\" saved.";
        }
    }

    private string FormatDateRange()
    {
        if (FromDate.HasValue && ToDate.HasValue)
        {
            return $"{FromDate:MMM dd} - {ToDate:MMM dd}";
        }

        return "Open range";
    }

    private void RefreshValidationState(bool includeSymbolRequirement = false)
    {
        ValidationSummary = string.Join(" ", GetValidationErrors(includeSymbolRequirement));
        RefreshPresentationState();
    }

    private string[] GetValidationErrors(bool includeSymbolRequirement)
    {
        var errors = new[]
        {
            this[nameof(SampleName)],
            this[nameof(SampleSize)],
            this[nameof(ToDate)],
            this[nameof(IncludeTrades)],
            includeSymbolRequirement && Symbols.Count == 0 ? "At least one symbol is required." : string.Empty
        };

        return errors
            .Where(error => !string.IsNullOrWhiteSpace(error))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private void RefreshPresentationState()
    {
        SymbolScopeText = FormatSymbolScope();
        var readinessErrors = GetValidationErrors(includeSymbolRequirement: true);
        if (readinessErrors.Length == 0)
        {
            SamplingReadinessTitle = "Sample ready";
            SamplingReadinessDetail = $"{SampleSize:N0} rows for {SymbolScopeText} over {FormatDateRange()} with {FormatDataTypeScope()}.";
        }
        else
        {
            SamplingReadinessTitle = "Sample setup incomplete";
            SamplingReadinessDetail = string.Join(" ", readinessErrors);
        }

        AddSymbolCommand.NotifyCanExecuteChanged();
        GenerateSampleCommand.NotifyCanExecuteChanged();
        SavePresetCommand.NotifyCanExecuteChanged();
    }

    private void RefreshRecentSamplesState()
    {
        RecentSamplesStateText = RecentSamples.Count switch
        {
            0 => "No sample runs retained in this session yet.",
            1 => "1 sample run retained in this session.",
            _ => $"{RecentSamples.Count} sample runs retained in this session."
        };
    }

    private string FormatSymbolScope()
    {
        return Symbols.Count switch
        {
            0 => "No symbols added",
            1 => $"1 symbol: {Symbols[0]}",
            <= 4 => $"{Symbols.Count} symbols: {string.Join(", ", Symbols)}",
            _ => $"{Symbols.Count} symbols: {string.Join(", ", Symbols.Take(4))} +{Symbols.Count - 4} more"
        };
    }

    private string FormatDataTypeScope()
    {
        if (IncludeTrades && IncludeQuotes)
            return "trades and quotes";
        if (IncludeTrades)
            return "trades";
        if (IncludeQuotes)
            return "quotes";

        return "no data types";
    }

    private bool ContainsPreset(string presetName) =>
        Presets.Any(preset => string.Equals(preset, presetName, StringComparison.OrdinalIgnoreCase));

    private static string NormalizeSymbol(string value) => value.Trim().ToUpperInvariant();
}

public sealed class SampleSummary
{
    public string Name { get; init; } = string.Empty;

    public int SymbolCount { get; init; }

    public string DateRange { get; init; } = string.Empty;

    public string CreatedAt { get; init; } = string.Empty;
}
