# Desktop Workflow Automation

This guide covers the scripted desktop workflows that launch `Meridian.Desktop`, walk repeatable operator flows, capture screenshots, and generate manual-ready markdown.

## What the Automation Covers

- `scripts/dev/run-desktop-workflow.ps1` launches the WPF shell, drives a named workflow, captures screenshots, and writes a JSON manifest.
- `scripts/dev/generate-desktop-user-manual.ps1` runs one or more manual workflows and produces a markdown user manual plus screenshot assets.
- `scripts/dev/capture-desktop-screenshots.ps1` now routes through the shared workflow runner so the screenshot catalog and debugging workflows use the same automation path.
- `scripts/dev/desktop-workflows.json` is the catalog of named workflows and per-step notes.

The workflows default to fixture mode so they stay deterministic and do not require a live backend to reproduce UI states.

## Quick Commands

```powershell
# Walk the default debugging flow
pwsh -File scripts/dev/run-desktop-workflow.ps1 -Workflow debug-startup

# Generate the user manual into artifacts/
pwsh -File scripts/dev/generate-desktop-user-manual.ps1

# Refresh the committed screenshot catalog
pwsh -File scripts/dev/capture-desktop-screenshots.ps1
```

Equivalent Make targets:

```bash
make desktop-workflow
make desktop-manual
make desktop-screenshots
```

## Available Workflows

| Workflow | Purpose | Output |
|---|---|---|
| `debug-startup` | Fast startup and diagnostics sweep for debugging | `artifacts/desktop-workflows/<timestamp>-debug-startup/` |
| `screenshot-catalog` | Refresh the existing WPF screenshot set used by docs | `docs/screenshots/desktop/` when run through `capture-desktop-screenshots.ps1` |
| `manual-overview` | Shell and workspace overview for operators | Included in the generated user manual |
| `manual-data-operations` | Providers, symbols, storage, and backfill workflow | Included in the generated user manual |
| `manual-research-and-trading` | Research, backtesting, Quant Script, and trading flow | Included in the generated user manual |
| `manual-governance` | Governance, ledger, notifications, and reference data flow | Included in the generated user manual |

## Runner Behavior

`run-desktop-workflow.ps1` uses two navigation mechanisms:

1. It starts the primary `Meridian.Desktop` process in fixture mode.
2. It drives page transitions by launching short-lived secondary processes with `--page=<PageTag>`, which the single-instance shell forwards to the already-running window.

That keeps navigation aligned with Meridian's own startup and deep-link handling instead of relying on brittle screen coordinates.

Each run writes:

- `manifest.json` with step timing, capture paths, and step notes
- `logs/stdout.log` and `logs/stderr.log` for startup diagnostics
- per-step screenshots

## Manual Generation

The manual generator runs every workflow marked `includeInManual: true` in `scripts/dev/desktop-workflows.json`, then writes:

- `artifacts/desktop-manuals/desktop-user-manual.md`
- `artifacts/desktop-manuals/screenshots/<workflow>/`

You can override those destinations when you want to publish the output into a docs folder:

```powershell
pwsh -File scripts/dev/generate-desktop-user-manual.ps1 `
  -OutputPath docs/generated/desktop-user-manual.md `
  -ScreenshotRoot docs/screenshots/desktop/manuals
```

## Live-Service Debugging

Fixture mode is the default because it is safer for screenshots and manual generation. When you need a live local host for debugging, launch it with the existing desktop runner first:

```powershell
pwsh -File scripts/dev/run-desktop.ps1
```

Then use `run-desktop-workflow.ps1 -NoFixture -ReuseExistingApp` if you want to drive the already-open shell without rebuilding or relaunching it.

<<<<<<< HEAD
For a specialized operator-facing smoke pass that validates Robinhood setup and the options workflow end to end, use `scripts/dev/robinhood-options-smoke.ps1`. That harness seeds and restores the desktop session files around each case so it can jump directly into `AddProviderWizard`, `Options`, and `PositionBlotter` without leaving the workstation in a modified state.

=======
>>>>>>> d5ab6a6bf3983ec9a9f290c5b8296eeb2fbc46a3
## Adding a New Workflow

Add a new entry to `scripts/dev/desktop-workflows.json`:

```json
{
  "name": "manual-example",
  "title": "Example Workflow",
  "description": "What the workflow proves or teaches.",
  "purpose": "manual",
  "includeInManual": true,
  "steps": [
    {
      "title": "Dashboard",
      "pageTag": "Dashboard",
      "captureName": "01-dashboard",
      "notes": "Explain why this page matters."
    }
  ]
}
```

Supported step fields:

- `title`: human-readable step name used in logs and manuals
- `pageTag`: WPF navigation tag forwarded as `--page=<PageTag>`
- `launchArgs`: optional raw argument array for non-page actions
- `keys`: optional `System.Windows.Forms.SendKeys` sequence after navigation
- `capture`: set to `false` when a step should act without saving a screenshot
- `captureName`: base file name for the screenshot
- `waitMs`: settle delay after the step runs
- `notes`: debugging context and manual copy for the step

## Notes

- The runner will refuse to hijack an already-running `Meridian.Desktop` session unless `-ReuseExistingApp` is supplied.
- The scripts assume Windows and the full WPF build target.
- Manual screenshots are copied out of the per-run artifacts so each generated manual is self-contained.
