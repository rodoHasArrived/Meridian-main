# AI Agent Handoff and Cost-Efficiency Checklist

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-06-04

Use this checklist for tasks that involve multiple agents, skill lanes, or handoffs across Codex,
Claude, Copilot, MCP clients, or local maintenance scripts.

## 1) Scope and Ownership

- [ ] Confirm the exact acceptance criteria before starting work.
- [ ] Identify a single coordinator (one file or artifact that owns final integration decisions).
- [ ] Assign disjoint ownership:
  - Orientation/source-finding work
  - Specialist implementation/design work
  - Review and verification
  - Documentation and index updates
- [ ] Define the minimum files and outputs each lane needs.

## 2) Context Efficiency

- [ ] Load shared navigation first:
  - `docs/ai/navigation/README.md`
  - `docs/ai/generated/repo-navigation.md`
  - `docs/ai/generated/repo-navigation.json` (for MCP tools)
- [ ] For large-repo tasks, do not recurse through the entire tree before task classification.
- [ ] Re-read only files needed for the current lane.
- [ ] Record inspected files before widening scope so downstream lanes can reuse discovery.
- [ ] Preserve reusable context in short handoff packets (required vs optional context), not full log dumps.
- [ ] Keep a compact evidence budget: summarize command output and include raw logs only when diagnosis requires them.
- [ ] Mark each shared fact as either validated evidence (with path) or assumption needing follow-up.
- [ ] Keep one authoritative source list in the handoff packet (agent names, touched files, validation status,
  residual risks).

## 3) Required Handoff Packet (Required for every lane transition)

Each handoff should include:

- `Scope`: what was requested and what was excluded.
- `Inputs loaded`: files already read and why they were needed.
- `Inspected files`: concise ledger of files or folders already scanned.
- `Changes made`: files edited plus concise reason per file.
- `Validation owner`: who is responsible for rerunning or expanding checks after integration.
- `Validation`: exact commands and outcomes.
- `Open risks`: concrete risks that remain.
- `Next lane`: requested action for the next agent/phase.
- `Required context`: exact files the next lane should read first.
- `Optional context`: useful but not required follow-up references.
- `Validation reuse`: prior validation evidence reused plus rerun triggers if touched files change.
- `Assumptions`: unresolved assumptions that still need verification.

## 4) Quality and Safety Gates

- [ ] Preserve safety and validation requirements from the shared workflow contract before skipping scope.
- [ ] Keep provider-specific instructions concise and aligned to shared policy documents.
- [ ] Include a cleanup plan if multiple files were touched and conflicts are likely.
- [ ] Confirm the handoff output is reviewable without re-reading the entire previous lane output.
- [ ] Run `python build/scripts/docs/check-ai-handoff.py --strict` when shared handoff, manifest, or mode docs change.

## 4b) Parallel Lane Integration

- [ ] Set an explicit integration order when lanes touch shared files, so merges land predictably.
- [ ] Merge lanes with the narrowest shared surface first; integrate doc/index lanes last.
- [ ] Give each lane a rollback trigger and action so one bad lane can be reverted on its own.
- [ ] Re-run shared validation (handoff, routing parity, contract drift) once after final integration.
- [ ] Summarize merge risks in one short block so the coordinator can decide whether validation reuse is still valid.

## 5) Token-Cost Efficiency

- [ ] Reuse this checklist as a fixed template so assistants do not rediscover orchestration rules each run.
- [ ] Prefer one short, structured handoff packet over large natural-language summaries.
- [ ] Include only validation evidence relevant to the touched surface.
- [ ] Keep logs summarized in the handoff packet and avoid full command output unless needed for diagnosis.
- [ ] Keep handoff file lists minimal: only touched files, files inspected, and next-lane required context.
- [ ] Prefer artifact paths (`docs/status/*.json`, generated reports) over pasted log bodies when a validator already emitted reusable evidence.

## 6) Cross-System Alignment Pointers

- Shared contract: `docs/ai/assistant-workflow-contract.md`
- Shared routing and validation links: `docs/ai/README.md`
- Orchestrated provider prompts and runbooks in `docs/prompts/` and `docs/ai/prompts/README.md`
- Codex execution details: `.codex/AGENTS.md`, `.codex/skills/_shared/codex-execution-contract.md`
- Claude/Codex/portable host guidance: `docs/ai/agents/README.md`, `docs/ai/skills/README.md`
- Copilot host guidance: `docs/ai/copilot/instructions.md`, `.github/copilot-instructions.md`
