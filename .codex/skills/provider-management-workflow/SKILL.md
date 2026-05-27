---
name: provider-management-workflow
description: Implement secure, testable Meridian desktop provider-management workflows for setup, credentials, health, degradation, calibration, validation, fallback, and operator recovery. Use for WPF provider screens, provider view models, credential flows, and provider readiness actions.
---

# Provider Management Workflow

Read `../_shared/project-context.md` and `../_shared/codex-execution-contract.md` before editing.

## Purpose

Build provider-management workflows that are secure by default, reuse provider contracts, and expose
clear operator readiness and recovery state.

## Inputs Required

- Provider type and workflow: setup, credential, health, degradation, calibration, validation, or
  fallback.
- Existing provider contracts, credential store, UI services, WPF view models, docs, and tests.
- Live-action safety requirements and whether only read-only validation is allowed.

## Use When

Use this skill for provider setup and management surfaces in the desktop workstation.

Trigger examples:

- "Add provider credential validation to WPF."
- "Improve provider health recovery."
- "Implement provider degradation calibration UI."

## Do Not Use When

Use `meridian-provider-builder` for low-level provider adapter implementation and
`research-data-acquisition` for research ingestion workflows.

Non-trigger examples:

- "Build a new historical provider adapter."
- "Add a dense table unrelated to providers."
- "Only update provider docs."

## Workflow

1. Inventory provider SDK contracts, credential storage, health/degradation services, setup docs,
   desktop view models, and tests.
2. Keep secrets out of logs, chat, snapshots, screenshots, and test artifacts.
3. Reuse shared provider readiness, health, credential, and validation models.
4. Put provider calls in services, projection state in view models, and no provider logic in views.
5. Use async APIs, cancellation, bounded retry/backoff, and explicit operator disabled reasons.
6. Add tests for success, invalid credentials, unavailable provider, degraded provider, cancel,
   retry, and read-only/live-action guards.

## Output Expectations

- Provider workflow with secure credential handling and clear operator state.
- Tests for view model and service behavior.
- Final summary of live-action safety and credential handling.

## Files Likely Affected

- `src/Meridian.Wpf/ViewModels/*Provider*`, `Views/*Provider*`, `Services/`
- `src/Meridian.Infrastructure/`, `src/Meridian.ProviderSdk/`, `src/Meridian.Ui.Services/`
- `tests/Meridian.Wpf.Tests/`, `tests/Meridian.Tests/`

## Architecture Rules

- Do not duplicate provider credentials or provider DTOs in screen-specific models.
- Do not write secrets to environment variables.
- Keep live trading or provider mutation gated and explicit.
- Prefer shared credential and provider-status services over direct API calls.

## Testing Requirements

- Use fakes or fixtures, not live credentials.
- Cover redaction and no-secret logging where touched.
- Cover cancellation and duplicate-command prevention.

## Common Mistakes To Avoid

- Asking the user to paste secrets into chat.
- Logging provider responses that may contain account or credential data.
- Blocking the UI thread while validating credentials.
- Polling provider health without throttle or lifecycle cleanup.

## Resource Management Considerations

- Batch or debounce health checks.
- Release provider connections and subscriptions when panels close.
- Avoid repeated network calls for unchanged validation state.

## Handoffs

- Hand off to `meridian-provider-builder` for adapter-level work.
- Hand off to `performance-resource-review` for live health dashboards.
- Hand off to `desktop-test-generation` for WPF coverage.

## Validation

- Run provider-focused unit tests and affected WPF view-model tests.
- Run `pwsh ./tools/codex/resource-review.ps1` when adding polling, timers, or provider calls.
- Run `git diff --check -- <changed files>`.

## Output Standards

- State provider contracts reused, credential safety posture, and live-action limits.
- Report tests and any unvalidated provider behavior.
