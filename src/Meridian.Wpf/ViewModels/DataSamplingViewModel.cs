using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

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

    public DataSamplingViewModel()
    {
        Symbols = new ObservableCollection<string>();
        RecentSamples = new ObservableCollection<SampleSummary>();
        Presets = new ObservableCollection<string> { "Exploratory", "Regression", "Volume Spike", "Gap Check" };
        SelectedPreset = Presets[0];
    }

    public ObservableCollection<string> Symbols { get; }

    public ObservableCollection<SampleSummary> RecentSamples { get; }

    public ObservableCollection<string> Presets { get; }

    public string SampleName
    {
        get => _sampleName;
        set
        {
            if (SetProperty(ref _sampleName, value))
            {
                UpdateValidationSummary();
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
        set => SetProperty(ref _symbolInput, value);
    }

    public DateTime? FromDate
    {
        get => _fromDate;
        set
        {
            if (SetProperty(ref _fromDate, value))
            {
                UpdateValidationSummary();
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
                UpdateValidationSummary();
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
                UpdateValidationSummary();
            }
        }
    }

    public bool IncludeTrades
    {
        get => _includeTrades;
        set => SetProperty(ref _includeTrades, value);
    }

    public bool IncludeQuotes
    {
        get => _includeQuotes;
        set => SetProperty(ref _includeQuotes, value);
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

    public void AddSymbol()
    {
        var symbol = SymbolInput.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(symbol) && !Symbols.Contains(symbol))
        {
            Symbols.Add(symbol);
            SymbolInput = string.Empty;
            UpdateValidationSummary();
        }
    }

    public void GenerateSample()
    {
        UpdateValidationSummary();
        if (!string.IsNullOrEmpty(ValidationSummary))
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

    public void SavePreset()
    {
        if (!string.IsNullOrWhiteSpace(SampleName) && !Presets.Contains(SampleName))
        {
            Presets.Add(SampleName.Trim());
            SelectedPreset = SampleName.Trim();
            StatusMessage = $"Preset \"{SampleName}\" saved.";
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

    private void UpdateValidationSummary()
    {
        var errors = new[]
        {
            this[nameof(SampleName)],
            this[nameof(SampleSize)],
            this[nameof(ToDate)]
        };

        ValidationSummary = string.Join(" ", errors.Where(error => !string.IsNullOrWhiteSpace(error)));
    }
}

public sealed class SampleSummary
{
    public string Name { get; init; } = string.Empty;

    public int SymbolCount { get; init; }

    public string DateRange { get; init; } = string.Empty;

    public string CreatedAt { get; init; } = string.Empty;
}
