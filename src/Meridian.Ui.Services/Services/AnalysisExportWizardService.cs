using System.Text;
using System.Text.Json;

namespace Meridian.Ui.Services;

/// <summary>
/// Service for guided analysis export wizard.
/// Provides tool-specific export profiles and generates ready-to-use exports.
/// </summary>
public sealed class AnalysisExportWizardService
{
    private readonly DataCompletenessService _completenessService;
    private readonly StorageAnalyticsService _storageService;
    private readonly ConfigService _configService;

    public AnalysisExportWizardService()
    {
        _completenessService = new DataCompletenessService(ManifestService.Instance, new TradingCalendarService());
        _storageService = StorageAnalyticsService.Instance;
        _configService = ConfigService.Instance;
    }

    /// <summary>
    /// Gets available export profiles for different analysis tools.
    /// </summary>
    public IReadOnlyList<ExportProfile> GetExportProfiles()
    {
        return new List<ExportProfile>
        {
            new()
            {
                Id = "python-pandas",
                Name = "Python / Pandas",
                Description = "Parquet files with appropriate dtypes for pandas DataFrame",
                Icon = "\uE943",
                OutputFormat = "Parquet",
                Compression = "snappy",
                IncludeLoaderCode = true,
                LoaderLanguage = "python",
                FileExtension = ".parquet",
                Features = new[] { "Efficient columnar storage", "Native pandas support", "Type preservation" }
            },
            new()
            {
                Id = "python-pytorch",
                Name = "Python / PyTorch",
                Description = "HDF5 files optimized for ML training pipelines",
                Icon = "\uE945",
                OutputFormat = "HDF5",
                Compression = "gzip",
                IncludeLoaderCode = true,
                LoaderLanguage = "python",
                FileExtension = ".h5",
                Features = new[] { "ML-ready format", "Chunked storage", "Feature scaling metadata" }
            },
            new()
            {
                Id = "r-dataframe",
                Name = "R / data.frame",
                Description = "CSV with proper formatting for R analysis",
                Icon = "\uE8A1",
                OutputFormat = "CSV",
                Compression = "none",
                IncludeLoaderCode = true,
                LoaderLanguage = "r",
                FileExtension = ".csv",
                Features = new[] { "R-compatible dates", "NA handling", "Factor encoding" }
            },
            new()
            {
                Id = "runmat",
                Name = "RunMat",
                Description = "Numeric CSV plus a MATLAB-style loader script for RunMat research workflows",
                Icon = "\uE943",
                OutputFormat = "CSV",
                Compression = "none",
                IncludeLoaderCode = true,
                LoaderLanguage = "runmat",
                FileExtension = ".csv",
                Features = new[] { "Numeric-only CSV", "Unix millisecond timestamps", "Ready-to-run .m loader script" }
            },
            new()
            {
                Id = "quantconnect-lean",
                Name = "QuantConnect Lean",
                Description = "Native Lean data format for backtesting",
                Icon = "\uE9D9",
                OutputFormat = "Lean",
                Compression = "zip",
                IncludeLoaderCode = false,
                LoaderLanguage = "csharp",
                FileExtension = ".zip",
                Features = new[] { "Direct Lean compatibility", "Multiple resolutions", "Corporate actions" }
            },
            new()
            {
                Id = "excel",
                Name = "Microsoft Excel",
                Description = "XLSX with multiple sheets and formatting",
                Icon = "\uE8D5",
                OutputFormat = "Excel",
                Compression = "none",
                IncludeLoaderCode = false,
                LoaderLanguage = "none",
                FileExtension = ".xlsx",
                Features = new[] { "Pivot-ready", "Charts included", "Summary statistics" }
            },
            new()
            {
                Id = "sql-postgres",
                Name = "PostgreSQL / TimescaleDB",
                Description = "SQL COPY format optimized for time-series databases",
                Icon = "\uE8F1",
                OutputFormat = "SQL",
                Compression = "gzip",
                IncludeLoaderCode = true,
                LoaderLanguage = "sql",
                FileExtension = ".sql.gz",
                Features = new[] { "COPY format", "Schema included", "TimescaleDB hypertables" }
            },
            new()
            {
                Id = "clickhouse",
                Name = "ClickHouse",
                Description = "Native ClickHouse format for analytics",
                Icon = "\uE8F1",
                OutputFormat = "ClickHouse",
                Compression = "lz4",
                IncludeLoaderCode = true,
                LoaderLanguage = "sql",
                FileExtension = ".clickhouse",
                Features = new[] { "Columnar format", "High compression", "Fast analytics" }
            },
            new()
            {
                Id = "jupyter",
                Name = "Jupyter Notebook",
                Description = "Ready-to-run notebook with data loading and exploration",
                Icon = "\uE8A1",
                OutputFormat = "Notebook",
                Compression = "none",
                IncludeLoaderCode = true,
                LoaderLanguage = "python",
                FileExtension = ".ipynb",
                Features = new[] { "Interactive exploration", "Sample visualizations", "Documentation" }
            }
        };
    }

    /// <summary>
    /// Gets available data types for export.
    /// </summary>
    public IReadOnlyList<ExportDataType> GetDataTypes()
    {
        return new List<ExportDataType>
        {
            new() { Id = "trades", Name = "Trades", Description = "Tick-by-tick trade data", Icon = "\uE8AB" },
            new() { Id = "quotes", Name = "Quotes", Description = "Best bid/offer quotes", Icon = "\uE8D4" },
            new() { Id = "depth", Name = "Order Book", Description = "L2 market depth snapshots", Icon = "\uE8A1" },
            new() { Id = "bars_1m", Name = "1-Minute Bars", Description = "OHLCV aggregated to 1 minute", Icon = "\uE9D9" },
            new() { Id = "bars_5m", Name = "5-Minute Bars", Description = "OHLCV aggregated to 5 minutes", Icon = "\uE9D9" },
            new() { Id = "bars_1h", Name = "Hourly Bars", Description = "OHLCV aggregated to 1 hour", Icon = "\uE9D9" },
            new() { Id = "bars_1d", Name = "Daily Bars", Description = "OHLCV aggregated to 1 day", Icon = "\uE9D9" }
        };
    }

