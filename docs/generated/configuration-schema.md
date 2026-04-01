# Configuration Schema

> Auto-generated from `config/appsettings.sample.json`.

This reference enumerates top-level and second-level configuration keys used by Meridian pilot workflows.

## Coverage Snapshot

- Top-level keys documented: **16 / 16 (100%)**
- Second-level keys documented: **102 / 102 (100%)**
- Pilot workflow key coverage (top + second level): **86 / 86 (100%)**

## Pilot Workflow Keys (Priority)

| Key |
|-----|
| `DataRoot` |
| `DataSource` |
| `Coordination` |
| `Synthetic` |
| `Backfill` |
| `DataSources` |
| `Alpaca` |
| `StockSharp` |
| `Storage` |
| `Symbols` |
| `Canonicalization` |
| `Serilog` |
| `Coordination.Enabled` |
| `Coordination.Mode` |
| `Coordination.InstanceId` |
| `Coordination.LeaseTtlSeconds` |
| `Coordination.RenewIntervalSeconds` |
| `Coordination.TakeoverDelaySeconds` |
| `Coordination.RootPath` |
| `Synthetic.Enabled` |
| `Synthetic.Seed` |
| `Synthetic.Priority` |
| `Synthetic.EventsPerSecond` |
| `Synthetic.HistoricalTradeDensityPerDay` |
| `Synthetic.HistoricalQuoteDensityPerDay` |
| `Synthetic.DefaultDepthLevels` |
| `Synthetic.IncludeCorporateActions` |
| `Synthetic.IncludeReferenceData` |
| `Synthetic.UniverseSymbols` |
| `Synthetic.DefaultHistoryStart` |
| `Synthetic.DefaultHistoryEnd` |
| `Backfill.Enabled` |
| `Backfill.Provider` |
| `Backfill.Symbols` |
| `Backfill.From` |
| `Backfill.To` |
| `Backfill.Granularity` |
| `Backfill.EnableFallback` |
| `Backfill.PreferAdjustedPrices` |
| `Backfill.EnableSymbolResolution` |
| `Backfill.ProviderPriority` |
| `Backfill.EnableRateLimitRotation` |
| `Backfill.RateLimitRotationThreshold` |
| `Backfill.SkipExistingData` |
| `Backfill.FillGapsOnly` |
| `Backfill.Jobs` |
| `Backfill.Providers` |
| `DataSources.Sources` |
| `DataSources.DefaultRealTimeSourceId` |
| `DataSources.DefaultHistoricalSourceId` |
| `DataSources.EnableFailover` |
| `DataSources.FailoverTimeoutSeconds` |
| `DataSources.HealthCheckIntervalSeconds` |
| `DataSources.AutoRecover` |
| `DataSources.FailoverRules` |
| `DataSources.SymbolMappings` |
| `Alpaca.Feed` |
| `Alpaca.UseSandbox` |
| `Alpaca.SubscribeQuotes` |
| `StockSharp.Enabled` |
| `StockSharp.ConnectorType` |
| `StockSharp.AdapterType` |
| `StockSharp.AdapterAssembly` |
| `StockSharp.EnableRealTime` |
| `StockSharp.EnableHistorical` |
| `StockSharp.UseBinaryStorage` |
| `StockSharp.StoragePath` |
| `StockSharp.Rithmic` |
| `StockSharp.IQFeed` |
| `StockSharp.CQG` |
| `StockSharp.InteractiveBrokers` |
| `Storage.NamingConvention` |
| `Storage.DatePartition` |
| `Storage.IncludeProvider` |
| `Storage.FilePrefix` |
| `Storage.Profile` |
| `Storage.RetentionDays` |
| `Storage.MaxTotalMegabytes` |
| `Storage.Sinks` |
| `Canonicalization.Enabled` |
| `Canonicalization.Version` |
| `Canonicalization.PilotSymbols` |
| `Canonicalization.EnableDualWrite` |
| `Canonicalization.UnresolvedAlertThresholdPercent` |
| `Serilog.MinimumLevel` |
| `Serilog.WriteTo` |

## Top-Level Keys

| Key |
|-----|
| `$schema` |
| `DataRoot` |
| `Compress` |
| `DataSource` |
| `Coordination` |
| `Synthetic` |
| `Backfill` |
| `DataSources` |
| `Alpaca` |
| `StockSharp` |
| `Storage` |
| `Symbols` |
| `Derivatives` |
| `Canonicalization` |
| `Settings` |
| `Serilog` |

## Second-Level Keys

