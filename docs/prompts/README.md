# Prompt Documentation

**Status:** supporting
**Owner:** core-team
**Reviewed:** 2026-07-19

This folder consolidates the active prompt, agent, and automation-note map.

- [Automation Prompts](automation-prompts.md)
- [Repo Maintenance Prompts](repo-maintenance-prompts.md)
- [Roadmap And Source Docs Implementation Prompt](roadmap-source-docs-implementation-prompt.md)

Keep this folder small. The prompt files themselves remain in their host-owned
locations such as `.github/prompts/`, `.github/agents/`, `.agents/`, `.codex/`,
and `docs/ai/`.
Use repository-relative paths in reusable prompt guidance. Put machine-specific
checkout paths in local run notes or automation memory, not committed prompt docs.
