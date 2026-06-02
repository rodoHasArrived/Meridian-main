# AI Tooling And Validation Index

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-06-02

Use this index when an AI task needs a script, validator, generator, or maintenance lane. It is the
shared discoverability surface for Codex, Claude, Copilot, MCP clients, and local automation runs.

Load this page after `docs/ai/assistant-workflow-contract.md` when the task involves AI guidance,
agent orchestration, prompt routing, repository navigation artifacts, or AI-maintenance scripts.

## Fast Selection

| Need | Start here | Typical proof lane |
| --- | --- | --- |
| Inventory AI surfaces or catch missing indexes | `build/scripts/docs/check-ai-inventory.py` | `python build/scripts/docs/check-ai-inventory.py --summary` |
| Validate Codex skill metadata and docs links | `build/scripts/docs/check-codex-skills.py` | `python build/scripts/docs/check-codex-skills.py --summary` |
| Validate shared handoff/parallel guidance | `build/scripts/docs/check-ai-handoff.py` | `python build/scripts/docs/check-ai-handoff.py --strict` |
| Validate shared policy mirrors | `build/scripts/docs/check-ai-contract-drift.py` | `python build/scripts/docs/check-ai-contract-drift.py --canonical docs/ai/contract-policy.json --mirror docs/ai/copilot/contract-policy.mirror.json --mirror docs/ai/claude/contract-policy.mirror.json` |
| Route a prompt into lane, mode, and validation floor | `build/scripts/docs/prompt-route-linter.py` | `python build/scripts/docs/prompt-route-linter.py --summary` |
| Emit a compact handoff packet from route evidence | `build/scripts/docs/handoff-packet-generator.py` | `python build/scripts/docs/handoff-packet-generator.py --summary --route-json docs/status/prompt-route-lint-report.json` |
| Check handoff packet schema and validation floor | `build/scripts/docs/check-handoff-packet-schema.py`, `build/scripts/docs/check-validation-floor.py`, `build/scripts/docs/check-mode-escalation.py` | Run the matching `docs/status/*` checks after route or handoff changes |
| Refresh repo-navigation artifacts | `build/scripts/docs/generate-ai-navigation.py` | `python build/scripts/docs/generate-ai-navigation.py --json-output docs/ai/generated/repo-navigation.json --markdown-output docs/ai/generated/repo-navigation.md --recent-changes-output docs/ai/generated/recent-changes.md --summary` |
| Apply deterministic scoped text edits | `build/scripts/ai/ai-edit-tool.py` | Preview with `plan`, then apply the saved plan |
| Build token-capped orientation packs | `build/scripts/ai/context-budget.py` | `python build/scripts/ai/context-budget.py --task "<task>" --target-file <path> --summary` |
| Run broader AI maintenance audits | `build/scripts/ai-repo-updater.py`, `make/ai.mk`, `scripts/ai/*.sh` | Pick the narrowest `audit`, `verify`, or maintenance lane |
| Run Codex-specific desktop quality scans | `tools/codex/*.ps1` | Use the nearest focused PowerShell tool, then report exact command/results |

## Tool Families

### Inventory And Catalog Drift

Use these before claiming an AI surface is current:

- `build/scripts/docs/check-ai-inventory.py`
  - Inventories root assistant shims, Codex/Claude/Copilot surfaces, prompts, tools, and optional
    IDE entrypoints.
- `build/scripts/docs/check-codex-skills.py`
  - Validates Codex skill structure, metadata, and linked docs.
- `build/scripts/docs/validate-skill-packages.py`
  - Checks portable skill packages mirrored for non-Codex hosts.

### Orchestration, Handoff, And Routing

Use these for multi-agent, parallel, or route-aware work:

- `build/scripts/docs/check-ai-handoff.py`
  - Verifies handoff and manifest guidance. Use `--strict` for shared AI-doc changes.
- `build/scripts/docs/prompt-route-linter.py`
  - Maps prompt intent to lane, skill, mode, validation floor, telemetry, and escalation triggers.
