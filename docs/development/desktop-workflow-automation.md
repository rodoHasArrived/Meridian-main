# Desktop Workflow Automation

This guide covers the scripted desktop workflows that launch `Meridian.Desktop`, walk repeatable operator flows, capture screenshots, and generate manual-ready markdown.

## What the Automation Covers

- `scripts/dev/run-desktop-workflow.ps1` launches the WPF shell, drives a named workflow, captures screenshots, and writes a JSON manifest.
- `scripts/dev/generate-desktop-user-manual.ps1` runs one or more manual workflows and produces a markdown user manual plus screenshot assets.
- `scripts/dev/capture-desktop-screenshots.ps1` now routes through the shared workflow runner so the screenshot catalog and debugging workflows use the same automation path.
- `scripts/dev/desktop-workflows.json` is the catalog of named workflows and per-step notes.

The workflows default to fixture mode so they stay deterministic and do not require a live backend to reproduce UI states. In the desktop shell this is presented as neutral demo data, not as an operational warning.

## Quick Commands

```powershell
# Walk the default debugging flow
pwsh -File scripts/dev/run-desktop-workflow.ps1 -Workflow debug-startup

# Generate the user manual into artifacts/
pwsh -File scripts/dev/generate-desktop-user-manual.ps1

# Refresh the committed screenshot catalog
pwsh -File scripts/dev/capture-desktop-screenshots.ps1
```

## Supported command surface (authoritative)

Desktop workflow automation is **PowerShell-script first**.

- Supported: `pwsh -File scripts/dev/*.ps1` (or `pwsh ./scripts/dev/*.ps1`)
- Not supported as canonical workflow entry points: `make desktop-workflow`, `make desktop-manual`, `make desktop-screenshots`

For migration context and replacement mappings, see:

- [Desktop command-surface migration note](./desktop-command-surface-migration.md)

## Available Workflows

| Workflow | Purpose | Output |
| --- | --- | --- |
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

Restore and build now share the same configuration, WPF build flags, and isolation key before the runner uses `build --no-restore`. The restore step lets each project restore its declared target framework so shared `net9.0` libraries get matching assets, while the build step pins the desktop shell to `net9.0-windows10.0.19041.0`.

Before any screenshot is saved, the runner now:

1. brings Meridian back to the foreground,
2. enters the operating-context selector when fixture startup lands on that first-run surface and fails immediately if that selection cannot be confirmed,
3. re-queries the live shell window,
4. checks `ShellAutomationState` / `PageTitleText` markers, whose automation names expose the current page tag and page title,
5. fails the step if the requested page was not actually confirmed.

The runner resolves the Meridian window from the owned `Meridian.Desktop` process handle first and
only falls back to narrow title-based UI Automation lookup. Avoid broad root-window scans in this
script; they can time out on headless CI runners while heavy pages are loading. Descendant lookups
for shell readiness markers are timeout-tolerant and return "not ready yet" so the existing polling
loop can continue through transient WPF navigation delays.

Each run writes:

- `manifest.json` with operating-context confirmation, step timing, capture paths, and step notes
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

For a specialized operator-facing smoke pass that validates Robinhood setup and the options workflow end to end, use `scripts/dev/robinhood-options-smoke.ps1`.

- The harness builds `src/Meridian.Wpf/Meridian.Wpf.csproj` by default unless `-SkipBuild` is supplied.
- It uses the bundled seed at `scripts/dev/fixtures/robinhood-options-smoke.seed.json` instead of relying on whatever happens to be in `%LocalAppData%\Meridian`.
- It starts the primary desktop shell without `--page` arguments, waits for the operating context to restore, and only then uses forwarded `--page=<PageTag>` launches as a retry path.
- Run artifacts now land under `artifacts/desktop-workflows/robinhood-options-smoke/`, including seeded session JSON, post-run workspace snapshots, screenshots, and UI automation dumps for failures.

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
      "title": "Research Workspace",
      "pageTag": "ResearchShell",
      "captureName": "01-research-workspace",
      "notes": "Explain why this workspace matters."
    }
  ]
}
```

Supported step fields:

- `title`: human-readable step name used in logs and manuals
- `pageTag`: WPF navigation tag forwarded as `--page=<PageTag>`; normal top-level workflow landings should use `ResearchShell`, `TradingShell`, `DataOperationsShell`, or `GovernanceShell`
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

## Screenshot diff classes and approval flow

`refresh-screenshots.yml` now classifies changed screenshots into:

- `blocking-regression` (major layout/structure loss, missing route/component evidence, missing image baseline/current image, or threshold breach),
- `review-needed` (moderate visual delta that needs a human decision),
- `non-blocking-noise` (small anti-aliasing/theme variance).

Thresholds, pixel tolerance, and per-image mask rectangles are versioned in:

- `scripts/dev/screenshot-diff-config.json`

The workflow publishes a `screenshot-diff-report` artifact with per-image category labels plus baseline/current/diff thumbnails.

Default CI behavior gates only on `blocking-regression`. `review-needed` does not fail the job by default, but auto-commit is withheld unless an explicit workflow-dispatch approval is supplied:

- `approve_review_needed=true`
- `review_approval_note=<required audit rationale>`

Approval actor/reason are recorded in the generated diff summary so baseline updates remain intentional and auditable.
