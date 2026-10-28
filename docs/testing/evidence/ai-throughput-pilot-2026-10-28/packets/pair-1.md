# Task context packet

Insert exactly Pilot handoffs record the focused validation command and its observed outcome. immediately after ## Useful Commands with a blank line before and after. Synchronize canonical shared context to both mirrors and preserve other bytes.

Packets provide partial context and never override applicable instructions.

## Scope

- `.agents/skills/_shared/project-context.md`
- `.claude/skills/_shared/project-context.md`
- `.codex/skills/_shared/project-context.md`

## Instructions to read in full

- `.codex/skills/_shared/project-context.md`
- `AGENTS.md`

## Validation candidates (not executed)

- `python build/scripts/docs/check-ai-navigation-freshness.py --max-age-days 14`

## Warnings

- No matching source-module owner; inspect the nearest README for ownership and proof.
- Excerpts are partial context. Read applicable instructions in full; packets never override them.

## Omitted context

- `src/Meridian.Application/Pipeline/EventPipeline.cs`: source missing
- `src/Meridian.Contracts`: source missing
- `src/Meridian.Contracts/Lifecycle`: source missing
- `src/Meridian.Contracts/Operations`: source missing
- `src/Meridian.Core/Serialization/MarketDataJsonContext.cs`: source missing
- `src/Meridian.Launcher/StartupOutcomeReceiptMonitor.cs`: source missing
- `src/Meridian.Setup/InstallationTransaction.cs`: source missing
- `src/Meridian.Setup/PayloadPackage.cs`: source missing
- `src/Meridian/Program.cs`: source missing
- `docs/operators/browser-workstation-installer.md`: source missing
- `docs/reference/lifecycle-control-plane.md`: source missing
- `docs/reference/verified-operation-outcomes.md`: source missing
- `docs/ai/README.md`: source missing
- `docs/ai/ai-known-errors.md`: source missing
- `docs/ai/navigation/README.md`: source missing
- `src/Meridian.Application/Composition`: source missing
- `src/Meridian.Application/Pipeline`: source missing
- `src/Meridian.Core/Serialization`: source missing
- `src/Meridian.Launcher/LauncherCommandPolicy.cs`: source missing
- `src/Meridian.Launcher/Program.cs`: source missing
- `src/Meridian.LifecycleSupervisor/LifecycleStartupOutcome.cs`: source missing
- `src/Meridian.LifecycleSupervisor/LifecycleSupervisorRuntime.cs`: source missing
- `src/Meridian.LifecycleSupervisor/Program.cs`: source missing
- `src/Meridian.Setup/Program.cs`: source missing

## Selected source excerpts

### .agents/skills/_shared/project-context.md:84

target file

````
## Source Documentation Mesh

- Active documentation is organized around `docs/start/`, `docs/product/`, `docs/engineering/`,
  `docs/operators/`, `docs/ai/`, `docs/roadmap/`, `docs/source/`, `docs/reference/`, and
  `docs/generated/`. Treat `docs/status/`, `docs/development/`, and `docs/operations/` as canonical
  only when linked from those indexes or registry-owned workflows. `docs/plans/` holds redirect stubs
  only; every plan is listed in the Plans and Blueprints Register.
- Roadmap truth lives in `docs/roadmap/data/*.yml`; generated roadmap views live in
  `docs/roadmap/generated/`.
- Source/module truth lives in `docs/source/data/*.yml`; registered modules have local
  `src/**/README.md` files with purpose, ownership, diagrams, roadmap traceability, TODOs, and
  validation commands.
- Before editing `src/**`, read the nearest source README, identify the module ID in
  `docs/source/data/source-modules.yml`, and update source README or registry records when behavior,
  validation, ownership, diagrams, or TODO scope changes.
- Do not hand-edit generated roadmap/source docs. Update registry data or renderers under
  `build/scripts/docs/`, then rerun the narrow generator.
- Use `python3 build/scripts/docs/mark-stale-docs.py --write --summary` to mark registered modules
  whose code or README hashes need documentation review.
- Use `python3 build/scripts/docs/validate-doc-hashes.py --summary` to detect code/docs drift for
  registered modules. Refresh reviewed stale module entries with
  `python3 build/scripts/docs/validate-doc-hashes.py --write-module <MODULE_ID> --summary`;
  reserve broad `--write --summary` for a full accepted-baseline review.

---

````

### .claude/skills/_shared/project-context.md:84

target file

````
## Source Documentation Mesh

- Active documentation is organized around `docs/start/`, `docs/product/`, `docs/engineering/`,
  `docs/operators/`, `docs/ai/`, `docs/roadmap/`, `docs/source/`, `docs/reference/`, and
  `docs/generated/`. Treat `docs/status/`, `docs/development/`, and `docs/operations/` as canonical
  only when linked from those indexes or registry-owned workflows. `docs/plans/` holds redirect stubs
  only; every plan is listed in the Plans and Blueprints Register.
- Roadmap truth lives in `docs/roadmap/data/*.yml`; generated roadmap views live in
  `docs/roadmap/generated/`.
- Source/module truth lives in `docs/source/data/*.yml`; registered modules have local
  `src/**/README.md` files with purpose, ownership, diagrams, roadmap traceability, TODOs, and
  validation commands.
- Before editing `src/**`, read the nearest source README, identify the module ID in
  `docs/source/data/source-modules.yml`, and update source README or registry records when behavior,
  validation, ownership, diagrams, or TODO scope changes.
- Do not hand-edit generated roadmap/source docs. Update registry data or renderers under
  `build/scripts/docs/`, then rerun the narrow generator.
- Use `python3 build/scripts/docs/mark-stale-docs.py --write --summary` to mark registered modules
  whose code or README hashes need documentation review.
- Use `python3 build/scripts/docs/validate-doc-hashes.py --summary` to detect code/docs drift for
  registered modules. Refresh reviewed stale module entries with
  `python3 build/scripts/docs/validate-doc-hashes.py --write-module <MODULE_ID> --summary`;
  reserve broad `--write --summary` for a full accepted-baseline review.

---

````

### .codex/skills/_shared/project-context.md:1

target file

````
# Meridian — Shared Project Context

> Codex copy. Use this file for Meridian-specific terminology, product direction, commands, and
> architecture when a Codex skill needs repository grounding without repeating the same facts in
> every `SKILL.md`.
>
> **Last verified:** 2026-09-28
> **Primary grounding docs:** `README.md`, `docs/roadmap/data/*.yml`,
> `docs/roadmap/generated/ROADMAP_SUMMARY.md`, `docs/product/meridian-design-document.md`,
> `docs/architecture/meridian-development-intelligence-framework.md`
>
> The section between the `shared-context` markers is identical in all three copies:
> `.claude/skills/_shared/project-context.md` (canonical, edit it first),
> `.codex/skills/_shared/project-context.md`, and `.agents/skills/_shared/project-context.md`.
> After editing the canonical section, run `python build/scripts/ai/generate-guidance.py --write`.
> Check freshness with `python build/scripts/ai/generate-guidance.py --check`; the existing
> `build/scripts/docs/check-ai-inventory.py` gate also detects drift. Host-specific guidance stays
> outside the generated section and is preserved byte-for-byte.

<!-- shared-context:begin — identical in .claude/, .codex/, and .agents/ copies; check-ai-inventory.py enforces it -->

````