    /// <summary>
    /// Estimates export size and duration.
    /// </summary>
    public async Task<ExportEstimate> EstimateExportAsync(
        ExportConfiguration config,
        CancellationToken ct = default)
    {
        var estimate = new ExportEstimate();

        // Get data availability info
        var completeness = await _completenessService.GetCompletenessReportAsync(
            config.Symbols,
            config.FromDate,
            config.ToDate,
            ct);

        estimate.TotalRecords = completeness.TotalActualEvents;
        estimate.AvailableRecords = completeness.TotalActualEvents;
        estimate.CompletenessPercent = completeness.OverallCompleteness;

        // Estimate file size based on format
        var bytesPerRecord = config.Profile.OutputFormat switch
        {
            "Parquet" => 50,
            "CSV" => 120,
            "HDF5" => 60,
            "Excel" => 150,
            "SQL" => 100,
            _ => 80
        };

        estimate.EstimatedSizeBytes = estimate.AvailableRecords * bytesPerRecord;

        // Apply compression factor
        var compressionFactor = config.Profile.Compression switch
        {
            "snappy" => 0.4,
            "gzip" => 0.3,
            "lz4" => 0.5,
            "zstd" => 0.25,
            _ => 1.0
        };

        estimate.EstimatedSizeBytes = (long)(estimate.EstimatedSizeBytes * compressionFactor);

        // Estimate duration (rough estimate: 100MB/minute)
        estimate.EstimatedDurationSeconds = (int)(estimate.EstimatedSizeBytes / (100.0 * 1024 * 1024) * 60);
        estimate.EstimatedDurationSeconds = Math.Max(5, estimate.EstimatedDurationSeconds);

        // Check for potential issues
        if (completeness.OverallCompleteness < 95)
        {
            estimate.Warnings.Add($"Data completeness is {completeness.OverallCompleteness:F1}% - some gaps may exist");
        }

        if (completeness.GapCount > 0)
        {
            estimate.Warnings.Add($"{completeness.GapCount} data gaps detected in selected range");
        }

        if (estimate.EstimatedSizeBytes > 1024L * 1024 * 1024)
        {
            estimate.Warnings.Add("Large export - consider splitting by date range");
        }

        return estimate;
    }

    /// <summary>
    /// Generates a data quality pre-export report.
    /// </summary>
    public async Task<PreExportQualityReport> GenerateQualityReportAsync(
        ExportConfiguration config,
        CancellationToken ct = default)
    {
        var report = new PreExportQualityReport
        {
            GeneratedAt = DateTime.UtcNow,
            Symbols = config.Symbols,
            FromDate = config.FromDate,
            ToDate = config.ToDate
        };

        // Get completeness data
        var completeness = await _completenessService.GetCompletenessReportAsync(
            config.Symbols,
            config.FromDate,
            config.ToDate,
            ct);

        report.OverallCompleteness = completeness.OverallCompleteness;
        report.TotalTradingDays = completeness.TotalTradingDays;
        report.DaysWithData = completeness.TotalTradingDays - completeness.DaysWithGaps;
        report.GapCount = completeness.GapCount;

        // Per-symbol quality
        foreach (var symbol in config.Symbols)
        {
            var symbolReport = await _completenessService.GetSymbolCompletenessAsync(
                symbol, config.FromDate, config.ToDate, ct);

            report.SymbolQuality.Add(new SymbolQualityInfo
            {
                Symbol = symbol,
                Completeness = symbolReport.Score,
                RecordCount = symbolReport.RecordCount,
                HasGaps = symbolReport.MissingDays.Count > 0,
                QualityGrade = GetQualityGrade(symbolReport.Score)
            });
        }

        // Analysis suitability assessment
        report.SuitabilityAssessment = AssessSuitability(report);

        return report;
    }

    private string GetQualityGrade(double completeness)
    {
        return completeness switch
        {
            >= 99 => "A+",
            >= 95 => "A",
            >= 90 => "B",
            >= 80 => "C",
            >= 70 => "D",
            _ => "F"
        };
    }

    private string AssessSuitability(PreExportQualityReport report)
    {
        if (report.OverallCompleteness >= 99)
            return "Excellent - Data is highly suitable for all analysis types including ML training";
        if (report.OverallCompleteness >= 95)
            return "Good - Data is suitable for most analysis with minor gap handling";
        if (report.OverallCompleteness >= 90)
            return "Fair - Consider gap filling before use in production backtests";
        if (report.OverallCompleteness >= 80)
            return "Limited - Significant gaps may affect analysis accuracy";
        return "Poor - Data quality issues may severely impact analysis results";
    }

    /// <summary>
    /// Generates loader code for the selected profile.
    /// </summary>
    public string GenerateLoaderCode(ExportProfile profile, string exportPath, string[] symbols)
    {
        return profile.LoaderLanguage switch
        {
            "python" => GeneratePythonLoader(profile, exportPath, symbols),
            "r" => GenerateRLoader(profile, exportPath, symbols),
            "sql" => GenerateSqlLoader(profile, exportPath),
            "runmat" => GenerateRunMatLoader(profile, exportPath, symbols),
            _ => string.Empty
        };
    }

    private string GeneratePythonLoader(ExportProfile profile, string exportPath, string[] symbols)
    {
        var sb = new StringBuilder();
        sb.AppendLine("\"\"\"");
        sb.AppendLine("Auto-generated data loader for Meridian export");
        sb.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"Profile: {profile.Name}");
        sb.AppendLine("\"\"\"");
        sb.AppendLine();
        sb.AppendLine("import pandas as pd");
        sb.AppendLine("from pathlib import Path");