- `build/scripts/docs/handoff-packet-generator.py`
  - Emits `docs/status/ai-handoff-packet.json` from route evidence.
- `build/scripts/docs/check-handoff-packet-schema.py`
  - Verifies the emitted handoff packet schema.
- `build/scripts/docs/check-validation-floor.py`
  - Confirms the route's required validation floor was satisfied.
- `build/scripts/docs/check-mode-escalation.py`
  - Flags mode/context mismatches between route evidence and executed work.
- `build/scripts/docs/check-ai-routing-parity.py`
  - Confirms route and handoff rules stay aligned.

### Navigation And Generated AI Artifacts

- `build/scripts/docs/generate-ai-navigation.py`
  - Regenerates `docs/ai/generated/repo-navigation.json`,
    `docs/ai/generated/repo-navigation.md`, and `docs/ai/generated/recent-changes.md`.
- `build/scripts/docs/check-ai-navigation-freshness.py`
  - Detects stale navigation artifacts.
- `build/scripts/docs/repair-links.py`
  - Fast docs-link hygiene after AI-doc changes.
- `build/scripts/docs/validate-docs-structure.py`
  - Detects documentation tree drift for active lanes.

### Scoped Edit And Maintenance Helpers

- `build/scripts/ai/ai-edit-tool.py`
  - Preview-first text rewrite tool for deterministic, reviewable repository edits.
- `build/scripts/ai/context-budget.py`
  - Generates subsystem-aware, token-capped context packs from task text and touched files.
- `build/scripts/ai-repo-updater.py`
  - Broader repository audit and maintenance helper.
- `scripts/ai/setup.sh`, `scripts/ai/setup-ai-agent.sh`
  - Local AI environment/bootstrap helpers.
- `scripts/ai/maintenance-light.sh`, `scripts/ai/maintenance-full.sh`
  - Light/full maintenance wrappers.
- `scripts/ai/route-maintenance.sh`
  - Route-maintenance helper lane.
- `make/ai.mk`
  - Canonical make wrappers for common AI audit, maintenance, docs-map, and skill checks.

### Codex-Focused PowerShell Tools

Use only when the task is Codex-specific and the narrower docs validators are not enough:

- `tools/codex/run-codex-quality-suite.ps1`
- `tools/codex/architecture-scan.ps1`
- `tools/codex/component-inventory.ps1`
- `tools/codex/desktop-workspace-generator.ps1`
- `tools/codex/mvvm-compliance-check.ps1`
- `tools/codex/refactor-plan-generator.ps1`
- `tools/codex/resource-review.ps1`
- `tools/codex/shared-pattern-suggest.ps1`

## Token And Cost Discipline

- Start with the smallest validator that proves the change; do not default to full maintenance runs.
- Prefer generated summaries, packet artifacts, and inventories over pasting raw logs into prompts.
- Reuse route artifacts and handoff packets instead of re-scanning the same AI docs in a new lane.
- Record inspected files, validation reuse, and rerun triggers in the handoff packet or manifest so
  the next lane does not repeat discovery.
- Use `ai-edit-tool.py plan` before broad text rewrites to keep prompt size, file count, and edit
  count explicit.

## Safe Usage Rules

- Do not hand-edit generated outputs under `docs/ai/generated/`; regenerate them.
- Prefer `--summary` and artifact outputs over verbose console dumps.
- Treat `make ai-maintenance-full`, `make ai-verify`, and broader audits as expanded-cost lanes:
  run them only when the touched surfaces justify the wider proof.
- Keep host-specific docs aligned with the shared contract when a tool or validation lane changes.

## Related Resources

- [`../README.md`](../README.md)
- [`../assistant-workflow-contract.md`](../assistant-workflow-contract.md)
- [`../agent-handoff-checklist.md`](../agent-handoff-checklist.md)
- [`../parallel-task-manifest-template.md`](../parallel-task-manifest-template.md)
- [`../work-modes.md`](../work-modes.md)
- [`../codex/quickstart.md`](../codex/quickstart.md)