| Key |
|-----|
| `Coordination.Enabled` |
| `Coordination.Mode` |
| `Coordination.InstanceId` |
| `Coordination.LeaseTtlSeconds` |
| `Coordination.RenewIntervalSeconds` |
| `Coordination.TakeoverDelaySeconds` |
| `Coordination.RootPath` |
| `Synthetic.Enabled` |
| `Synthetic.Seed` |
| `Synthetic.Priority` |
| `Synthetic.EventsPerSecond` |
| `Synthetic.HistoricalTradeDensityPerDay` |
| `Synthetic.HistoricalQuoteDensityPerDay` |
| `Synthetic.DefaultDepthLevels` |
| `Synthetic.IncludeCorporateActions` |
| `Synthetic.IncludeReferenceData` |
| `Synthetic.UniverseSymbols` |
| `Synthetic.DefaultHistoryStart` |
| `Synthetic.DefaultHistoryEnd` |
| `Backfill.Enabled` |
| `Backfill.Provider` |
| `Backfill.Symbols` |
| `Backfill.From` |
| `Backfill.To` |
| `Backfill.Granularity` |
| `Backfill.EnableFallback` |
| `Backfill.PreferAdjustedPrices` |
| `Backfill.EnableSymbolResolution` |
| `Backfill.ProviderPriority` |
| `Backfill.EnableRateLimitRotation` |
| `Backfill.RateLimitRotationThreshold` |
| `Backfill.SkipExistingData` |
| `Backfill.FillGapsOnly` |
| `Backfill.Jobs` |
| `Backfill.Providers` |
| `DataSources.Sources` |
| `DataSources.DefaultRealTimeSourceId` |
| `DataSources.DefaultHistoricalSourceId` |
| `DataSources.EnableFailover` |
| `DataSources.FailoverTimeoutSeconds` |
| `DataSources.HealthCheckIntervalSeconds` |
| `DataSources.AutoRecover` |
| `DataSources.FailoverRules` |
| `DataSources.SymbolMappings` |
| `Alpaca.Feed` |
| `Alpaca.UseSandbox` |
| `Alpaca.SubscribeQuotes` |
| `StockSharp.Enabled` |
| `StockSharp.ConnectorType` |
| `StockSharp.AdapterType` |
| `StockSharp.AdapterAssembly` |
| `StockSharp.EnableRealTime` |
| `StockSharp.EnableHistorical` |
| `StockSharp.UseBinaryStorage` |
| `StockSharp.StoragePath` |
| `StockSharp.Rithmic` |
| `StockSharp.IQFeed` |
| `StockSharp.CQG` |
| `StockSharp.InteractiveBrokers` |
| `Storage.NamingConvention` |
| `Storage.DatePartition` |
| `Storage.IncludeProvider` |
| `Storage.FilePrefix` |
| `Storage.Profile` |
| `Storage.RetentionDays` |
| `Storage.MaxTotalMegabytes` |
| `Storage.Sinks` |
| `Derivatives.Enabled` |
| `Derivatives.Underlyings` |
| `Derivatives.MaxDaysToExpiration` |
| `Derivatives.StrikeRange` |
| `Derivatives.CaptureGreeks` |
| `Derivatives.CaptureChainSnapshots` |
| `Derivatives.ChainSnapshotIntervalSeconds` |
| `Derivatives.CaptureOpenInterest` |
| `Derivatives.ExpirationFilter` |
| `Derivatives.IndexOptions` |
| `Canonicalization.Enabled` |
| `Canonicalization.Version` |
| `Canonicalization.PilotSymbols` |
| `Canonicalization.EnableDualWrite` |
| `Canonicalization.UnresolvedAlertThresholdPercent` |
| `Settings.Theme` |
| `Settings.AccentColor` |
| `Settings.CompactMode` |
| `Settings.NotificationsEnabled` |
| `Settings.NotifyConnectionStatus` |
| `Settings.NotifyErrors` |
| `Settings.NotifyBackfillComplete` |
| `Settings.NotifyDataGaps` |
| `Settings.NotifyStorageWarnings` |
| `Settings.QuietHoursEnabled` |
| `Settings.QuietHoursStart` |
| `Settings.QuietHoursEnd` |
| `Settings.AutoReconnectEnabled` |
| `Settings.MaxReconnectAttempts` |
| `Settings.StatusRefreshIntervalSeconds` |
| `Settings.ServiceUrl` |
| `Settings.ServiceTimeoutSeconds` |
| `Settings.BackfillTimeoutMinutes` |
| `Serilog.MinimumLevel` |
| `Serilog.WriteTo` |

---

*This file is auto-generated from `config/appsettings.sample.json`. Do not edit manually; regenerate when sample config changes.*