        if (profile.OutputFormat == "Parquet")
        {
            sb.AppendLine("import pyarrow.parquet as pq");
            sb.AppendLine();
            sb.AppendLine($"DATA_PATH = Path(r\"{exportPath}\")");
            sb.AppendLine();
            sb.AppendLine("def load_data(symbol: str = None) -> pd.DataFrame:");
            sb.AppendLine("    \"\"\"Load market data from parquet files.\"\"\"");
            sb.AppendLine("    if symbol:");
            sb.AppendLine("        file_path = DATA_PATH / f\"{symbol}.parquet\"");
            sb.AppendLine("        return pd.read_parquet(file_path)");
            sb.AppendLine("    ");
            sb.AppendLine("    # Load all symbols");
            sb.AppendLine("    dfs = []");
            sb.AppendLine("    for file in DATA_PATH.glob(\"*.parquet\"):");
            sb.AppendLine("        df = pd.read_parquet(file)");
            sb.AppendLine("        df['symbol'] = file.stem");
            sb.AppendLine("        dfs.append(df)");
            sb.AppendLine("    return pd.concat(dfs, ignore_index=True)");
        }
        else if (profile.OutputFormat == "HDF5")
        {
            sb.AppendLine("import h5py");
            sb.AppendLine("import numpy as np");
            sb.AppendLine();
            sb.AppendLine($"DATA_PATH = Path(r\"{exportPath}\")");
            sb.AppendLine();
            sb.AppendLine("def load_data(symbol: str) -> dict:");
            sb.AppendLine("    \"\"\"Load market data from HDF5 file.\"\"\"");
            sb.AppendLine("    with h5py.File(DATA_PATH / f\"{symbol}.h5\", 'r') as f:");
            sb.AppendLine("        return {");
            sb.AppendLine("            'timestamps': f['timestamps'][:],");
            sb.AppendLine("            'prices': f['prices'][:],");
            sb.AppendLine("            'volumes': f['volumes'][:],");
            sb.AppendLine("        }");
        }

        sb.AppendLine();
        sb.AppendLine("# Example usage:");
        sb.AppendLine($"# df = load_data(\"{symbols.FirstOrDefault() ?? "SPY"}\")");
        sb.AppendLine("# print(df.head())");

