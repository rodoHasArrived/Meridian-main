# Automation Prompts

This page inventories automation-oriented prompt and workflow surfaces so new
automation guidance is not scattered across the repo.

## Active Surfaces

| Surface | Role |
| --- | --- |
| `.github/prompts/*.prompt.yml` | Copilot prompt files for common maintenance, provider, review, test, performance, and WPF tasks |
| `.github/prompts/README.md` | Prompt usage guide and prompt catalog |
| `build/scripts/docs/generate-prompts.py` | Current local prompt-generation helper |
| `archive/docs/workflows/legacy-github-actions-2026-05-18.md` | Archive note for retired prompt-generation workflow automation |
| `docs/ai/prompts/README.md` | AI docs index for prompt usage |
| `scripts/ai/*.sh` | AI maintenance routing scripts |

## Current Prompt Inventory

- `add-data-provider`
- `add-export-format`
- `code-review`
- `configure-deployment`
- `explain-architecture`
- `fix-build-errors`
- `fix-code-quality`
- `fix-test-failures`
- `optimize-performance`
- `operations-continuity-core`
- `project-context`
- `provider-implementation-guide`
- `runtime-observability-diagnostics`
- `simulate-user-panel*`
- `troubleshoot-issue`
- `workflow-results-code-quality`
- `workflow-results-test-matrix`
- `wpf-debug-improve`
- `wpf-design-system-screen-impact`
- `write-unit-tests`

## Maintenance Rules

- Update `.github/prompts/README.md` and this page when adding, renaming, or
  retiring a prompt.
- Prefer extending an existing prompt before creating a near-duplicate.
- Use repository-relative paths in committed prompt guidance. Keep machine-specific checkout paths
  in local run notes or automation memory.
- Move obsolete prompt notes to `archive/docs/` with a replacement link.
- Do not duplicate generated prompt catalogs by hand; update the generator or
  source inventory instead.
