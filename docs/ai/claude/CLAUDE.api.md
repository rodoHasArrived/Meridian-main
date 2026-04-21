# CLAUDE.api.md — HTTP API & Application Services Reference

This sub-document covers the REST API endpoints and application service catalogue for Meridian.
Load this when working on endpoints, API clients, monitoring services, backtesting, portfolio tracking, or strategy management.

---

## HTTP API Reference

The application exposes a REST API when running with `--mode desktop`.

**Implementation Note:** 300 route constants in `UiApiRoutes.cs` across 38 endpoint files. Core endpoints (status, health, config, backfill) are fully functional. A small number of advanced endpoints may return stub responses or 501 Not Implemented.

### Core Endpoints
| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/status` | GET | Full status with metrics |
| `/api/health` | GET | Comprehensive health status |
| `/healthz`, `/readyz`, `/livez` | GET | Kubernetes health probes |
| `/api/metrics` | GET | Prometheus metrics |
| `/api/errors` | GET | Error log with filtering |
| `/api/backpressure` | GET | Backpressure status |

### Configuration API (`/api/config/`)
| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/config` | GET | Full configuration |
| `/api/config/data-source` | POST | Update active data source |
| `/api/config/symbols` | POST | Add/update symbol |
| `/api/config/symbols/{symbol}` | DELETE | Remove symbol |
| `/api/config/data-sources` | GET/POST | Manage data sources |
| `/api/config/data-sources/{id}/toggle` | POST | Toggle source enabled |

### Provider API (`/api/providers/`)
| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/providers/status` | GET | All provider status |
| `/api/providers/metrics` | GET | Provider metrics |
| `/api/providers/latency` | GET | Latency metrics |
| `/api/providers/catalog` | GET | Provider catalog with metadata |
| `/api/providers/comparison` | GET | Feature comparison |
| `/api/connections` | GET | Connection health |

### Failover API (`/api/failover/`)
| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/failover/config` | GET/POST | Failover configuration |
| `/api/failover/rules` | GET/POST | Failover rules |
| `/api/failover/health` | GET | Provider health status |
| `/api/failover/force/{ruleId}` | POST | Force failover |

### Backfill API (`/api/backfill/`)
| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/backfill/providers` | GET | Available providers |
| `/api/backfill/status` | GET | Last backfill status |
| `/api/backfill/run` | POST | Execute backfill |
| `/api/backfill/run/preview` | POST | Preview backfill |
| `/api/backfill/schedules` | GET/POST | Manage schedules |
| `/api/backfill/schedules/{id}/trigger` | POST | Trigger schedule |
| `/api/backfill/executions` | GET | Execution history |
| `/api/backfill/gap-fill` | POST | Immediate gap fill |
| `/api/backfill/statistics` | GET | Backfill statistics |

### Data Quality API (`/api/quality/`)
| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/quality/dashboard` | GET | Quality dashboard |
| `/api/quality/metrics` | GET | Real-time metrics |
| `/api/quality/completeness` | GET | Completeness scores |
| `/api/quality/gaps` | GET | Gap analysis |
| `/api/quality/gaps/{symbol}` | GET | Symbol gaps |
| `/api/quality/errors` | GET | Sequence errors |
| `/api/quality/anomalies` | GET | Detected anomalies |
| `/api/quality/latency` | GET | Latency distributions |
| `/api/quality/comparison/{symbol}` | GET | Cross-provider comparison |
| `/api/quality/health` | GET | Quality health status |
| `/api/quality/reports/daily` | GET | Daily quality report |

### SLA Monitoring API (`/api/sla/`)
| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/sla/status` | GET | SLA compliance status |
| `/api/sla/violations` | GET | SLA violations |
| `/api/sla/health` | GET | SLA health |
| `/api/sla/metrics` | GET | SLA metrics |

### Maintenance API (`/api/maintenance/`)
| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/maintenance/schedules` | GET/POST | Manage schedules |
| `/api/maintenance/schedules/{id}/trigger` | POST | Trigger maintenance |
| `/api/maintenance/executions` | GET | Execution history |
| `/api/maintenance/execute` | POST | Immediate execution |
| `/api/maintenance/task-types` | GET | Available task types |

### Packaging API (`/api/packaging/`)
| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/packaging/create` | POST | Create package |
| `/api/packaging/import` | POST | Import package |
| `/api/packaging/validate` | POST | Validate package |
| `/api/packaging/list` | GET | List packages |
| `/api/packaging/download/{fileName}` | GET | Download package |

### Backtesting API (`/api/backtesting/`)
| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/backtesting/strategies` | GET | List registered strategies |
| `/api/backtesting/run` | POST | Execute backtest |
| `/api/backtesting/status/{runId}` | GET | Get backtest status |
| `/api/backtesting/results/{runId}` | GET | Get backtest results |
| `/api/backtesting/results/{runId}/metrics` | GET | Performance metrics (Sharpe, drawdown, XIRR) |
| `/api/backtesting/results/{runId}/fills` | GET | Fill tape |
| `/api/backtesting/results/{runId}/portfolio` | GET | Portfolio snapshots |
| `/api/backtesting/compare` | POST | Compare multiple backtests |
| `/api/backtesting/history` | GET | Backtest execution history |