        return sb.ToString();
    }

    private string GenerateRLoader(ExportProfile profile, string exportPath, string[] symbols)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Auto-generated data loader for Meridian export");
        sb.AppendLine($"# Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"# Profile: {profile.Name}");
        sb.AppendLine();
        sb.AppendLine("library(tidyverse)");
        sb.AppendLine("library(lubridate)");
        sb.AppendLine();
        sb.AppendLine($"DATA_PATH <- \"{exportPath.Replace("\\", "/")}\"");
        sb.AppendLine();
        sb.AppendLine("load_data <- function(symbol = NULL) {");
        sb.AppendLine("  if (!is.null(symbol)) {");
        sb.AppendLine("    file_path <- file.path(DATA_PATH, paste0(symbol, \".csv\"))");
        sb.AppendLine("    return(read_csv(file_path, col_types = cols(");
        sb.AppendLine("      timestamp = col_datetime(),");
        sb.AppendLine("      price = col_double(),");
        sb.AppendLine("      volume = col_integer()");
        sb.AppendLine("    )))");
        sb.AppendLine("  }");
        sb.AppendLine("  ");
        sb.AppendLine("  # Load all symbols");
        sb.AppendLine("  files <- list.files(DATA_PATH, pattern = \"\\\\.csv$\", full.names = TRUE)");
        sb.AppendLine("  map_dfr(files, ~read_csv(.x) %>% mutate(symbol = tools::file_path_sans_ext(basename(.x))))");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("# Example usage:");
        sb.AppendLine($"# df <- load_data(\"{symbols.FirstOrDefault() ?? "SPY"}\")");
        sb.AppendLine("# head(df)");

        return sb.ToString();
    }

    private string GenerateSqlLoader(ExportProfile profile, string exportPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("-- Auto-generated SQL loader for Meridian export");
        sb.AppendLine($"-- Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"-- Profile: {profile.Name}");
        sb.AppendLine();

        if (profile.Id == "sql-postgres")
        {
            sb.AppendLine("-- Create table");
            sb.AppendLine("CREATE TABLE IF NOT EXISTS market_data (");
            sb.AppendLine("    id BIGSERIAL PRIMARY KEY,");
            sb.AppendLine("    symbol VARCHAR(20) NOT NULL,");
            sb.AppendLine("    timestamp TIMESTAMPTZ NOT NULL,");
            sb.AppendLine("    price DECIMAL(18,8) NOT NULL,");
            sb.AppendLine("    volume BIGINT NOT NULL,");
            sb.AppendLine("    data_type VARCHAR(20) NOT NULL");
            sb.AppendLine(");");
            sb.AppendLine();
            sb.AppendLine("-- Create TimescaleDB hypertable (if using TimescaleDB)");
            sb.AppendLine("-- SELECT create_hypertable('market_data', 'timestamp', if_not_exists => TRUE);");
            sb.AppendLine();
            sb.AppendLine("-- Create index");
            sb.AppendLine("CREATE INDEX IF NOT EXISTS idx_market_data_symbol_time ON market_data (symbol, timestamp DESC);");
            sb.AppendLine();
            sb.AppendLine("-- Load data (run from psql)");
            sb.AppendLine($"-- \\COPY market_data FROM '{exportPath}/data.csv' WITH CSV HEADER;");
        }
        else if (profile.Id == "clickhouse")
        {
            sb.AppendLine("-- Create table");
            sb.AppendLine("CREATE TABLE IF NOT EXISTS market_data (");
            sb.AppendLine("    symbol String,");
            sb.AppendLine("    timestamp DateTime64(3),");
            sb.AppendLine("    price Decimal64(8),");
            sb.AppendLine("    volume UInt64,");
            sb.AppendLine("    data_type String");
            sb.AppendLine(") ENGINE = MergeTree()");
            sb.AppendLine("ORDER BY (symbol, timestamp);");
            sb.AppendLine();
            sb.AppendLine("-- Load data");
            sb.AppendLine($"-- INSERT INTO market_data FROM INFILE '{exportPath}/data.csv' FORMAT CSV;");
        }

        return sb.ToString();
    }

    private string GenerateRunMatLoader(ExportProfile profile, string exportPath, string[] symbols)
    {
        var exampleSymbol = symbols.FirstOrDefault() ?? "SPY";
        var sb = new StringBuilder();
        sb.AppendLine("% Auto-generated RunMat loader for Meridian export");
        sb.AppendLine($"% Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"% Profile: {profile.Name}");
        sb.AppendLine("% Column order: Timestamp, Price, Size, BidPrice, BidSize, AskPrice, AskSize, Open, High, Low, Close, Volume");
        sb.AppendLine();
        sb.AppendLine($"DATA_PATH = '{exportPath.Replace("\\", "/")}';");
        sb.AppendLine();
        sb.AppendLine("function data = load_data(symbol)");
        sb.AppendLine("  if nargin < 1");
        sb.AppendLine("    symbol = '';");
        sb.AppendLine("  end");
        sb.AppendLine("  pattern = '*.csv';");
        sb.AppendLine("  if ~isempty(symbol)");
        sb.AppendLine("    pattern = strcat(symbol, '_*.csv');");
        sb.AppendLine("  end");
        sb.AppendLine("  files = dir(fullfile(DATA_PATH, pattern));");
        sb.AppendLine("  data = [];");
        sb.AppendLine("  for i = 1:numel(files)");
        sb.AppendLine("    data = [data; readmatrix(fullfile(DATA_PATH, files(i).name))];");
        sb.AppendLine("  end");
        sb.AppendLine("end");
        sb.AppendLine();
        sb.AppendLine("% Example usage:");
        sb.AppendLine($"% data = load_data('{exampleSymbol}');");
        sb.AppendLine("% plot(data(:,1), data(:,2));");

        return sb.ToString();
    }

    /// <summary>
    /// Executes the export with the given configuration.
    /// </summary>
    public async Task<ExportResult> ExecuteExportAsync(
        ExportConfiguration config,
        IProgress<ExportProgress>? progress = null,
        CancellationToken ct = default)
    {
        var result = new ExportResult
        {
            StartTime = DateTime.UtcNow,
            Configuration = config
        };

        try
        {
            // Create output directory
            Directory.CreateDirectory(config.OutputPath);

            var totalSymbols = config.Symbols.Length;
            var processedSymbols = 0;

            foreach (var symbol in config.Symbols)
            {
                ct.ThrowIfCancellationRequested();

                progress?.Report(new ExportProgress
                {
                    CurrentSymbol = symbol,
                    ProcessedSymbols = processedSymbols,
                    TotalSymbols = totalSymbols,
                    PercentComplete = (double)processedSymbols / totalSymbols * 100,
                    StatusMessage = $"Exporting {symbol}..."
                });

                // Export data for this symbol based on the selected profile
                var exportedRecords = await ExportSymbolDataAsync(
                    symbol,
                    config,
                    progress,
                    ct);

                result.TotalRecordsExported += exportedRecords.RecordCount;
                result.OutputSizeBytes += exportedRecords.OutputBytes;

                if (!string.IsNullOrEmpty(exportedRecords.OutputFile))
                {
                    result.GeneratedFiles.Add(exportedRecords.OutputFile);
                }

                processedSymbols++;
                result.ProcessedSymbols++;
            }

            // Generate loader code if requested
            if (config.Profile.IncludeLoaderCode)
            {
                var loaderCode = GenerateLoaderCode(config.Profile, config.OutputPath, config.Symbols);
                var loaderFile = Path.Combine(config.OutputPath, $"loader{GetLoaderExtension(config.Profile.LoaderLanguage)}");
                await File.WriteAllTextAsync(loaderFile, loaderCode, ct);
                result.GeneratedFiles.Add(loaderFile);
            }

            // Generate quality report if requested
            if (config.IncludeQualityReport)
            {
                var qualityReport = await GenerateQualityReportAsync(config, ct);
                var reportPath = Path.Combine(config.OutputPath, "quality_report.json");
                await File.WriteAllTextAsync(reportPath,
                    JsonSerializer.Serialize(qualityReport, DesktopJsonOptions.PrettyPrint), ct);
                result.GeneratedFiles.Add(reportPath);
            }

            // Generate schema file if requested
            if (config.IncludeSchema)
            {
                var schemaPath = Path.Combine(config.OutputPath, "schema.json");
                var schema = GenerateDataSchema(config);
                await File.WriteAllTextAsync(schemaPath, schema, ct);
                result.GeneratedFiles.Add(schemaPath);
            }

            result.Success = true;
            result.EndTime = DateTime.UtcNow;
        }
        catch (OperationCanceledException)
        {
            result.Success = false;
            result.ErrorMessage = "Export cancelled";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        result.EndTime = DateTime.UtcNow;
        return result;
    }

    /// <summary>
    /// Exports data for a single symbol to the configured format.
    /// </summary>
    private async Task<SymbolExportResult> ExportSymbolDataAsync(
        string symbol,
        ExportConfiguration config,
        IProgress<ExportProgress>? progress,
        CancellationToken ct)
    {
        var result = new SymbolExportResult { Symbol = symbol };

        try
        {
            // Load configuration to get data root path
            var appConfig = await _configService.LoadConfigAsync();
            var dataRoot = appConfig?.DataRoot ?? "data";

            // Determine source files based on data types requested
            var sourceFiles = new List<string>();
            foreach (var dataType in config.DataTypes)
            {
                var pattern = dataType switch
                {
                    "trades" => $"*{symbol}*trade*.jsonl*",
                    "quotes" => $"*{symbol}*quote*.jsonl*",
                    "depth" => $"*{symbol}*depth*.jsonl*",
                    "bars_1m" or "bars_5m" or "bars_1h" or "bars_1d" => $"*{symbol}*bar*.jsonl*",
                    _ => $"*{symbol}*.jsonl*"
                };

                if (Directory.Exists(dataRoot))
                {
                    var files = Directory.GetFiles(dataRoot, pattern, SearchOption.AllDirectories)
                        .Where(f => IsInDateRange(f, config.FromDate, config.ToDate))
                        .ToList();
                    sourceFiles.AddRange(files);
                }
            }

            if (sourceFiles.Count == 0)
            {
                // No source files found, return empty result
                return result;
            }

            // Export based on the selected format
            result = config.Profile.OutputFormat switch
            {
                "CSV" => await ExportToCsvAsync(symbol, sourceFiles, config, ct),
                "Parquet" => await ExportToParquetPlaceholderAsync(symbol, sourceFiles, config, ct),
                "Excel" => await ExportToExcelPlaceholderAsync(symbol, sourceFiles, config, ct),
                "SQL" => await ExportToSqlAsync(symbol, sourceFiles, config, ct),
                "Notebook" => await ExportToNotebookAsync(symbol, sourceFiles, config, ct),
                "HDF5" => await ExportToHdf5PlaceholderAsync(symbol, sourceFiles, config, ct),
                "ClickHouse" => await ExportToClickHousePlaceholderAsync(symbol, sourceFiles, config, ct),
                "Lean" => await ExportToLeanPlaceholderAsync(symbol, sourceFiles, config, ct),
                _ => await ExportToCsvAsync(symbol, sourceFiles, config, ct) // Default to CSV
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AnalysisExportWizard] Export failed for {symbol}: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Checks if a file's date is within the specified range based on filename.
    /// </summary>
    private static bool IsInDateRange(string filePath, DateOnly fromDate, DateOnly toDate)
    {
        var fileName = Path.GetFileName(filePath);

        // Try to extract date from filename (common patterns: YYYY-MM-DD, YYYYMMDD)
        var datePatterns = new[]
        {
            @"(\d{4})-(\d{2})-(\d{2})",
            @"(\d{4})(\d{2})(\d{2})"
        };

        foreach (var pattern in datePatterns)
        {
            var match = System.Text.RegularExpressions.Regex.Match(fileName, pattern);
            if (match.Success)
            {
                if (int.TryParse(match.Groups[1].Value, out var year) &&
                    int.TryParse(match.Groups[2].Value, out var month) &&
                    int.TryParse(match.Groups[3].Value, out var day))
                {
                    try
                    {
                        var fileDate = new DateOnly(year, month, day);
                        return fileDate >= fromDate && fileDate <= toDate;
                    }
                    catch
                    {
                        // Invalid date, include by default
                    }
                }
            }
        }

        // If we can't determine the date, include the file
        return true;
    }

    /// <summary>
    /// Exports data to CSV format.
    /// </summary>
    private async Task<SymbolExportResult> ExportToCsvAsync(
        string symbol,
        List<string> sourceFiles,
        ExportConfiguration config,
        CancellationToken ct)
    {
        var result = new SymbolExportResult { Symbol = symbol };
        var outputFile = Path.Combine(config.OutputPath, $"{symbol}.csv");

        await using var writer = new StreamWriter(outputFile, false, Encoding.UTF8);

        var headerWritten = false;
        long recordCount = 0;

        foreach (var sourceFile in sourceFiles)
        {
            ct.ThrowIfCancellationRequested();

            // Handle gzipped files
            Stream inputStream;
            if (sourceFile.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            {
                var fileStream = File.OpenRead(sourceFile);
                inputStream = new System.IO.Compression.GZipStream(fileStream, System.IO.Compression.CompressionMode.Decompress);
            }
            else
            {
                inputStream = File.OpenRead(sourceFile);
            }

            await using (inputStream)
            using (var reader = new StreamReader(inputStream))
            {
                string? line;
                while ((line = await reader.ReadLineAsync(ct)) != null)
                {
                    ct.ThrowIfCancellationRequested();

                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    try
                    {
                        // Parse JSONL and convert to CSV
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;

                        // Write header on first record
                        if (!headerWritten)
                        {
                            var headers = root.EnumerateObject().Select(p => p.Name);
                            await writer.WriteLineAsync(string.Join(",", headers));
                            headerWritten = true;
                        }

                        // Write values
                        var values = root.EnumerateObject().Select(p => FormatCsvValue(p.Value));
                        await writer.WriteLineAsync(string.Join(",", values));
                        recordCount++;
                    }
                    catch (JsonException)
                    {
                        // Skip malformed lines
                    }
                }
            }
        }

        result.OutputFile = outputFile;
        result.RecordCount = recordCount;
        result.OutputBytes = new FileInfo(outputFile).Length;

        return result;
    }

    /// <summary>
    /// Formats a JSON value for CSV output.
    /// </summary>
    private static string FormatCsvValue(JsonElement element)
    {
        var value = element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "",
            _ => element.GetRawText()
        };

        // Escape CSV values containing commas, quotes, or newlines
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }

    /// <summary>
    /// Placeholder for Parquet export (requires Apache.Arrow or Parquet.Net library).
    /// </summary>
    private async Task<SymbolExportResult> ExportToParquetPlaceholderAsync(
        string symbol,
        List<string> sourceFiles,
        ExportConfiguration config,
        CancellationToken ct)
    {
        // Parquet export requires additional libraries (Apache.Arrow.Parquet)
        // For now, fall back to CSV with a note
        var result = await ExportToCsvAsync(symbol, sourceFiles, config, ct);

        // Rename to indicate it's a placeholder
        var csvFile = result.OutputFile;
        if (!string.IsNullOrEmpty(csvFile) && File.Exists(csvFile))
        {
            var parquetNote = Path.Combine(config.OutputPath, $"{symbol}_parquet_note.txt");
            await File.WriteAllTextAsync(parquetNote,
                "Note: Full Parquet export requires Apache.Arrow library.\n" +
                "CSV data has been exported as a fallback.\n" +
                "To convert to Parquet, use the generated Python loader with pandas:\n" +
                "  df = pd.read_csv('" + $"{symbol}.csv" + "')\n" +
                "  df.to_parquet('" + $"{symbol}.parquet" + "')", ct);
            result.OutputFile = csvFile;
        }

        return result;
    }

    /// <summary>
    /// Placeholder for Excel export (requires EPPlus or similar library).
    /// Falls back to CSV with instructions for Excel conversion.
    /// </summary>
    private async Task<SymbolExportResult> ExportToExcelPlaceholderAsync(
        string symbol,
        List<string> sourceFiles,
        ExportConfiguration config,
        CancellationToken ct)
    {
        // Excel export requires additional libraries (EPPlus, ClosedXML, etc.)
        // For now, export to CSV which can be opened in Excel
        var result = await ExportToCsvAsync(symbol, sourceFiles, config, ct);

        // Add a note file explaining the Excel fallback
        var csvFile = result.OutputFile;
        if (!string.IsNullOrEmpty(csvFile) && File.Exists(csvFile))
        {
            var excelNote = Path.Combine(config.OutputPath, $"{symbol}_excel_note.txt");
            await File.WriteAllTextAsync(excelNote,
                "Note: Full Excel (.xlsx) export requires the EPPlus library.\n" +
                "CSV data has been exported as a fallback.\n\n" +
                "To convert to Excel:\n" +
                "  Option 1: Open the CSV directly in Excel and save as .xlsx\n" +
                "  Option 2: Use Python with openpyxl:\n" +
                "    import pandas as pd\n" +
                $"    df = pd.read_csv('{symbol}.csv')\n" +
                $"    df.to_excel('{symbol}.xlsx', index=False)\n\n" +
                "  Option 3: Use the generated Python loader and export to Excel\n\n" +
                "The CSV file is fully compatible with Excel and can be imported using:\n" +
                "  Data > From Text/CSV > Select the file", ct);
            result.OutputFile = csvFile;
        }

        return result;
    }

    /// <summary>
    /// Placeholder for HDF5 export (requires h5py or HDF5.NET library).
    /// Falls back to CSV with instructions for HDF5 conversion.
    /// </summary>
    private async Task<SymbolExportResult> ExportToHdf5PlaceholderAsync(
        string symbol,
        List<string> sourceFiles,
        ExportConfiguration config,
        CancellationToken ct)
    {
        // HDF5 export requires additional libraries (HDF5.NET, or Python h5py)
        // For now, export to CSV with conversion instructions
        var result = await ExportToCsvAsync(symbol, sourceFiles, config, ct);

        var csvFile = result.OutputFile;
        if (!string.IsNullOrEmpty(csvFile) && File.Exists(csvFile))
        {
            var hdf5Note = Path.Combine(config.OutputPath, $"{symbol}_hdf5_note.txt");
            await File.WriteAllTextAsync(hdf5Note,
                "Note: Full HDF5 (.h5) export requires the h5py library in Python.\n" +
                "CSV data has been exported as a fallback.\n\n" +
                "To convert to HDF5 for ML pipelines:\n" +
                "  import pandas as pd\n" +
                "  import h5py\n" +
                "  import numpy as np\n\n" +
                $"  df = pd.read_csv('{symbol}.csv')\n" +
                $"  with h5py.File('{symbol}.h5', 'w') as f:\n" +
                "      # Store numeric columns as datasets\n" +
                "      for col in df.select_dtypes(include=[np.number]).columns:\n" +
                "          f.create_dataset(col, data=df[col].values)\n" +
                "      # Store timestamps as ISO strings\n" +
                "      if 'timestamp' in df.columns:\n" +
                "          f.create_dataset('timestamp', data=df['timestamp'].astype(str).values.astype('S'))\n\n" +
                "Benefits of HDF5:\n" +
                "  - Efficient chunked storage for large datasets\n" +
                "  - Native support in PyTorch/TensorFlow\n" +
                "  - Compression support built-in", ct);
            result.OutputFile = csvFile;
        }

        return result;
    }

    /// <summary>
    /// Placeholder for ClickHouse export (requires ClickHouse client).
    /// Falls back to CSV with SQL import script.
    /// </summary>
    private async Task<SymbolExportResult> ExportToClickHousePlaceholderAsync(
        string symbol,
        List<string> sourceFiles,
        ExportConfiguration config,
        CancellationToken ct)
    {
        // ClickHouse native export requires ClickHouse client tools
        // Export to CSV with ClickHouse import instructions
        var result = await ExportToCsvAsync(symbol, sourceFiles, config, ct);

        var csvFile = result.OutputFile;
        if (!string.IsNullOrEmpty(csvFile) && File.Exists(csvFile))
        {
            var chScript = Path.Combine(config.OutputPath, $"{symbol}_clickhouse_import.sql");
            var sb = new StringBuilder();
            sb.AppendLine($"-- ClickHouse Import Script for {symbol}");
            sb.AppendLine($"-- Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine();
            sb.AppendLine("-- Create table for market data");
            sb.AppendLine($"CREATE TABLE IF NOT EXISTS market_data_{symbol.ToLower()} (");
            sb.AppendLine("    timestamp DateTime64(3),");
            sb.AppendLine("    symbol LowCardinality(String),");
            sb.AppendLine("    price Decimal64(8),");
            sb.AppendLine("    volume UInt64,");
            sb.AppendLine("    data_type LowCardinality(String)");
            sb.AppendLine(") ENGINE = MergeTree()");
            sb.AppendLine("ORDER BY (symbol, timestamp);");
            sb.AppendLine();
            sb.AppendLine("-- Import from CSV using clickhouse-client:");
            sb.AppendLine($"-- clickhouse-client --query=\"INSERT INTO market_data_{symbol.ToLower()} FORMAT CSV\" < {symbol}.csv");
            sb.AppendLine();
            sb.AppendLine("-- Or using HTTP interface:");
            sb.AppendLine($"-- curl 'http://localhost:8123/?query=INSERT%20INTO%20market_data_{symbol.ToLower()}%20FORMAT%20CSV' --data-binary @{symbol}.csv");
            sb.AppendLine();
            sb.AppendLine("-- Benefits of ClickHouse:");
            sb.AppendLine("--   - Columnar storage with high compression");
            sb.AppendLine("--   - Sub-second queries on billions of rows");
            sb.AppendLine("--   - Excellent for time-series analytics");

            await File.WriteAllTextAsync(chScript, sb.ToString(), ct);
            result.OutputFile = csvFile;
            result.OutputBytes += new FileInfo(chScript).Length;
        }

        return result;
    }

    /// <summary>
    /// Placeholder for QuantConnect Lean export.
    /// Falls back to CSV with Lean format conversion instructions.
    /// </summary>
    private async Task<SymbolExportResult> ExportToLeanPlaceholderAsync(
        string symbol,
        List<string> sourceFiles,
        ExportConfiguration config,
        CancellationToken ct)
    {
        // Lean format requires specific directory structure and format
        // Export to CSV with instructions for Lean format conversion
        var result = await ExportToCsvAsync(symbol, sourceFiles, config, ct);

        var csvFile = result.OutputFile;
        if (!string.IsNullOrEmpty(csvFile) && File.Exists(csvFile))
        {
            var leanNote = Path.Combine(config.OutputPath, $"{symbol}_lean_note.txt");
            await File.WriteAllTextAsync(leanNote,
                "Note: Full QuantConnect Lean format export requires specific data structure.\n" +
                "CSV data has been exported as a fallback.\n\n" +
                "Lean Data Format Requirements:\n" +
                $"  Data should be placed in: {{Lean Data Root}}/equity/usa/daily/{symbol.ToLower()}.zip\n\n" +
                "To convert to Lean format using the ToolBox:\n" +
                $"  1. Run the Lean Data Writer ToolBox\n" +
                $"  2. Use the CSV converter with --source-dir pointing to this export\n" +
                $"  3. The ToolBox will create properly formatted Lean data files\n\n" +
                "Alternative: Use LeanDataWriter in your algorithm:\n" +
                "  var writer = new LeanDataWriter(\n" +
                "      Globals.DataFolder,\n" +
                $"      Symbol.Create(\"{symbol}\", SecurityType.Equity, Market.USA),\n" +
                "      Resolution.Daily);\n" +
                "  writer.Write(data);\n\n" +
                "For more information:\n" +
                "  https://www.quantconnect.com/docs/v2/writing-algorithms/importing-custom-data", ct);
            result.OutputFile = csvFile;
        }

        return result;
    }

    /// <summary>
    /// Exports data to SQL COPY format for PostgreSQL/TimescaleDB.
    /// </summary>
    private async Task<SymbolExportResult> ExportToSqlAsync(
        string symbol,
        List<string> sourceFiles,
        ExportConfiguration config,
        CancellationToken ct)
    {
        var result = new SymbolExportResult { Symbol = symbol };

        // First export data as CSV for COPY command
        var csvResult = await ExportToCsvAsync(symbol, sourceFiles, config, ct);

        // Generate SQL loader script
        var sqlFile = Path.Combine(config.OutputPath, $"{symbol}_load.sql");
        var sb = new StringBuilder();

        sb.AppendLine($"-- SQL loader for {symbol}");
        sb.AppendLine($"-- Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();
        sb.AppendLine("-- Create table if not exists");
        sb.AppendLine($"CREATE TABLE IF NOT EXISTS market_data_{symbol.ToLower()} (");
        sb.AppendLine("    id BIGSERIAL PRIMARY KEY,");
        sb.AppendLine("    timestamp TIMESTAMPTZ NOT NULL,");
        sb.AppendLine("    price DECIMAL(18,8),");
        sb.AppendLine("    volume BIGINT,");
        sb.AppendLine("    data_type VARCHAR(20)");
        sb.AppendLine(");");
        sb.AppendLine();
        sb.AppendLine("-- Load data from CSV");
        sb.AppendLine($"\\COPY market_data_{symbol.ToLower()} FROM '{symbol}.csv' WITH CSV HEADER;");
        sb.AppendLine();
        sb.AppendLine("-- Create index for time-series queries");
        sb.AppendLine($"CREATE INDEX IF NOT EXISTS idx_{symbol.ToLower()}_timestamp ON market_data_{symbol.ToLower()} (timestamp DESC);");

        await File.WriteAllTextAsync(sqlFile, sb.ToString(), ct);

        result.OutputFile = csvResult.OutputFile;
        result.RecordCount = csvResult.RecordCount;
        result.OutputBytes = csvResult.OutputBytes + new FileInfo(sqlFile).Length;

        return result;
    }

    /// <summary>
    /// Exports data with a Jupyter notebook for interactive analysis.
    /// </summary>
    private async Task<SymbolExportResult> ExportToNotebookAsync(
        string symbol,
        List<string> sourceFiles,
        ExportConfiguration config,
        CancellationToken ct)
    {
        // First export as CSV
        var csvResult = await ExportToCsvAsync(symbol, sourceFiles, config, ct);

        // Generate Jupyter notebook
        var notebookFile = Path.Combine(config.OutputPath, $"{symbol}_analysis.ipynb");

        var notebook = new
        {
            nbformat = 4,
            nbformat_minor = 5,
            metadata = new
            {
                kernelspec = new
                {
                    display_name = "Python 3",
                    language = "python",
                    name = "python3"
                }
            },
            cells = new object[]
            {
                new
                {
                    cell_type = "markdown",
                    metadata = new { },
                    source = new[]
                    {
                        $"# {symbol} Market Data Analysis\n",
                        $"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC\n",
                        "\n",
                        "This notebook provides interactive analysis of the exported market data."
                    }
                },
                new
                {
                    cell_type = "code",
                    metadata = new { },
                    source = new[]
                    {
                        "import pandas as pd\n",
                        "import matplotlib.pyplot as plt\n",
                        "from pathlib import Path\n",
                        "\n",
                        "# Load data\n",
                        $"df = pd.read_csv('{symbol}.csv')\n",
                        "print(f'Loaded {len(df):,} records')\n",
                        "df.head()"
                    },
                    outputs = Array.Empty<object>(),
                    execution_count = (int?)null
                },
                new
                {
                    cell_type = "code",
                    metadata = new { },
                    source = new[]
                    {
                        "# Data summary\n",
                        "df.describe()"
                    },
                    outputs = Array.Empty<object>(),
                    execution_count = (int?)null
                },
                new
                {
                    cell_type = "code",
                    metadata = new { },
                    source = new[]
                    {
                        "# Convert timestamp column if present\n",
                        "if 'timestamp' in df.columns:\n",
                        "    df['timestamp'] = pd.to_datetime(df['timestamp'])\n",
                        "    df.set_index('timestamp', inplace=True)\n",
                        "\n",
                        "# Plot price data if available\n",
                        "price_cols = [c for c in df.columns if 'price' in c.lower()]\n",
                        "if price_cols:\n",
                        "    df[price_cols].plot(figsize=(12, 6), title=f'{symbol} Price')\n",
                        "    plt.show()"
                    },
                    outputs = Array.Empty<object>(),
                    execution_count = (int?)null
                }
            }
        };

        var notebookJson = JsonSerializer.Serialize(notebook, DesktopJsonOptions.PrettyPrint);
        await File.WriteAllTextAsync(notebookFile, notebookJson, ct);

        var result = csvResult;
        result.OutputBytes += new FileInfo(notebookFile).Length;

        return result;
    }

    /// <summary>
    /// Generates a data schema document.
    /// </summary>
    private string GenerateDataSchema(ExportConfiguration config)
    {
        var schema = new
        {
            GeneratedAt = DateTime.UtcNow,
            Profile = config.Profile.Name,
            DataTypes = config.DataTypes,
            DateRange = new { From = config.FromDate.ToString(FormatHelpers.IsoDateFormat), To = config.ToDate.ToString(FormatHelpers.IsoDateFormat) },
            Symbols = config.Symbols,
            Fields = new[]
            {
                new { Name = "timestamp", Type = "datetime", Description = "Event timestamp in UTC" },
                new { Name = "symbol", Type = "string", Description = "Trading symbol" },
                new { Name = "price", Type = "decimal", Description = "Price value" },
                new { Name = "volume", Type = "long", Description = "Trade/quote volume" },
                new { Name = "bid", Type = "decimal", Description = "Bid price (quotes)" },
                new { Name = "ask", Type = "decimal", Description = "Ask price (quotes)" },
                new { Name = "open", Type = "decimal", Description = "Open price (bars)" },
                new { Name = "high", Type = "decimal", Description = "High price (bars)" },
                new { Name = "low", Type = "decimal", Description = "Low price (bars)" },
                new { Name = "close", Type = "decimal", Description = "Close price (bars)" }
            }
        };

        return JsonSerializer.Serialize(schema, DesktopJsonOptions.PrettyPrint);
    }

    /// <summary>
    /// Result of exporting a single symbol.
    /// </summary>
    private sealed class SymbolExportResult
    {
        public string Symbol { get; set; } = string.Empty;
        public string OutputFile { get; set; } = string.Empty;
        public long RecordCount { get; set; }
        public long OutputBytes { get; set; }
    }

    private string GetLoaderExtension(string language)
    {
        return language switch
        {
            "python" => ".py",
            "r" => ".R",
            "sql" => ".sql",
            "runmat" => ".m",
            "csharp" => ".cs",
            _ => ".txt"
        };
    }
}

/// <summary>
/// Export profile for a specific analysis tool.
/// </summary>
public sealed class ExportProfile
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string OutputFormat { get; set; } = string.Empty;
    public string Compression { get; set; } = string.Empty;
    public bool IncludeLoaderCode { get; set; }
    public string LoaderLanguage { get; set; } = string.Empty;
    public string FileExtension { get; set; } = string.Empty;
    public string[] Features { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Data type available for export.
/// </summary>
public sealed class ExportDataType
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
}

/// <summary>
/// Export configuration.
/// </summary>
public sealed class ExportConfiguration
{
    public ExportProfile Profile { get; set; } = new();
    public string[] Symbols { get; set; } = Array.Empty<string>();
    public string[] DataTypes { get; set; } = Array.Empty<string>();
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public string OutputPath { get; set; } = string.Empty;
    public bool IncludeQualityReport { get; set; }
    public bool IncludeSchema { get; set; }
    public Dictionary<string, string> AdditionalOptions { get; set; } = new();
}

/// <summary>
/// Export size and duration estimate.
/// </summary>
public sealed class ExportEstimate
{
    public long TotalRecords { get; set; }
    public long AvailableRecords { get; set; }
    public double CompletenessPercent { get; set; }
    public long EstimatedSizeBytes { get; set; }
    public int EstimatedDurationSeconds { get; set; }
    public List<string> Warnings { get; set; } = new();

    public string EstimatedSizeFormatted => FormatBytes(EstimatedSizeBytes);
    public string EstimatedDurationFormatted => FormatDuration(EstimatedDurationSeconds);

    private static string FormatBytes(long bytes) => FormatHelpers.FormatBytes(bytes);

    private static string FormatDuration(int seconds)
    {
        if (seconds < 60)
            return $"{seconds}s";
        if (seconds < 3600)
            return $"{seconds / 60}m {seconds % 60}s";
        return $"{seconds / 3600}h {seconds % 3600 / 60}m";
    }
}

/// <summary>
/// Pre-export data quality report.
/// </summary>
public sealed class PreExportQualityReport
{
    public DateTime GeneratedAt { get; set; }
    public string[] Symbols { get; set; } = Array.Empty<string>();
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public double OverallCompleteness { get; set; }
    public int TotalTradingDays { get; set; }
    public int DaysWithData { get; set; }
    public int GapCount { get; set; }
    public List<SymbolQualityInfo> SymbolQuality { get; set; } = new();
    public string SuitabilityAssessment { get; set; } = string.Empty;
}

/// <summary>
/// Quality info for a single symbol.
/// </summary>
public sealed class SymbolQualityInfo
{
    public string Symbol { get; set; } = string.Empty;
    public double Completeness { get; set; }
    public long RecordCount { get; set; }
    public bool HasGaps { get; set; }
    public string QualityGrade { get; set; } = string.Empty;
}

/// <summary>
/// Export progress information.
/// </summary>
public sealed class ExportProgress
{
    public string CurrentSymbol { get; set; } = string.Empty;
    public int ProcessedSymbols { get; set; }
    public int TotalSymbols { get; set; }
    public double PercentComplete { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
}

/// <summary>
/// Export result.
/// </summary>
public sealed class ExportResult
{
    public bool Success { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public ExportConfiguration Configuration { get; set; } = new();
    public int ProcessedSymbols { get; set; }
    public long TotalRecordsExported { get; set; }
    public long OutputSizeBytes { get; set; }
    public List<string> GeneratedFiles { get; set; } = new();

    public TimeSpan Duration => EndTime - StartTime;
}
