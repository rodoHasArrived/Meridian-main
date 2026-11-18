# Meridian Copilot Plugins

This folder vendors selected plugins from [github/awesome-copilot](https://github.com/github/awesome-copilot/tree/main/plugins) that are useful for Meridian development.

## Included plugins

- `frontend-web-dev` - web UI and Playwright-focused frontend workflows.
- `testing-automation` - TDD and test automation workflows.
- `security-best-practices` - security and prompt-safety review workflows.

## Usage notes

- The Claude-side plugin set (including `csharp-dotnet-development`) lives in `.claude/plugins/`;
  `frontend-web-dev` is intentionally vendored in both places for Copilot and Claude hosts.
  `plugins/frontend-web-dev` is the canonical snapshot; the `.claude/plugins/` copy must stay
  byte-for-byte identical. `python build/scripts/ai/generate-guidance.py --check` and
  `python build/scripts/docs/check-ai-inventory.py` fail on drift; `--write` re-syncs the copy.
- These plugin folders are vendored snapshots from Awesome Copilot.
- Some skills intentionally overlap between plugins (for example Playwright and prompt-safety skills).
- For Meridian tasks, repository instructions and path-specific rules in `.github/instructions/` always take precedence over generic plugin guidance.
