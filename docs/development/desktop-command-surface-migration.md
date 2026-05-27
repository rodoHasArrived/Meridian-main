# Desktop Command Surface Migration (April 2026)

## Decision

Meridian now treats **PowerShell scripts under `scripts/dev/` as the authoritative invocation surface** for desktop workflow automation and screenshot/manual capture tasks.

## Why

- The implemented automation entry points already live in `scripts/dev/`.
- Several historical Make targets were referenced in docs but never existed in `make/desktop.mk`.
- A single canonical command family reduces drift between docs and runnable tooling.

## Command mapping

| Deprecated docs command | Use this instead |
| --- | --- |
| `make desktop-run` | `pwsh ./scripts/dev/run-desktop.ps1` |
| `make desktop-workflow` | `pwsh -File scripts/dev/run-desktop-workflow.ps1 -Workflow debug-startup` |
| `make desktop-manual` | `pwsh -File scripts/dev/generate-desktop-user-manual.ps1` |
| `make desktop-screenshots` | `pwsh -File scripts/dev/capture-desktop-screenshots.ps1` |
| `make desktop-dev-bootstrap` | `pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/desktop-dev.ps1` |
| `make build-wpf` | `make desktop-build` |
| `make test-desktop-services` | `make desktop-test` |

## Going forward

- Keep desktop automation and workflow docs on PowerShell command examples.
- Keep Make usage limited to maintained targets that exist in `make/*.mk` (for example `make desktop-build` and `make desktop-test`).
- Keep `scripts/dev/desktop-dev.ps1` as the bootstrap entry point for desktop developer readiness; it validates the selected workflow profile and uses shared isolated build output by default.
- Run the desktop command docs lint check (`make docs-lint`) after editing workflow/testing docs.
