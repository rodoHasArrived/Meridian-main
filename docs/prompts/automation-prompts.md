# Automation Prompts

This page inventories automation-oriented prompt and workflow surfaces so new
automation guidance is not scattered across the repo.

## Active Surfaces

| Surface | Role |
| --- | --- |
| `.github/prompts/*.prompt.yml` | Copilot prompt files for common maintenance, provider, review, test, performance, and WPF tasks |
| `.github/prompts/README.md` | Prompt usage guide and prompt catalog |
| `.github/workflows/prompt-generation.yml` | Prompt generation workflow |
| `build/scripts/docs/generate-prompts.py` | Prompt-generation helper |
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
- `project-context`
- `provider-implementation-guide`
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
- Keep current local path examples pointed at `C:\Dev\Meridian-main`.
- Move obsolete prompt notes to `docs/archive/` with a replacement link.
- Do not duplicate generated prompt catalogs by hand; update the generator or
  source inventory instead.