### Strategy Execution API (`/api/strategies/`)
| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/strategies/list` | GET | List registered strategies |
| `/api/strategies/register` | POST | Register new strategy |
| `/api/strategies/{id}/start` | POST | Start strategy |
| `/api/strategies/{id}/pause` | POST | Pause strategy |
| `/api/strategies/{id}/stop` | POST | Stop strategy |
| `/api/strategies/{id}/status` | GET | Get strategy status |
| `/api/strategies/{id}/parameters` | GET/POST | Get/update strategy parameters |

### Portfolio Tracking API (`/api/portfolio/`)
| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/portfolio/snapshot` | GET | Current portfolio snapshot |
| `/api/portfolio/snapshot/{timestamp}` | GET | Historical portfolio snapshot |
| `/api/portfolio/positions` | GET | Current positions |
| `/api/portfolio/pnl` | GET | PnL summary |
| `/api/portfolio/metrics` | GET | Performance metrics |
| `/api/portfolio/metrics/{strategyId}` | GET | Strategy-specific metrics |
| `/api/portfolio/history` | GET | Historical portfolio data |
| `/api/portfolio/comparison` | POST | Compare multiple runs |

---

## Data Quality Monitoring

Located in `src/Meridian.Application/Monitoring/DataQuality/`.

### Quality Services
| Service | Purpose |
|---------|---------|
| `DataQualityMonitoringService` | Orchestrates all quality checks |
| `CompletenessScoreCalculator` | Calculates data completeness scores |
| `GapAnalyzer` | Detects and analyzes data gaps |
| `SequenceErrorTracker` | Tracks sequence/integrity errors |
| `AnomalyDetector` | Detects data anomalies |
| `LatencyHistogram` | Tracks latency distribution |
| `CrossProviderComparisonService` | Compares data across providers |
| `PriceContinuityChecker` | Checks price continuity |
| `DataFreshnessSlaMonitor` | Monitors data freshness SLA |
| `DataQualityReportGenerator` | Generates quality reports |

### Quality Metrics
- **Completeness Score** — Percentage of expected data received
- **Gap Analysis** — Missing data periods with duration
- **Sequence Errors** — Out-of-order or duplicate events
- **Anomaly Detection** — Unusual price/volume patterns
- **Latency Distribution** — End-to-end latency percentiles
- **Cross-Provider Comparison** — Data consistency across providers
- **SLA Compliance** — Data freshness within thresholds

---

## Application Services

### Core Services (`src/Meridian.Application/Services/`)
| Service | Purpose |
|---------|---------|
| `ConfigurationService` | Configuration loading with self-healing |
| `ConfigurationWizard` | Interactive configuration setup |
| `AutoConfigurationService` | Auto-config from environment |
| `PreflightChecker` | Pre-startup validation |
| `GracefulShutdownService` | Graceful shutdown coordination |
| `DryRunService` | Dry-run validation mode |
| `DiagnosticBundleService` | Comprehensive diagnostics |
| `TradingCalendar` | Market hours and holidays |

### Monitoring Services (`src/Meridian.Application/Monitoring/`)
| Service | Purpose |
|---------|---------|
| `ConnectionHealthMonitor` | Provider connection health |
| `ProviderLatencyService` | Latency tracking |
| `SpreadMonitor` | Bid-ask spread monitoring |
| `BackpressureAlertService` | Backpressure alerts |
| `ErrorTracker` | Error categorization |
| `PrometheusMetrics` | Metrics export |

### Storage Services
| Service | Location | Purpose |
|---------|----------|---------|
| `WriteAheadLog` | `Storage/Archival/` | WAL for durability |
| `PortableDataPackager` | `Storage/Packaging/` | Data package creation |
| `TierMigrationService` | `Storage/Services/` | Hot/warm/cold tier migration |
| `ScheduledArchiveMaintenanceService` | `Storage/Maintenance/` | Scheduled maintenance |
| `HistoricalDataQueryService` | `Application/Services/` | Query stored data |

---

## CI/CD Pipelines

25+ GitHub Actions workflows in `.github/workflows/`:

| Workflow | Purpose |
|----------|---------|
| `benchmark.yml` | Performance benchmarks |
| `bottleneck-detection.yml` | Performance bottleneck detection |
| `build-observability.yml` | Build metrics collection |
| `code-quality.yml` | Code quality checks (formatting, analyzers) |
| `desktop-builds.yml` | Desktop app builds (WPF) |
| `docker.yml` | Docker image building and publishing |
| `documentation.yml` | Documentation generation, AI instruction sync, TODO scanning |
| `nightly.yml` | Nightly builds |
| `pr-checks.yml` | PR validation checks |
| `release.yml` | Release automation |
| `security.yml` | Security scanning (CodeQL, dependency audit) |
| `test-matrix.yml` | Multi-platform test matrix (Windows, Linux, macOS) |
| `update-diagrams.yml` | Architecture diagram and UML generation |
| `validate-workflows.yml` | Workflow validation |

Full workflow details: `docs/development/github-actions-summary.md`

---

*Last Updated: 2026-03-18*
