# AI Systems Inventory

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-07-27

This is a provider-agnostic change-scope map for AI-assisted development surfaces in Meridian. It
helps an editor find the authoritative owner, affected mirrors, and narrow validation lane before a
cross-host change. The supported-system registry remains the
[`Supported AI Systems`](assistant-workflow-contract.md#supported-ai-systems) section of the shared
contract; this page does not duplicate system status or claim validation readiness.

## Change-scope map

| Change scope | Authoritative owner | Mirrors and consumers to inspect | Narrow validation start |
| --- | --- | --- | --- |
| Shared workflow, safety, orchestration, or validation policy | `docs/ai/assistant-workflow-contract.md` | `AGENTS.md`, `CLAUDE.md`, `.github/copilot-instructions.md`, provider indexes, shared skill context | `python build/scripts/docs/check-ai-inventory.py --summary` |
| Codex routing, prompts, skills, or memory | `.codex/`, `docs/ai/codex/` | `.agents/skills/` when a package is portable; generated catalogs and route artifacts when their inputs change | `python build/scripts/docs/check-codex-skills.py --summary` and the matching memory or route validator |
| GitHub Copilot behavior | `.github/copilot-instructions.md`, `.github/agents/`, `.github/instructions/`, `.github/prompts/` | `docs/ai/copilot/` and shared policy only when behavior is provider-agnostic | `python build/scripts/docs/check-ai-inventory.py --summary` |
| Claude behavior and packages | `.claude/`, `CLAUDE.md` | `docs/ai/claude/`, portable skill mirrors, and shared policy when applicable | `python build/scripts/docs/validate-skill-packages.py` for `.claude/skills` packages |
| Portable Agent Skills packages | `.agents/skills/` | Owning `.codex/skills/` or `.claude/skills/` package only when intentionally mirrored; skill indexes | `python build/scripts/docs/check-codex-skills.py --summary` plus package-specific checks |
| MCP and generated AI navigation | `src/Meridian.Mcp/`, `docs/ai/navigation/` | `docs/ai/generated/repo-navigation.json`, `docs/ai/generated/repo-navigation.md`, affected host indexes | Run the documented navigation generator and freshness check |
| Prompt and evaluation assets | `.codex/prompts/`, `.github/prompts/`, `docs/prompts/`, package-local eval scripts | `docs/ai/prompts/README.md`, generated prompt artifacts, affected host index | Run the prompt generator/checker or package-local eval lane that owns the changed asset |
| Handoff and approval evidence | `docs/ai/agent-handoff-checklist.md`, route-card and manifest sources | Generated `docs/status/` artifacts; provider-specific coordinator guidance | `python build/scripts/docs/check-ai-handoff.py --strict` |
| AI maintenance tooling | `build/scripts/docs/`, `build/scripts/ai/`, `scripts/ai/`, `make/ai.mk`, `tools/codex/` | `docs/ai/tooling/README.md` and tests for the owning script | Run the owning unit test and the narrow documented validator |

## Usage

- Start here when a task may touch more than one AI host (for example, docs + Copilot + Codex skills).
- Confirm system status against the shared contract before editing a host-specific surface.
- If a surface is missing or contradictory, record the finding and update its authoritative owner only
  when the current task scope authorizes that change; otherwise carry it in the handoff.
- Keep shared policy in `docs/ai/assistant-workflow-contract.md`; keep host-specific clarifications
  short and linked.
- Treat a narrower fallback check as diagnostic evidence only. Record any unavailable required gate as
  `not-run` or blocked.

_Last Updated: 2026-07-27_
