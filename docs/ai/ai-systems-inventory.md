# AI Systems Inventory

**Status:** active  
**Owner:** core-team  
**Reviewed:** 2026-07-25  
**Last Updated:** 2026-07-25

This is the current, human-readable registry of AI-assisted development systems used by Meridian. It is provider-agnostic by design and meant to reduce rediscovery before cross-provider edits.

## Current AI Surface Inventory

| AI system | Provider / platform | Purpose | Config/docs location | Shared guidance used | Current alignment status | Validation / cost-efficiency notes |
| --- | --- | --- | --- | --- | --- | --- |
| Shared Meridian AI contract | Provider agnostic | Cross-provider workflow, safety, token management, orchestration, and validation rules | `docs/ai/assistant-workflow-contract.md` | Canonical | Active and authoritative | Drives all host-specific lanes; keep source docs, not duplicated long-form copies |
| Codex runtime | OpenAI Codex + local CLI tooling | Primary desktop agent orchestration, task execution, specialist skills, memory, routing, and handoff evidence | `.codex/`, `tools/codex/`, `.codex/AGENTS.md` | `.codex/skills/_shared/project-context.md`, `docs/ai/assistant-workflow-contract.md` | Active and validated | Use `docs/ai/codex/quickstart.md` and `docs/ai/codex/memory-system.md`; preserve small token contexts via mode + manifest + required/optional split |
| GitHub Copilot | GitHub Copilot coding agent + chat | Repo-level coding agent behavior, path-specific guidance, and reusable instructions | `.github/copilot-instructions.md`, `.github/agents/`, `.github/prompts/`, `.github/instructions/` | `docs/ai/assistant-workflow-contract.md` | Active and synchronized | `docs/ai/copilot/instructions.md` keeps provider-specific docs short and index-linked |
| Claude / Claude Code | Claude Code + OpenAI-compatible hosts | Portable coding assistants, plugin-based capabilities, and provider-neutral skill execution | `.claude/`, `docs/ai/agents/README.md`, `docs/ai/skills/README.md` | `docs/ai/assistant-workflow-contract.md`, `CLAUDE.md`, `.claude/skills/_shared/project-context.md` | Active and synchronized | Route through shared manifests and routing floors before expanding context |
| Agent Skills-compatible hosts | Agent SDK hosts (`.agents/`) | Portable specialist package model for multiple orchestrators | `.agents/skills/`, `docs/ai/skills/README.md`, `docs/ai/agents/README.md` | `docs/ai/assistant-workflow-contract.md` | Active and validated by package checks | Keep one package per lane; update catalog + checks in one scoped change |
| MCP / tool surfaces | MCP-compatible tooling and generated navigation | Route/lookup helper for tooling and automation | `src/Meridian.Mcp`, `docs/ai/navigation/**`, `docs/ai/generated/repo-navigation.json`, `docs/ai/generated/repo-navigation.md` | `docs/ai/navigation/README.md`, `docs/ai/assistant-workflow-contract.md` | Active with generated artifacts | Regenerate routing artifacts when ownership or entrypoints change; avoid hand-editing generated outputs |
| AI workflow tools and scripts | Repo-local scripts and checks | Contract checks, prompt routing, handoff packets, validation floor checks, context budget, maintenance scripts | `build/scripts/docs/`, `build/scripts/ai/`, `scripts/ai/`, `make/ai.mk`, `tools/codex/` | `docs/ai/tooling/README.md` | Active and complete | Use `docs/ai/tooling/README.md` first, then narrowest command lane with `--summary` |
| Prompt and evaluation surfaces | Prompt packages and evaluation adapters | Prompt libraries, evaluation manifests, provider-specific prompt quality | `build/scripts/docs/generate-prompts.py`, `.codex/prompts/`, `.github/prompts/`, `.codex/skills/*/scripts/run_evals.py` | `docs/ai/prompts/README.md`, `docs/ai/assistant-workflow-contract.md` | Active (some legacy workflow docs archived) | Keep active prompts in `prompts/`; preserve historical/archived references instead of extending old workflow paths |
| GitHub automation for AI workflows | GitHub Actions + docs automation | Automation bootstrap, documentation validation, and CI checks for AI guidance | `.github/workflows/`, `.github/workflows/README.md`, `docs/ai/tooling/README.md` | `docs/ai/assistant-workflow-contract.md` | Active, with archived historical workflow paths noted | Prefer maintained workflow lanes and documented maintenance wrappers; avoid reactivating archived flows |
| Memory and context indexing | Repo-local memory index and memory task/goal descriptors | Durable context loading, memory routing, promotion, and evidence replay | `.codex/memory/`, `docs/ai/codex/memory-system.md` | `docs/ai/assistant-workflow-contract.md`, `.codex/AGENTS.md` | Active and required for long automation | Use `check-codex-memory.py` receipts for load decisions; avoid full memory scans unless required |

## Inventory usage

- Start here when a task may touch more than one AI host (for example, docs + Copilot + Codex skills).
- Use this file when deciding whether a system is "active", "archived", or "contradictory".
- If a system is missing from this registry, treat that as a discovery/validation prompt and add/update immediately.
- For each system, keep context changes provider-scoped: shared policy changes go in `docs/ai/assistant-workflow-contract.md`, host-specific clarifications stay short and linked.

## Deferred follow-up (if needed)

- Extend this registry with historical run telemetry when `python build/scripts/docs/check-ai-inventory.py` is updated to emit richer compatibility signal fields (status/version/last-validated).
- Add optional per-system cost telemetry if a lightweight upstream model-agnostic cost estimator is introduced in local tooling.
