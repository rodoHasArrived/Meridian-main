# UWP Desktop App Development Roadmap

**Last Updated:** 2026-02-02
**Version:** 1.0.0

This document outlines feature refinements and development roadmap for the Meridian UWP Desktop Application.

## Recent Updates (February 2026)

### Critical Build Fix (February 2, 2026)

#### XAML Compiler Compatibility Issue Resolved
Fixed long-standing build failure in GitHub Actions Desktop App Build workflow:

- **Problem**: All 81+ desktop app workflow runs were failing with `XamlCompiler.exe` exit code 1
- **Root Cause**: WinUI 3 XAML compiler (`XamlCompiler.exe`) is a .NET Framework 4.7.2 (net472) executable that cannot process C# 11 features used extensively in `Meridian.Contracts` (e.g., `MarketEvent`, `CredentialFieldInfo`, `FieldError`, etc.)
- **Solution**: Added `<XamlCompilerPlatform>Managed</XamlCompilerPlatform>` to project file to use managed (.NET) XAML compiler instead
  - Note: Previous attempts with `<UseXamlCompilerExecutable>false</UseXamlCompilerExecutable>` were ineffective
  - For Windows App SDK 1.6+, `XamlCompilerPlatform=Managed` is the correct property
- **Impact**: Enables successful builds for x64, arm64, and MSIX packages
- **Reference**: [Microsoft WinUI Issue #5315](https://github.com/microsoft/microsoft-ui-xaml/issues/5315)
- **Workflow Enhancement**: Added binary logging (`-bl`) to build commands for improved diagnostics

This fix unblocks desktop app deployment and CI/CD automation.

## Recent Updates (January 2026)

### Code Quality Improvements (January 29, 2026)

#### Sealed Class Convention Enforcement
Applied CLAUDE.md guidelines requiring all classes to be `sealed` unless designed for inheritance:

- **Exception Classes**: Sealed `StorageException`, `ConnectionException`, `ConfigurationException`, `IBPacingViolationException`, `IBMarketDataNotSubscribedException`, `IBSecurityNotFoundException`
  - Note: Base exception classes (`MeridianException`, `DataProviderException`, `IBApiException`) remain unsealed as they have subclasses
- **Validator Classes**: Sealed all FluentValidation validators in `ConfigValidationHelper.cs` (`AppConfigValidator`, `AlpacaOptionsValidator`, `StorageConfigValidator`, `SymbolConfigValidator`)
- **Lean Integration Classes**: Sealed `MeridianDataProvider`, `MeridianTradeData`, `MeridianQuoteData`
- **Storage/Export Classes** (18 classes): `AnalysisQualityReportGenerator`, `AnalysisQualityReport`, `FileQualityAnalysis`, `DescriptiveStats`, `TimeStats`, `DataOutlier`, `DataGap`, `QualityIssue`, `AnalysisRecommendation`, `ExportProfile`, `CompressionSettings`, `TimestampSettings`, `ExportRequest`, `AggregationSettings`, `FeatureSettings`, `ExportResult`, `ExportDateRange`, `ExportedFile`, `ExportQualitySummary`, `AnalysisExportService`
- **Storage/Archival Classes** (16 classes): `SchemaVersionManager`, `SchemaDefinition`, `SchemaField`, `FieldConstraints`, `SchemaMigration`, `MigrationResult`, `SchemaValidationResult`, `SchemaRegistry`, `SchemaRegistryEntry`, `CompressionProfileManager`, `CompressionProfile`, `CompressionContext`, `CompressionResult`, `CompressionBenchmarkResult`, `WalRecord`, `WalOptions`, `ArchivalStorageOptions`, `ArchivalStorageStats`
- **Infrastructure Classes**: `WebSocketHeartbeat`, `DataSourceAttribute`
- **OpenAPI DTOs** (14 classes): `OpenApiSpec`, `OpenApiInfo`, `OpenApiContact`, `OpenApiLicense`, `OpenApiServer`, `OpenApiTag`, `OpenApiPathItem`, `OpenApiOperation`, `OpenApiParameter`, `OpenApiRequestBody`, `OpenApiMediaType`, `OpenApiResponse`, `OpenApiComponents`, `OpenApiSchema`

#### Anti-Pattern Fixes
- **Empty Catch Block**: Fixed bare `catch { }` in `StatusHttpServer.cs` to properly catch `OperationCanceledException` during graceful shutdown

#### Duplicate Code Removal (~786 lines removed)
- **Consolidated Symbol Search Providers**: Deleted duplicate `AlpacaSymbolSearchProvider.cs` and `FinnhubSymbolSearchProvider.cs` in favor of refactored versions using `BaseSymbolSearchProvider` abstraction
- **Updated References**: Modified `SymbolSearchService.cs` and `UiServer.cs` to use `AlpacaSymbolSearchProviderRefactored` and `FinnhubSymbolSearchProviderRefactored`
- **Cleanup**: Removed `Program.Refactored.cs.bak` backup file

### Newly Completed Features (January 14, 2026)

#### Full Application Integration
- **Admin & Maintenance Page**: Comprehensive administrative interface
  - Quick system check with health status indicators
  - Maintenance scheduling with cron expressions (daily, weekly, monthly)
  - Configurable maintenance operations (compression, cleanup, integrity, tier migration)
  - Storage tier usage visualization (Hot/Warm/Cold)
  - Retention policy management per tier
  - Maintenance history with status tracking
  - Manual maintenance trigger with progress feedback

- **Advanced Analytics Page**: Deep data quality analysis
  - Quality summary cards (Overall Score, Completeness, Gaps, Anomalies)
  - Letter grade system (A+, A, B, C, D, F) with color-coded badges
  - Per-symbol quality reports with drill-down capability
  - Gap analysis with date range selection and symbol filtering
  - One-click gap repair functionality
  - Cross-provider comparison tools
  - Latency histogram visualization with percentiles
  - Provider rate limit monitoring and alerts
  - AI-powered recommendations for data quality improvement

#### New Backend Services
- **SymbolManagementService**: Full symbol CRUD, status queries, monitoring, and archive information
- **AdminMaintenanceService**: Archive scheduling, tier migration, retention policies, cleanup operations
- **AdvancedAnalyticsService**: Gap analysis, cross-provider comparison, latency metrics, quality reports
- **ProviderManagementService**: Failover configuration, rate limit tracking, capability queries, provider switching
- **DiagnosticsService (Extended)**: Quick-check, show-config, error-codes reference, self-test functionality

#### UI/UX Improvements
- Right-click context menus for dropdown navigation items
- Enhanced error handling with user-friendly messages
- Improved form validation with inline feedback
- Accessibility enhancements (keyboard navigation, screen reader support)
- Performance optimizations for large datasets
- Better responsive layouts for different window sizes

### Newly Completed Features (January 11, 2026)

#### App Icons & Visual Branding
- **Professional App Icons**: Generated 39 UWP app icons from custom SVG source
  - Square logos (44x44, 150x150) with scale variants (100%, 125%, 150%, 200%, 400%)
  - Target-size variants (16x16, 24x24, 32x32, 48x48, 256x256) for taskbar/system tray
  - Splash screens (620x300 to 1240x600 with scale variants)
  - Wide and large tiles for Windows Start menu
  - StoreLogo variants for Windows Store distribution
- **Design**: Blue gradient background with green chart line and "Meridian" watermark

#### Desktop App CI/CD Pipeline
- **GitHub Actions Workflow**: Full build pipeline for Windows desktop application
  - Asset generation job (Node.js-based icon generation from SVG)
  - Multi-platform build (x64 and ARM64 architectures)
  - Self-contained publishing with .NET 9.0 runtime
  - Performance optimization (PublishReadyToRun enabled)
  - MSIX packaging for Windows Store (triggered on tags/manual dispatch)
  - Integration testing with smoke tests
  - Release automation with artifact uploads to GitHub Releases
- **New Makefile Targets**: `make icons`, `make desktop`, `make desktop-publish`

#### Background Task Scheduler Service
- **Scheduled Task Execution**: Full background task system supporting:
  - Export tasks with cron-like scheduling
  - Sync-to-remote tasks (S3/Azure/network storage)
  - Custom tasks (webhooks, scripts, commands, notifications)
  - Verification tasks with scheduling
- **Features**: Concurrent job management, job persistence, recovery across app restarts

#### Enhanced ViewModels (MVVM Architecture)
- **DashboardViewModel**: Real-time service updates, connection state tracking
- **BackfillViewModel**: Progress tracking, cancellation support, scheduler integration
- **DataExportViewModel**: Export service integration, format selection, destination management

### Previously Completed Features (January 2026)
- **Symbols Page**: Bulk symbol management (CSV import/export), symbol search with autocomplete, subscription templates, watchlist functionality
- **Backfill Page**: Scheduled backfill with cron-like interface, per-symbol progress visualization, data validation and repair
- **Provider Page**: Multi-provider support with Primary/Failover/Comparison roles, credential testing with visual feedback, latency history charts
- **Data Sources Page**: Comprehensive multi-data source management with failover settings, default source configuration, and provider-specific settings
- **Trading Hours Page**: Full exchange calendar with holidays, pre/post market sessions, timezone conversion, session overlap visualization, DST handling
- **Data Export Page**: Complete export suite with scheduled exports, email delivery, database export (6 databases), webhook integration, QuantConnect Lean export
- **Plugins Page**: Full data source plugin system with hot reload, directory watching, plugin configuration and management

> **Primary Mission: Data Collection & Archival**
>
> The Meridian is designed as a **collection and archival system first**. Analysis of collected data is performed externally using specialized tools (Python, R, QuantConnect Lean, databases, etc.). This mission guides our feature priorities:
>
> - **Reliable Collection**: Gap-free, fault-tolerant data capture
> - **Robust Archival**: Long-term storage with integrity verification
> - **Export Excellence**: Easy extraction in formats for external analysis
> - **Future Flexibility**: Architecture supports cloud/hybrid when needed

---

## Table of Contents

1. [Recent Updates (January 2026)](#recent-updates-january-2026)
2. [Desktop-Ready Release Checklist](#desktop-ready-release-checklist)
3. [Feature Refinements](#feature-refinements)
4. [New Feature Ideas](#new-feature-ideas)
5. [Offline Storage & Archival Ideas (2026)](#offline-storage--archival-ideas-2026)
6. [External Analysis Support (2026)](#external-analysis-support-2026)
7. [Additional New Feature Ideas (2026)](#additional-new-feature-ideas-2026)
8. [New Feature Ideas (January 2026)](#new-feature-ideas-january-2026)
9. [Infrastructure & DevOps Features (2026)](#infrastructure--devops-features-2026)
10. [Priority Matrix](#priority-matrix)
11. [Implementation Notes](#implementation-notes)

---

## Desktop-Ready Release Checklist

Release expectations are captured in the desktop-ready checklist, including must-ship vs. post-ship scope and acceptance criteria. Review it before cutting a release. See [docs/guides/uwp-release-checklist.md](uwp-release-checklist.md).

## Feature Refinements

### Dashboard Page Enhancements

#### 1. Real-Time Data Visualization ✅ COMPLETE
**Current State:** Dashboard shows static counters for Published, Dropped, Integrity, and Historical Bars.

**Implemented:**
- ✅ Real-time event throughput graph with time range selector (1, 5, 15 min, 1 hour)
- ✅ Sparkline mini-charts on each metric card showing trends
- ✅ Data Health gauge displaying current health percentage
- ✅ Rolling statistics display (Current, Average, Peak throughput)
- ✅ Animated Canvas-based visualizations with polyline charts

#### 2. Quick Actions Panel ✅ COMPLETE
**Current State:** Limited to refresh and provider display.

**Implemented:**
- ✅ Start/Stop Collector buttons with visual feedback
- ✅ Status badges for active streams
- ✅ Collector uptime timer display
- ✅ Connection status indicator with latency

**Remaining:**
- Quick Add Symbol inline input (planned for future iteration)

#### 3. Symbol Performance Summary ✅ COMPLETE
**Current State:** Shows basic symbol list with Trades/Depth flags.

**Implemented:**
- ✅ Per-symbol event rates (events/sec) column
- ✅ Color-coded status indicators by activity level
- ✅ Last event timestamp per symbol
- ✅ Sortable columns in performance table
- ✅ Health score with trend sparklines per symbol

---

### Provider Page Enhancements

#### 1. Connection Health Monitoring ✅ COMPLETE
**Current State:** Basic connection status indicator.

**Implemented:**
- ✅ Latency display with real-time measurement
- ✅ Reconnection attempt counter and history tracking
- ✅ Auto-reconnection with configurable retry attempts
- ✅ Connection state tracking (Connected/Disconnected/Reconnecting)
- ✅ Uptime calculation and total reconnection counter
- ✅ Health check timer with periodic verification

#### 2. Multi-Provider Support ✅ COMPLETE
**Current State:** Full multi-provider support implemented.

**Implemented:**
- ✅ Single/Multi provider toggle in Provider Page
- ✅ Simultaneous multi-provider connections
- ✅ Provider roles configuration (Primary, Failover, Comparison)
- ✅ Automatic failover configuration between providers
- ✅ Per-provider enable/disable toggles
- ✅ DataSourcesPage for comprehensive multi-source management
- ✅ Failover timeout configuration (5-300 seconds)
- ✅ Default real-time and historical source selection

**Remaining:**
- Provider comparison view showing data quality metrics side-by-side

**Recently Implemented:**
- ✅ Provider-specific symbol mapping interface (SymbolMappingPage) - 2026-01-11

#### 3. Credential Testing ✅ COMPLETE
**Current State:** Full credential testing implemented.

**Implemented:**
- ✅ "Test Credentials" button with visual feedback
- ✅ Credential status display (configured/not configured)
- ✅ Test connection button for IB
- ✅ Clear credentials functionality
- ✅ ProgressRing during credential testing
- ✅ InfoBar for test results display

**Remaining:**
- Credential expiration warnings (for time-limited tokens)
- Credential auto-refresh for OAuth-based providers

---

### Storage Page Enhancements

#### 1. Storage Analytics Dashboard ✅ COMPLETE
**Current State:** Configuration-only view.

**Implemented:**
- ✅ Disk usage visualization with breakdown by symbol, type, and date
- ✅ Storage growth rate estimation and days-until-full projection
- ✅ File count and per-type storage statistics
- ✅ Top symbols by storage usage list
- ✅ Colored progress bar showing storage breakdown by type

#### 2. Data Lifecycle Management ✅ COMPLETE
**Current State:** Basic retention configuration.

**Implemented:**
- ✅ Three-tier storage configuration (Hot/Warm/Cold)
- ✅ Hot Tier: Last 7 days, uncompressed JSONL, fast SSD
- ✅ Warm Tier: 7-90 days, compressed (gzip), local storage
- ✅ Cold Tier: 90+ days, archive to cloud or Parquet format
- ✅ Configurable retention periods per tier

#### 3. Cloud Storage Integration ✅ COMPLETE
**Current State:** Local storage only.

**Implemented:**
- ✅ Azure Blob Storage integration (connection string, container, storage tier)
- ✅ AWS S3 integration (access key, secret key, bucket, region)
- ✅ Google Cloud Storage integration (service account JSON, bucket)
- ✅ Sync modes: Manual, Scheduled, Real-time
- ✅ Connection status and sync statistics display

---

### Symbols Page Enhancements

#### 1. Bulk Symbol Management ✅ COMPLETE
**Current State:** Single symbol add/edit workflow.

**Implemented:**
- ✅ CSV/text file import for bulk symbol additions
- ✅ CSV export functionality
- ✅ Symbol group/watchlist functionality with save/load/manage
- ✅ Batch operations (delete multiple, enable/disable trades/depth)
- ✅ Select all checkbox with bulk action buttons

**Remaining:**
- "Import from Portfolio" integration (broker positions) - future consideration

#### 2. Symbol Discovery & Search ✅ COMPLETE
**Current State:** Manual symbol entry.

**Implemented:**
- ✅ Symbol search with AutoSuggestBox autocomplete
- ✅ OpenFIGI symbol lookup integration
- ✅ Exchange filter dropdown (All, SMART, NYSE, NASDAQ, ARCA)
- ✅ Subscription type filter (All, Trades Only, Depth Only, Both)
- ✅ Popular symbol templates as quick picks

**Remaining:**
- Symbol details popup (company name, exchange, type, average volume) - future enhancement

#### 3. Subscription Templates ✅ COMPLETE
**Current State:** Individual symbol configuration.

**Implemented:**
- ✅ Pre-built templates: FAANG, Magnificent 7, Major ETFs, Semiconductors, Financials
- ✅ One-click template addition with symbol counts
- ✅ Visual template cards with symbol previews

**Remaining:**
- Custom subscription presets (e.g., "Scalping" = Trades+L2@20 levels)
- Symbol-type defaults (Stocks, ETFs, Futures)
- Exchange-specific default configurations

---

### Backfill Page Enhancements

#### 1. Scheduled Backfill Jobs ✅ COMPLETE
**Current State:** Manual, on-demand backfill.

**Implemented:**
- ✅ Scheduled backfill with cron-like interface (Daily/Weekly/Monthly)
- ✅ Configurable run time with TimePicker
- ✅ Option to include all subscribed symbols or specify custom list
- ✅ Notification on completion toggle
- ✅ Upcoming scheduled jobs list with Run Now/Edit buttons
- ✅ Recent backfill history with status indicators
- ✅ Quick stats panel (total bars, symbols with data, date coverage)

#### 2. Backfill Progress Visualization ✅ COMPLETE
**Current State:** Basic progress bar.

**Implemented:**
- ✅ Per-symbol progress breakdown with individual progress bars
- ✅ Overall progress bar with symbol count and elapsed time
- ✅ Status indicators per symbol (color-coded badges)
- ✅ Bars downloaded count per symbol
- ✅ Pause/resume capability for long-running jobs
- ✅ Cancel button for aborting backfill

**Remaining:**
- Download speed and ETA estimation - future enhancement

#### 3. Data Validation & Repair ✅ COMPLETE
**Current State:** No post-backfill validation.

**Implemented:**
- ✅ Validate Data button with data integrity check
- ✅ Validation results card showing symbols checked, gaps found, missing days, data health %
- ✅ Gap detection with individual repair buttons per gap
- ✅ Repair Gaps button for bulk gap filling
- ✅ Validation issues list with symbol, description, date range, and repair action

**Remaining:**
- Data coverage calendar visualization - future enhancement

---

### Settings Page Enhancements

#### 1. Notification System ✅ COMPLETE
**Current State:** No notifications.

**Implemented:**
- ✅ Windows toast notifications using Microsoft.Windows.AppNotifications API
- ✅ Notification types: Info, Success, Warning, Error
- ✅ Alert categories: Connection status, Errors, Backfill completion, Data gaps, Storage warnings
- ✅ Customizable notification sounds (Default, Subtle, None)
- ✅ Quiet hours configuration with start/end times
- ✅ Notification history (last 50 notifications)
- ✅ Interactive notification buttons with action URLs

#### 2. Export & Backup ✅ COMPLETE
**Current State:** No configuration export.

**Implemented:**
- ✅ Configuration export/import functionality
- ✅ Selective export (symbols, storage settings, provider config, scheduled jobs, UI preferences)
- ✅ Reset to Defaults with confirmation dialog
- ✅ Visual configuration backup management in Settings UI

---

## New Feature Ideas

### 1. Live Order Book Visualization Page

**Description:** A dedicated page showing real-time Level 2 order book visualization for selected symbols.

**Features:**
- Heatmap-style depth chart with bid/ask ladders
- Time & Sales (tape) feed with trade direction highlighting
- Spread and imbalance indicators
- Volume profile sidebar
- Order flow delta (aggressive buy vs. sell)
- Customizable update speed (throttle for performance)
- Multi-symbol grid view (2x2, 3x3 layouts)

**Technical Considerations:**
- Use WriteableBitmap for high-performance rendering
- Implement data throttling to prevent UI overload
- Consider WebGL/Win2D for advanced visualizations

---

### 2. Data Quality Analytics Page

**Description:** Comprehensive data quality monitoring and reporting interface.

**Features:**
- Data completeness score per symbol/date
- Gap analysis with visual timeline
- Sequence error tracking and reporting
- Cross-provider data comparison
- Latency distribution histograms
- Anomaly detection alerts (price spikes, volume outliers)
- Daily/weekly quality reports with export

---

### 3. Replay & Simulation Mode

**Description:** Historical data replay functionality for strategy testing and review.

**Features:**
- Load historical JSONL files and replay at configurable speeds
- Playback controls (play, pause, speed 1x-100x, seek)
- Event-by-event stepping mode
- Time range selection with calendar
- Export replay to video format
- Bookmark interesting moments
- Side-by-side comparison of different dates

---

### 4. Alert & Monitoring Center

**Description:** Centralized alerting system for data collection health and market events.

**Features:**
- Configurable alert rules (drop rate > X%, connection lost, symbol stale)
- Alert history with acknowledgment workflow
- Email/SMS/Webhook notification integration
- Alert snooze and escalation policies
- Dashboard widget for active alerts
- Integration with external monitoring (PagerDuty, Slack)

---

### 5. Symbol Performance Analytics Page

**Description:** Per-symbol analytics showing data collection statistics and quality metrics.

**Features:**
- Symbol comparison table with sortable columns
- Historical data availability chart per symbol
- Average spreads and volume statistics
- Data collection cost analysis (for paid feeds)
- Symbol health score combining multiple metrics
- Top/bottom performers lists

---

### 6. Collector Service Manager ✅ COMPLETE

**Description:** UI for managing the collector background service/process.

**Implemented:**
- ✅ Start/Stop/Restart collector service controls
- ✅ Service status with detailed health info (process ID, uptime, command line)
- ✅ Real-time log viewer with filtering (Info, Warning, Error, Debug)
- ✅ Resource usage monitoring (CPU %, Memory, Network in/out)
- ✅ Auto-start with Windows toggle
- ✅ Auto-restart on crash with configurable delay (1-300 seconds)
- ✅ Scheduled collection (24/7 or market hours only)
- ✅ Crash recovery history tracking with timestamps
- ✅ Log search, auto-scroll, clear, and export functions

---

### 7. Data Export & Integration Page ✅ COMPLETE

**Description:** Tools for exporting data and integrating with external systems.

**Implemented:**
- ✅ DataExportPage.xaml with comprehensive export UI
- ✅ Multiple export format support (CSV, Parquet, JSON Lines, HDF5, Feather)
- ✅ Multiple compression options (None, Gzip, LZ4, Zstd)
- ✅ Date range and symbol selection with quick date buttons (Today, Week, Month)
- ✅ Data type selection (Trades, Order Book Depth, BBO Quotes, Historical Bars)
- ✅ Export progress visualization with per-symbol breakdown
- ✅ Scheduled exports with frequency options (Hourly, Daily, Weekly, Monthly)
- ✅ Email delivery configuration with recipients, subject template, and attachment option
- ✅ Direct database export with support for:
  - PostgreSQL, TimescaleDB, ClickHouse, QuestDB, InfluxDB, SQLite
  - Connection configuration (Host, Port, Database, Credentials)
  - Test Connection functionality
- ✅ Webhook integration for real-time event streaming
  - Configurable webhook URL
  - Payload format options (JSON, MessagePack, Protobuf)
  - Batching options (Real-time, Micro-batch, Batch, Bulk)
  - Event type filtering (Trades, Quotes, Depth Updates, Integrity Alerts)
  - Test Webhook functionality
- ✅ QuantConnect Lean data format export
  - Lean data folder configuration with Browse button
  - Resolution selection (Tick, Second, Minute, Hour, Daily)
  - Verify Lean Data functionality
- ✅ Export history tracking with timestamps, format, symbol count, size, destination

**Remaining for Future:**
- REST API configuration for external consumers

---

### 8. Trading Hours Manager ✅ COMPLETE

**Description:** Configure and visualize trading sessions for different exchanges.

**Implemented:**
- ✅ TradingHoursPage.xaml with comprehensive trading hours configuration UI
- ✅ Market hours scheduling integration with collector service
- ✅ 24/7 vs market hours only collection mode
- ✅ Exchange calendar with holidays (NYSE, NASDAQ, CME, LSE, TSE, Custom)
- ✅ Holiday management with Add/Import functionality
- ✅ Pre/post market session configuration with individual enable toggles
- ✅ TimePickers for Pre-Market, Regular Hours, and Post-Market sessions
- ✅ Timezone conversion tools (America/New_York, America/Chicago, Europe/London, Asia/Tokyo, Asia/Hong_Kong, UTC)
- ✅ Session overlap visualization with global market hours timeline (UTC)
- ✅ Visual representation of Tokyo, London, and New York sessions
- ✅ Current time indicator on timeline
- ✅ Automatic DST adjustments with toggle
- ✅ DST transition warnings with upcoming change notifications
- ✅ Trading days configuration (Mon-Sun checkboxes)
- ✅ Real-time local time, UTC time, and exchange status display
- ✅ Next market event countdown (e.g., "Closes in 1h 30m")

---

### 9. Dashboard Customization & Widgets 🔄 PARTIAL

**Description:** Customizable dashboard with drag-and-drop widget arrangement.

**Implemented:**
- ✅ Dark/Light/System theme switching via ThemeService
- ✅ Accent color customization
- ✅ Compact mode toggle
- ✅ Status cards with embedded widgets (sparklines, charts)

**Remaining for Future:**
- Drag-and-drop widget arrangement
- Widget library expansion
- Multiple dashboard layouts/profiles
- Widget resize capability
- Custom refresh intervals per widget
- Dashboard sharing/export

---

### 10. Data Source Plugin System ✅ COMPLETE (NEW)

**Description:** Extensible plugin architecture for adding custom data source providers.

**Implemented:**
- ✅ PluginsPage.xaml with comprehensive plugin management UI
- ✅ Plugin system status dashboard (Total, Active, Error counts)
- ✅ Plugin directory configuration with watch capability
- ✅ Hot reload support for plugins
- ✅ Auto-load new plugins toggle
- ✅ Browse and install plugin files (.dll)
- ✅ Open plugins folder functionality
- ✅ Installed plugins list with:
  - Enable/disable toggle per plugin
  - Plugin name, version, status, data source type
  - Plugin description and author
  - Priority and permissions display
  - Reload, Configure, and Unload actions
- ✅ Plugin details panel with:
  - Plugin ID, Version, Data Source Type, Category
  - Assembly path display
  - Required permissions listing
- ✅ Keyboard accelerators (Ctrl+R for refresh, etc.)

---

### 11. Mobile Companion App Integration

**Description:** Companion mobile app support for remote monitoring.

**Features:**
- Push notifications to mobile app
- Remote status viewing
- Quick actions (start/stop collector)
- QR code pairing for easy setup
- Secure remote access through Azure/cloud relay

---

### 12. Machine Learning Data Preparation

**Description:** Tools for preparing collected data for ML model training.

**Features:**
- Feature engineering presets (OHLC, technical indicators)
- Label generation (future returns, direction)
- Train/validation/test split configuration
- Data normalization options
- Missing data imputation strategies
- Export to ML frameworks (PyTorch, TensorFlow formats)
- Integration with Jupyter notebooks

---

### 13. Multi-Asset Class Support

**Description:** Expand beyond equities to support additional asset classes.

**Features:**
- Cryptocurrency data collection (Coinbase, Binance)
- Forex pairs with proper handling
- Futures with contract rollover
- Options chain data
- Asset class-specific visualizations
- Cross-asset correlation tools

---

### 14. Compliance & Audit Features

**Description:** Features for regulatory compliance and data governance.

**Features:**
- Data access audit logging
- User authentication for multi-user setups
- Data retention policy enforcement
- PII detection and redaction
- Compliance report generation
- Data lineage tracking

---

### 15. Performance Benchmarking Page

**Description:** Compare system performance against benchmarks and historical baselines.

**Features:**
- Event processing latency percentiles
- Throughput benchmarks vs. hardware specs
- Historical performance trend analysis
- Resource efficiency scoring
- Optimization recommendations
- A/B testing for configuration changes

---

### 16. Help & Onboarding Improvements 🔄 PARTIAL

**Description:** Enhanced user guidance and documentation within the app.

**Implemented:**
- ✅ HelpPage.xaml with documentation links
- ✅ Keyboard shortcuts reference in Settings (20+ shortcuts)
- ✅ Shortcut categories: Navigation, Collector, Backfill, Symbols, View, General
- ✅ Help links: Documentation, Issue Reporting, Update Check

**Remaining for Future:**
- Interactive tutorial/walkthrough for new users
- Contextual help tooltips throughout UI
- Video tutorials embedded in help sections
- Feature discovery prompts
- Searchable in-app documentation

---

## Offline Storage & Archival Ideas (2026)

> **Focus Area**: These features enhance the core archival mission, ensuring data is reliably collected, securely stored, and easily accessible for export.

### 26. Archive Health Dashboard

**Description:** Dedicated page for monitoring archive integrity and health metrics.

**Features:**
- Overall archive health score with trend visualization
- Checksum verification status for all files (verified/pending/failed)
- Storage media health indicators (SMART status for local drives)
- Scheduled verification job status and history
- One-click full archive verification with progress tracking
- Repair recommendations with automated fix options
- Archive growth projections and capacity planning
- Integrity report generation for compliance documentation

**UI Components:**
- Health score gauge (0-100%)
- File status breakdown chart
- Verification history timeline
- Active issues list with severity indicators
- Quick action buttons for verify/repair/export

---

### 27. Collection Session Manager

**Description:** Organize data collection into discrete, manageable sessions with comprehensive tracking.

**Features:**
- Define collection sessions (daily, weekly, custom date ranges)
- Session start/stop controls with automatic boundary detection
- Per-session statistics: events, bytes, symbols, quality score
- Session tagging and categorization (e.g., "Earnings Week", "Volatility Event")
- Session comparison view for A/B analysis
- Session-based export with one-click packaging
- Session notes and annotations for documentation
- Automatic session reports upon completion

**Session Metadata:**
```
Session: Q1-2026-Week1
Started: 2026-01-06 09:30:00 ET
Ended: 2026-01-10 16:00:00 ET
Symbols: 50 | Events: 62.5M | Size: 8.2 GB
Quality: 99.7% | Gaps: 3 (all filled)
```

---

### 28. Portable Data Packager

**Description:** Create self-contained, portable archive packages for data transfer and backup.

**Features:**
- Package creation wizard with symbol/date selection
- Multiple output formats: ZIP, TAR.GZ, 7Z
- Include manifest, schemas, and documentation automatically
- Selective packaging by event type, quality threshold, or custom filter
- Package verification tool with checksum validation
- Split large packages for media limitations
- Package encryption for secure transport
- Embedded package browser/viewer for self-describing archives

**Package Structure:**
```
MarketData_2026-Q1.tar.gz
├── manifest.json
├── README.md
├── schemas/
├── data/
└── verification/
    └── checksums.sha256
```

---

### 29. Data Completeness Calendar

**Description:** Visual calendar view showing data coverage and gaps across time and symbols.

**Features:**
- Calendar heatmap with color-coded data completeness
- Drill-down from year → month → day → symbol
- Gap highlighting with estimated missing data counts
- Trading calendar integration (holidays, half-days marked)
- Expected vs. actual data volume comparison
- One-click backfill for identified gaps
- Export completeness report by date range
- Multi-symbol coverage matrix view

**Visualization:**
- Green: Complete data (>99%)
- Yellow: Minor gaps (95-99%)
- Orange: Significant gaps (80-95%)
- Red: Major issues (<80%)
- Gray: Non-trading day

---

### 30. Archive Browser & Inspector

**Description:** In-app file browser for navigating and inspecting archived data.

**Features:**
- Hierarchical tree view: Year → Month → Day → Symbol → Type
- File metadata panel (size, events, checksums, timestamps)
- Quick preview of file contents (first/last N events)
- Search within archive by date range, symbol, or event type
- File comparison tool for duplicate detection
- Right-click context menu: export, verify, repair, delete
- Bulk operations on selected files
- Integration with export presets

---

### 31. Storage Optimization Advisor

**Description:** AI-powered recommendations for optimizing archive storage efficiency.

**Features:**
- Analyze current storage utilization patterns
- Recommend compression strategy changes
- Identify redundant or duplicate data
- Suggest tiering adjustments based on access patterns
- Calculate potential savings for each recommendation
- One-click implementation of approved optimizations
- Before/after comparison reports
- Scheduled optimization runs during maintenance windows

**Example Recommendations:**
```
1. Compress 150 warm-tier files → Save 25 GB
2. Merge 500 small files → Improve access speed
3. Move 2023 data to cold tier → Free 100 GB SSD
4. Remove 5 duplicate files → Save 0.8 GB
```

---

### 32. Write-Ahead Logging Dashboard

**Description:** Monitor crash-safe write operations and data persistence guarantees.

**Features:**
- WAL status indicator (enabled, buffer size, sync interval)
- Pending writes queue visualization
- Sync operation history with timing metrics
- Recovery status after unexpected shutdowns
- Write performance metrics (latency, throughput)
- Configuration tuning recommendations
- Crash simulation and recovery testing tools

---

### 33. Multi-Drive Archive Management

**Description:** Manage archives across multiple storage devices and locations.

**Features:**
- Configure multiple archive locations (local drives, NAS, external)
- Define storage policies per location (hot/warm/cold assignments)
- Automatic data distribution based on policies
- Cross-location data migration tools
- Aggregate storage metrics across all locations
- Drive health monitoring with failure alerts
- Redundancy configuration (mirroring, distribution)
- Offline drive handling and resync capabilities

---

### 34. Archive Versioning System

**Description:** Track changes to archived data with versioning and rollback capabilities.

**Features:**
- Version history for all archive modifications
- Change tracking (additions, deletions, repairs)
- Point-in-time archive snapshots
- Rollback to previous archive state
- Version comparison and diff tools
- Automated snapshot scheduling
- Version retention policies
- Audit trail for compliance

---

## External Analysis Support (2026)

> **Focus Area**: These features prepare data for consumption by external analysis tools, making the transition from collection to analysis seamless.

### 35. Analysis Export Wizard

**Description:** Guided workflow for exporting data in formats optimized for external tools.

**Features:**
- Step-by-step export configuration wizard
- Tool-specific export profiles:
  - Python/Pandas (Parquet with appropriate dtypes)
  - R (CSV with proper formatting)
  - QuantConnect Lean (native format)
  - Excel (XLSX with multiple sheets)
  - PostgreSQL/TimescaleDB (SQL/COPY format)
- Preview exported data structure before generation
- Estimated export size and time
- Include auto-generated data loader code
- Save export configurations as reusable presets

---

### 36. Data Quality Pre-Export Report

**Description:** Generate analysis-focused quality reports before exporting data.

**Features:**
- Completeness assessment with gap details
- Outlier detection with statistical analysis
- Distribution statistics for key fields
- Time series stationarity indicators
- Warnings about data issues affecting analysis
- Recommendations for handling detected issues
- Machine-readable quality metadata (JSON)
- Quality score breakdown by dimension

**Report Sections:**
- Data completeness (trading days, expected vs. actual events)
- Distribution statistics (price range, volume, trade count)
- Potential issues (outliers, spikes, gaps)
- Analysis suitability assessment

---

### 37. Schema & Data Dictionary Generator

**Description:** Auto-generate comprehensive documentation for exported data.

**Features:**
- Generate data dictionaries for all event types
- Include field descriptions, types, valid ranges
- Document exchange-specific codes and conditions
- Export in multiple formats (Markdown, JSON Schema, Avro, Protobuf)
- Version tracking for schema changes
- Include sample records for each event type
- Generate ER diagrams for relational exports
- API documentation for programmatic access

---

### 38. Time Series Aggregation Tools

**Description:** Pre-aggregate tick data into bars and intervals for analysis.

**Features:**
- Generate OHLCV bars at configurable intervals (1s, 1m, 5m, 1h, 1d)
- Multiple aggregation methods (last, mean, VWAP, TWAP)
- Gap handling strategies (forward fill, null, interpolate)
- Market hours filtering with timezone support
- Pre-market/regular/after-hours session separation
- Volume profile generation
- Export aggregated data alongside raw ticks
- Alignment across multiple symbols

---

### 39. Feature Engineering Export

**Description:** Generate derived features during export for ML/research workflows.

**Features:**
- Pre-computed feature sets:
  - Returns (log, simple, multi-horizon)
  - Rolling statistics (mean, std, min, max, percentiles)
  - Technical indicators (SMA, EMA, RSI, MACD, Bollinger)
  - Microstructure features (spread, imbalance, velocity)
- Custom feature definitions via configuration
- Feature normalization and scaling options
- Train/validation/test split configuration
- Feature documentation with formulas
- Export to ML framework formats (PyTorch, TensorFlow)

---

### 40. Batch Export Scheduler

**Description:** Automate recurring export jobs for regular analysis workflows.

**Features:**
- Schedule exports (daily, weekly, monthly, custom cron)
- Incremental export (only new/changed data)
- Multi-destination export (local, network, cloud)
- Export job queuing with priority
- Parallel export processing for large datasets
- Export completion notifications
- Job history with status and statistics
- Automatic retry on failure

---

### 41. External Tool Workspace Generator

**Description:** Generate ready-to-use analysis environments for external tools.

**Features:**
- Python workspace: requirements.txt, loader module, Jupyter notebooks
- R workspace: R project with data import scripts
- QuantConnect Lean: project structure with data links
- Sample analysis scripts for common workflows
- Environment setup documentation
- Data exploration templates
- Workspace templates for symbol groups
- One-click workspace creation

---

### 42. Dataset Fingerprinting & Versioning

**Description:** Track exported datasets for reproducibility and auditing.

**Features:**
- Unique fingerprint for each exported dataset
- Version control for export configurations
- Dataset comparison across versions
- Reproducibility verification tools
- Export history with full metadata
- Chain-of-custody documentation
- Integration with Git for version tracking
- Dataset registry with search capabilities

---

### 43. Analysis Integration Hub

**Description:** Central dashboard for managing external analysis tool connections.

**Features:**
- Registered analysis tools and environments
- Connection status for linked tools
- Data pipeline visualization (collection → storage → export → analysis)
- Tool-specific export shortcuts
- Usage analytics (which tools access which data)
- Integration health monitoring
- Documentation links for each integration
- Community-contributed integration templates

---

## Additional New Feature Ideas (2026)

### 16. Real-Time Anomaly Detection Engine

**Description:** AI-powered anomaly detection for market data quality and unusual market conditions.

**Features:**
- Statistical anomaly detection for price/volume spikes
- Machine learning model for pattern recognition
- Configurable sensitivity thresholds per symbol
- Visual anomaly highlighting on charts
- Anomaly classification (data error vs. market event)
- Historical anomaly log with drill-down analysis
- Slack/Teams/Discord webhook integration for alerts
- Anomaly correlation across related symbols

---

### 17. Market Microstructure Analytics

**Description:** Advanced analytics for understanding market microstructure from collected data.

**Features:**
- Bid-ask spread analysis over time
- Order book imbalance metrics
- Trade flow toxicity indicators (VPIN, Kyle's Lambda)
- Price impact estimation
- Market maker activity detection
- Hidden order detection algorithms
- Venue comparison analytics
- Intraday seasonality patterns

---

### 18. Data Federation & Multi-Source Reconciliation

**Description:** Combine and reconcile data from multiple providers for enhanced quality.

**Features:**
- Cross-provider timestamp alignment
- Price discrepancy detection and resolution
- Best bid/offer aggregation across venues
- Data quality scoring per source
- Automatic failover with seamless data continuity
- Provider latency comparison dashboard
- Cost-per-message analytics by provider
- Consolidated tape construction

---

### 19. Event-Driven Automation Framework

**Description:** Create automated workflows triggered by data events and conditions.

**Features:**
- Visual workflow builder (drag-and-drop)
- Trigger conditions: price alerts, volume thresholds, data gaps
- Actions: notifications, data exports, API calls, scripts
- Scheduled automation tasks
- Workflow templates library
- Execution history and logging
- Conditional branching logic
- Integration with external systems (IFTTT, Zapier)

---

### 20. Historical Data Comparison Tool

**Description:** Compare data across different time periods, symbols, or market conditions.

**Features:**
- Side-by-side chart comparison
- Overlay mode for multi-period analysis
- Event alignment (earnings, Fed meetings, etc.)
- Statistical similarity scoring
- Pattern matching across historical data
- Seasonal comparison (YoY, QoQ)
- Correlation matrix visualization
- Export comparison reports

---

### 21. Smart Symbol Recommendations

**Description:** AI-powered suggestions for symbols to add based on portfolio and market analysis.

**Features:**
- Correlated symbol suggestions
- Sector/industry coverage analysis
- Liquidity-based recommendations
- Gap analysis for portfolio hedging
- Trending symbols detection
- Similar volatility profile matching
- Options chain coverage suggestions
- ETF component symbol recommendations

---

### 22. Data Lineage & Provenance Tracking

**Description:** Full transparency into data origin, transformations, and quality chain.

**Features:**
- Complete data lineage visualization
- Transformation audit trail
- Provider attribution per data point
- Quality score inheritance tracking
- Data versioning with diff capability
- Compliance-ready provenance reports
- Chain-of-custody documentation
- Reproducibility verification

---

### 23. Embedded Scripting Environment

**Description:** Built-in scripting for custom data processing and analysis.

**Features:**
- Python scripting integration
- Live data stream access from scripts
- Custom indicator calculation engine
- Scheduled script execution
- Script template library
- Output visualization widgets
- Script sharing and versioning
- Jupyter notebook integration

---

### 24. Network Diagnostics & Optimization

**Description:** Advanced network monitoring and optimization for data collection.

**Features:**
- Real-time network latency heatmap
- Packet loss detection and alerting
- MTU optimization recommendations
- DNS resolution analytics
- TCP connection pooling stats
- Bandwidth utilization graphs
- Network route tracing
- Connection quality forecasting

---

### 25. Collaborative Workspaces

**Description:** Multi-user collaboration features for teams.

**Features:**
- Shared symbol watchlists
- Team configuration profiles
- Real-time collaboration indicators
- Comment and annotation system
- Role-based access control
- Activity feed for team actions
- Shared alert configurations
- Team performance dashboards

---

## New Feature Ideas (January 2026)

### 44. Data Completeness Heatmap Calendar

**Description:** Visual calendar showing data collection completeness across all symbols and time periods.

**Features:**
- Month/Year view with color-coded completeness indicators
- Drill-down from year → month → day → symbol
- Per-symbol coverage matrix (symbols × dates)
- Gap highlighting with one-click backfill triggers
- Expected vs. actual event count comparison
- Trading day vs. holiday distinction
- Export completeness reports for compliance

**UI Components:**
- Calendar grid with heatmap coloring (green=complete, yellow=partial, red=gaps)
- Symbol filter and date range selector
- Coverage percentage summary stats
- Quick actions for filling detected gaps

---

### 45. Real-Time Data Feed Monitor

**Description:** Live visualization of incoming data streams with health metrics.

**Features:**
- Real-time event stream waterfall/ticker display
- Per-provider throughput gauges
- Latency histogram with percentile markers
- Sequence gap detection with alerts
- Message rate comparison across providers
- Bandwidth utilization monitoring
- Connection health timeline
- Auto-pause during market close

---

### 46. Symbol Universe Browser

**Description:** Comprehensive symbol discovery and universe management tool.

**Features:**
- Search across multiple providers (OpenFIGI, SEC EDGAR, exchange listings)
- Filter by sector, industry, market cap, average volume
- Symbol cross-reference mapping (CUSIP, ISIN, FIGI)
- Corporate actions tracker (splits, dividends, name changes)
- IPO calendar integration
- Delisted symbols archive
- Custom universe creation with criteria
- Universe comparison and overlap analysis

---

### 47. Backtest Data Validator

**Description:** Validate collected data quality for backtesting use cases.

**Features:**
- Split/dividend adjustment verification
- Corporate action alignment check
- Price continuity validation (gap detection)
- Volume anomaly detection
- Timestamp alignment across symbols
- Look-ahead bias detection tools
- Survivorship bias warnings
- Data quality certification reports

---

### 48. Intelligent Data Recommendations

**Description:** AI-powered suggestions for data collection optimization.

**Features:**
- Suggested symbols based on portfolio correlation analysis
- Optimal collection schedule recommendations
- Storage optimization suggestions
- Provider cost/quality trade-off analysis
- Gap filling priority recommendations
- Collection frequency optimization
- Anomaly pattern learning and prediction
- Resource utilization recommendations

---

### 49. Remote Collection Node Manager

**Description:** Manage distributed data collection nodes for redundancy and coverage.

**Features:**
- Register and monitor remote collection nodes
- Node health and status dashboard
- Data synchronization between nodes
- Automatic failover to backup nodes
- Geographic distribution visualization
- Node resource monitoring (CPU, memory, disk)
- Centralized configuration deployment
- Node-to-node data reconciliation

---

### 50. Data Monetization Dashboard

**Description:** Tools for preparing and packaging data for sharing or monetization.

**Features:**
- Data product catalog management
- Access control and licensing configuration
- Usage analytics and metering
- Data sampling and preview generation
- Terms of service management
- API key management for data consumers
- Revenue tracking and reporting
- Data quality SLA monitoring

---

## Infrastructure & DevOps Features (2026)

> **Focus Area**: These features enhance development workflow, deployment automation, and operational excellence.

### 51. Guided Setup & Preflight Checks

**Description:** Step-by-step wizard for new users with connectivity validation.

**Features:**
- First-run setup wizard with provider selection
- Connectivity tests before enabling services
- Credential validation with clear error messages
- Presets for common configurations (Day Trader, Researcher, Data Archivist)
- Contextual tips based on detected environment
- Skip option for experienced users
- Configuration export at end of setup

**UI Components:**
- Multi-step wizard with progress indicator
- Provider test cards with status badges
- Configuration summary before completion

---

### 52. Support Bundle Composer

**Description:** Automated diagnostic bundle generation for troubleshooting.

**Features:**
- One-click bundle generation
- Automatic secret redaction with preview
- Include logs, configuration, environment info
- System health snapshot
- Recent error summary
- Optional screenshot capture
- Secure upload to support endpoint
- Bundle history with notes

**Bundle Contents:**
```
support-bundle-2026-01-11.zip
├── logs/
│   ├── app.log (last 7 days)
│   └── errors.log
├── config/
│   └── settings.json (redacted)
├── diagnostics/
│   ├── system-info.json
│   ├── health-check.json
│   └── recent-errors.json
└── manifest.json
```

---

### 53. Advanced Charting System

**Description:** Interactive charting with technical indicators for data visualization.

**Features:**
- Candlestick/OHLC bar charts
- Line and area charts for time series
- Technical indicator overlays (SMA, EMA, VWAP, RSI, MACD, Bollinger Bands)
- Volume profile sidebar
- Multi-symbol overlay comparison
- Zoom and pan with touch support
- Chart annotations and drawing tools
- Export to PNG/SVG
- Chart templates and presets

**Technical Considerations:**
- Use Win2D or SkiaSharp for high-performance rendering
- Implement data virtualization for large datasets
- Support both light and dark themes

---

### 54. Provider Health Score Dashboard

**Description:** Detailed breakdown of provider health with component-wise metrics.

**Features:**
- Health score components: Stability, Latency, Completeness, Accuracy
- Per-component trend visualization
- Threshold configuration for failover triggers
- Health history timeline
- Automatic provider ranking
- Health alerts with configurable sensitivity
- Provider comparison matrix
- SLA compliance tracking

**Health Score Components:**
| Component | Weight | Metrics |
|-----------|--------|---------|
| Stability | 30% | Uptime, reconnection frequency |
| Latency | 25% | P50, P95, P99 response times |
| Completeness | 25% | Gap rate, message delivery |
| Accuracy | 20% | Data validation pass rate |

---

### 55. PowerShell Integration Module

**Description:** PowerShell module for automation and scripting.

**Features:**
- Cmdlets for common operations:
  - `Get-MDCStatus`, `Start-MDCCollector`, `Stop-MDCCollector`
  - `Get-MDCSymbols`, `Add-MDCSymbol`, `Remove-MDCSymbol`
  - `Start-MDCBackfill`, `Get-MDCBackfillProgress`
  - `Export-MDCData`, `Get-MDCExportHistory`
- Pipeline support for bulk operations
- Tab completion for parameters
- Integration with Windows Task Scheduler
- Module documentation with examples

**Example Usage:**
```powershell
# Get current status
Get-MDCStatus | Format-Table

# Bulk add symbols
Import-Csv symbols.csv | Add-MDCSymbol

# Schedule daily backfill
Start-MDCBackfill -Symbols SPY,QQQ -Days 30 -Schedule "0 6 * * *"
```

---

### 56. REST API for Remote Management

**Description:** HTTP API for remote monitoring and control.

**Features:**
- RESTful endpoints for all major operations
- OpenAPI/Swagger documentation
- JWT authentication with API keys
- Rate limiting and throttling
- WebSocket endpoint for real-time updates
- Prometheus metrics endpoint
- Health check endpoints for load balancers
- CORS configuration for web clients

**Endpoints:**
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/status` | Current collector status |
| GET | `/api/symbols` | List subscribed symbols |
| POST | `/api/symbols` | Add symbol |
| POST | `/api/collector/start` | Start collector |
| POST | `/api/collector/stop` | Stop collector |
| GET | `/api/backfill/status` | Backfill progress |
| WS | `/api/events` | Real-time event stream |

---

### 57. Time Series Alignment & Aggregation Tools

**Description:** Pre-aggregate tick data into bars and aligned time series.

**Features:**
- Generate OHLCV bars at configurable intervals (1s, 1m, 5m, 15m, 1h, 1d)
- Multiple aggregation methods (last, mean, VWAP, TWAP)
- Gap handling strategies (forward fill, null, interpolate, skip)
- Market hours filtering with timezone support
- Pre/regular/after-hours session separation
- Cross-symbol time alignment
- Volume profile generation
- Export aggregated data alongside raw ticks

**Configuration Options:**
```json
{
  "interval": "5m",
  "aggregation": "VWAP",
  "gapHandling": "forwardFill",
  "marketHoursOnly": true,
  "timezone": "America/New_York"
}
```

---

### 58. Offline Mode & Cache Management

**Description:** Full offline functionality with intelligent caching.

**Features:**
- Read-only offline mode for disconnected environments
- Automatic cache of recent data and configuration
- Background cache refresh when online
- Connectivity banner with retry controls
- Offline edit queue with sync on reconnect
- Cache size management and cleanup
- Selective sync for large datasets
- Conflict resolution for offline edits

---

### 59. External Alert Integrations

**Description:** Send alerts to external services and communication platforms.

**Features:**
- Email alerts via SMTP configuration
- Slack webhook integration
- Microsoft Teams connector
- Discord webhooks
- PagerDuty for critical alerts
- Custom webhook support (any HTTP endpoint)
- Alert templates with variable substitution
- Alert batching to prevent spam
- Delivery status tracking

**Alert Configuration:**
```json
{
  "type": "slack",
  "webhook": "https://hooks.slack.com/...",
  "events": ["connectionLost", "dataGap", "backfillComplete"],
  "template": ":warning: *{event}* - {message}"
}
```

---

## Priority Matrix

### ✅ Completed Quick Wins
| Feature | Status | Completed |
|---------|--------|-----------|
| Real-time sparkline charts on Dashboard | ✅ Complete | 2025 |
| Connection health latency display | ✅ Complete | 2025 |
| Toast notifications for alerts | ✅ Complete | 2025 |
| Configuration export/import | ✅ Complete | 2025 |
| Storage Analytics Dashboard | ✅ Complete | 2025 |
| Collector Service Manager | ✅ Complete | 2025 |
| Cloud Storage Integration | ✅ Complete | 2025 |
| Bulk symbol import (CSV) | ✅ Complete | 2026-01 |
| Symbol search autocomplete | ✅ Complete | 2026-01 |
| Scheduled Backfill Jobs | ✅ Complete | 2026-01 |
| Backfill Progress Visualization | ✅ Complete | 2026-01 |
| Data Validation & Repair | ✅ Complete | 2026-01 |
| Subscription Templates | ✅ Complete | 2026-01 |
| Symbol Watchlists | ✅ Complete | 2026-01 |
| Credential testing with feedback | ✅ Complete | 2026-01 |
| Multi-Provider Support | ✅ Complete | 2026-01 |
| Trading Hours Manager (full) | ✅ Complete | 2026-01 |
| Data Export & Integration (full) | ✅ Complete | 2026-01 |
| Data Source Plugin System | ✅ Complete | 2026-01 |
| **App Icons & Visual Branding** | ✅ Complete | 2026-01-11 |
| **Desktop App CI/CD Pipeline** | ✅ Complete | 2026-01-11 |
| **Background Task Scheduler Service** | ✅ Complete | 2026-01-11 |
| **Enhanced MVVM ViewModels** | ✅ Complete | 2026-01-11 |
| **Admin & Maintenance Page** | ✅ Complete | 2026-01-14 |
| **Advanced Analytics Page** | ✅ Complete | 2026-01-14 |
| **Full CLI-Equivalent Services Integration** | ✅ Complete | 2026-01-14 |
| **UI/UX Improvements (context menus, accessibility)** | ✅ Complete | 2026-01-14 |

### High Impact, Low Effort (Remaining Quick Wins)
| Feature | Effort | Impact | Notes |
|---------|--------|--------|-------|
| Quick Add Symbol inline input (UWP) | 1 day | Medium | |
| Provider comparison data quality view | 2 days | Medium | Partially in AdvancedAnalyticsPage |

### High Impact, Medium Effort (Strategic)
| Feature | Effort | Impact | Status |
|---------|--------|--------|--------|
| Live Order Book Visualization | 1-2 weeks | High | |
| Data Quality Analytics Page | 1-2 weeks | High | ✅ **COMPLETE** (AdvancedAnalyticsPage) |
| Interactive Onboarding Tutorial | 1 week | Medium | |

### High Impact, High Effort (Major Features)
| Feature | Effort | Impact |
|---------|--------|--------|
| Replay & Simulation Mode | 2-3 weeks | High |
| Multi-Asset Class Support | 3-4 weeks | High |
| Dashboard Customization & Widgets (full) | 2-3 weeks | Medium |
| ML Data Preparation Tools | 2-3 weeks | Medium |
| Alert & Monitoring Center | 2 weeks | High |

### Lower Priority (Future Consideration)
| Feature | Effort | Impact |
|---------|--------|--------|
| Mobile Companion App | 4-6 weeks | Medium |
| Compliance & Audit Features | 2-3 weeks | Low |
| Multi-Provider Comparison | 2 weeks | Low |

### Offline Storage & Archival (2026) - PRIMARY FOCUS
| Feature | Effort | Impact | Priority | Status |
|---------|--------|--------|----------|--------|
| Archive Health Dashboard (#26) | 2 weeks | Critical | P0 | **IMPLEMENTED** (2026-01-03) |
| Collection Session Manager (#27) | 1-2 weeks | High | P0 | **IMPLEMENTED** (2026-01-03) |
| Portable Data Packager (#28) | 2 weeks | High | P1 | |
| Data Completeness Calendar (#29) | 2 weeks | High | P1 | |
| Archive Browser & Inspector (#30) | 2-3 weeks | High | P1 | |
| Storage Optimization Advisor (#31) | 2 weeks | Medium | P2 | |
| Write-Ahead Logging Dashboard (#32) | 1 week | Medium | P2 | |
| Multi-Drive Archive Management (#33) | 3 weeks | Medium | P2 | |
| Archive Versioning System (#34) | 2-3 weeks | Medium | P3 | |

### External Analysis Support (2026) - PRIMARY FOCUS
| Feature | Effort | Impact | Priority | Status |
|---------|--------|--------|----------|--------|
| Analysis Export Wizard (#35) | 2 weeks | Critical | P0 | |
| Schema & Data Dictionary Generator (#37) | 1 week | High | P0 | **IMPLEMENTED** (2026-01-03) |
| Data Quality Pre-Export Report (#36) | 2 weeks | High | P1 | |
| Time Series Aggregation Tools (#38) | 2 weeks | High | P1 | |
| Batch Export Scheduler (#40) | 2 weeks | High | P1 | **PARTIAL** (Scheduled exports in DataExportPage) |
| Feature Engineering Export (#39) | 3 weeks | Medium | P2 | |
| External Tool Workspace Generator (#41) | 2 weeks | Medium | P2 | |
| Dataset Fingerprinting & Versioning (#42) | 2 weeks | Medium | P2 | |
| Analysis Integration Hub (#43) | 3 weeks | Low | P3 | |

### Additional New Ideas (2026)
| Feature | Effort | Impact | Priority |
|---------|--------|--------|----------|
| Real-Time Anomaly Detection Engine (#16) | 3-4 weeks | High | P1 |
| Event-Driven Automation Framework (#19) | 2-3 weeks | High | P1 |
| Data Federation & Multi-Source Reconciliation (#18) | 3-4 weeks | High | P1 |
| Data Completeness Heatmap Calendar (#44) | 2 weeks | High | P1 |
| Real-Time Data Feed Monitor (#45) | 2 weeks | High | P1 |
| Symbol Universe Browser (#46) | 2-3 weeks | High | P1 |
| Backtest Data Validator (#47) | 2 weeks | High | P1 |
| Historical Data Comparison Tool (#20) | 2 weeks | Medium | P2 |
| Embedded Scripting Environment (#23) | 4 weeks | High | P2 |
| Market Microstructure Analytics (#17) | 3 weeks | Medium | P2 |
| Intelligent Data Recommendations (#48) | 3 weeks | Medium | P2 |
| Remote Collection Node Manager (#49) | 4 weeks | Medium | P2 |
| Smart Symbol Recommendations (#21) | 2 weeks | Medium | P3 |
| Network Diagnostics & Optimization (#24) | 2 weeks | Medium | P3 |
| Data Lineage & Provenance Tracking (#22) | 3 weeks | Medium | P3 |
| Collaborative Workspaces (#25) | 4-6 weeks | Medium | P4 |
| Data Monetization Dashboard (#50) | 4 weeks | Low | P4 |

### Infrastructure & DevOps (2026) - NEW
| Feature | Effort | Impact | Priority | Status |
|---------|--------|--------|----------|--------|
| Guided Setup & Preflight Checks (#51) | 1-2 weeks | High | P0 | |
| Support Bundle Composer (#52) | 1 week | Medium | P1 | |
| Advanced Charting System (#53) | 3-4 weeks | High | P1 | |
| Provider Health Score Dashboard (#54) | 2 weeks | High | P1 | |
| PowerShell Integration Module (#55) | 3-4 weeks | Medium | P2 | |
| REST API for Remote Management (#56) | 3-4 weeks | Medium | P2 | |
| Time Series Alignment & Aggregation (#57) | 2 weeks | High | P1 | |
| Offline Mode & Cache Management (#58) | 2-3 weeks | Medium | P2 | |
| External Alert Integrations (#59) | 2 weeks | High | P1 | |

> **Note on Priorities**: Offline storage and external analysis features are prioritized above other enhancements as they directly support the primary mission of data collection and archival. Infrastructure & DevOps features support operational excellence and automation. Cloud/online features remain implemented for future flexibility but are not the current focus.

---

## Implementation Notes

### Shared Source Files Architecture (2026-01-27)

The UWP project uses **shared source files** instead of assembly references for the Contracts library:

**Why:** The WinUI 3 XAML compiler rejects assemblies without WinRT metadata with error "Assembly is not allowed in type universe".

**Solution:**
```xml
<!-- In Meridian.Uwp.csproj -->
<ItemGroup Condition="'$(IsWindows)' == 'true'">
  <Compile Include="..\Meridian.Contracts\Configuration\*.cs"
           Link="SharedModels\Configuration\%(Filename)%(Extension)" />
</ItemGroup>
```

**Key Files:**
- `Models/SharedModelAliases.cs` - Global using directives and type aliases
- `Models/AppConfig.cs` - UWP-specific types only (e.g., `KeyboardShortcut`)
- Contracts source files linked at compile time in `SharedModels/` virtual folder

**Benefits:**
- Eliminated ~1,300 lines of duplicated DTOs
- Single source of truth in Contracts project
- Type aliases maintain backwards compatibility

### UI/UX Consistency
- All new features should follow existing WinUI 3 design patterns
- Use the established CardStyle and theme resources
- Maintain consistent spacing (24px page margins, 16px element spacing)
- Leverage Community Toolkit controls where applicable

### Performance Considerations
- Real-time visualizations should implement throttling
- Large datasets should use virtualized lists
- Consider background thread processing for analytics
- Implement caching for frequently accessed data

### Accessibility
- Ensure all new controls are keyboard navigable
- Provide high contrast theme support
- Include screen reader descriptions
- Follow Windows accessibility guidelines

---

## Related Documentation

- [Project Roadmap](../status/ROADMAP.md) - Overall feature backlog
- [Production Status](../status/production-status.md) - Deployment readiness
- [Architecture Overview](../architecture/overview.md) - System design
- [Getting Started](../getting-started/README.md) - Setup guide

---

*Last Updated: 2026-01-29*
