# Backtest Preflight & Stage Telemetry Reference

This document defines the contract surfaces used to validate a backtest run before replay and to report stage-aware progress during replay.

## Stage telemetry DTOs

- `Meridian.Backtesting.Sdk.BacktestStageTelemetryDto`
  - `Stage` (`BacktestStage`)
  - `StageElapsed` (`TimeSpan`)
  - `TotalElapsed` (`TimeSpan`)
  - `StageMessage` (`string?`)
- `Meridian.Backtesting.Sdk.BacktestProgressEvent`
  - Includes `StageTelemetry` (`BacktestStageTelemetryDto?`) in addition to legacy stage fields.

### Emission semantics

- Backtest engine emits preflight status (when configured) at `ProgressFraction = 0` and stage `ValidatingRequest`.
- Replay-loop progress events continue to emit with stage `Replaying`.
- Terminal event emits stage `Completed` with message `Complete`.

## Preflight trust-gate contracts (V2)

Namespace: `Meridian.Contracts.Backtesting`

- `BacktestPreflightRequestDto`
  - `From`, `To`, `DataRoot`, optional `Symbols`
- `BacktestPreflightReportV2Dto`
  - `IsReadyToRun`, `HasWarnings`, `Checks`, `TotalDurationMs`, `CheckedAt`, optional `SummaryMessage`
- `BacktestPreflightCheckResultDto`
  - `Name`, `Status`, `Message`, optional `Remediation`, optional `Details`
- `BacktestPreflightCheckStatusDto`
  - `Passed`, `Warning`, `Failed`

Service contract: `Meridian.Contracts.Services.IBacktestPreflightService`

- `Task<BacktestPreflightReportV2Dto> RunAsync(BacktestPreflightRequestDto request, CancellationToken ct = default)`

Default implementation: `Meridian.Application.Backtesting.BacktestPreflightService`

## Current default checks

- Date range validity (`From <= To`)
- Data-root existence
- Symbol-scope hygiene (missing symbols warn, duplicate symbols warn)

## Engine wiring

`Meridian.Backtesting.Engine.BacktestEngine` accepts an optional `IBacktestPreflightService`.

- If provided, preflight executes before universe discovery.
- If any check is `Failed`, engine throws `InvalidOperationException` and does not begin replay.
- If preflight passes (with or without warnings), replay proceeds and preflight summary is reported to progress telemetry.
