# Tooling & Workflow Backlog

**Owner:** Core Team  
**Scope:** Engineering — Tooling, automation, and contributor workflow  
**Status:** Proposed  
**Last Updated:** 2026-03-20

---

## Purpose

This backlog turns the current tooling and workflow improvement themes into a concrete, prioritized set of implementation tickets. It focuses on reducing automation drift, clarifying ownership, and making repo metadata self-validating.

---

## Current Friction Snapshot

The highest-value cleanup areas are all drift-related:

- `package.json` and `Makefile` have historically pointed at different Node entrypoints for icon generation.
- Workflow inventory counts have drifted across `README.md`, `.github/workflows/README.md`, and development docs.
- `.github/dependabot.yml` has had duplicated root npm coverage and path references that need periodic validation.
- `Directory.Build.props` currently suppresses multiple warning categories globally, which obscures where the real cleanup work belongs.

These problems are individually small but collectively expensive because they undermine trust in local commands, CI lanes, and maintenance docs.

---

## Recommended Delivery Sequence

1. **Canonicalize command entrypoints and remove drift**
2. **Modularize the root `Makefile` into focused include files**
3. **Make automation inventory self-validating**
4. **Define shared local/CI verification lanes**
5. **Replace blanket warning suppression with a ratcheting policy**
6. **Separate required AI gates from advisory automation**

---

## Backlog

### MW-001 — Canonicalize Node-based tooling entrypoints
- **Priority:** P0
- **Effort:** S
- **Outcome:** One canonical invocation path for repo-local Node tooling.
- **Scope:**
  - Standardize `generate-icons` and `generate-diagrams` on shared script locations.
  - Make `Makefile` targets call `npm run ...` instead of hard-coded Node paths.
  - Update docs to reference the same entrypoint everywhere.
- **Why now:** This is the fastest way to stop docs, CI, and local commands from diverging.
- **Definition of done:** `package.json`, `Makefile`, and docs all point to the same entrypoints, with a validation check to catch regressions.

### MW-002 — Add tooling metadata contract tests
- **Priority:** P0
- **Effort:** S
- **Outcome:** Fast validation for high-friction repo metadata.
- **Scope:**
  - Validate `package.json` script targets.
  - Validate hard-coded helper paths in `Makefile`.
  - Validate `dependabot.yml` directories.
  - Add a single command suitable for CI and local preflight runs.
- **Why now:** Broken references are cheap to introduce and disproportionately annoying to debug.
- **Definition of done:** A single validation script exists and returns non-zero on missing referenced paths.

### MW-003 — Fix and generate workflow inventory documentation
- **Priority:** P1
- **Effort:** M
- **Outcome:** Workflow docs stop relying on hand-maintained counts.
- **Scope:**
  - Generate workflow inventory from `.github/workflows/*.yml`.
  - Remove or minimize manually maintained workflow counts in docs.
  - Produce a single authoritative summary artifact consumed by README/docs.
- **Why now:** Contributor trust drops quickly when workflow counts and names disagree.
- **Definition of done:** Workflow inventory is generated from files on disk and docs stop diverging on counts.

### MW-004 — Modularize the `Makefile`
- **Priority:** P1
- **Effort:** M
- **Outcome:** Lower merge pressure and clearer ownership boundaries.
- **Scope:**
  - Split targets into `make/install.mk`, `make/build.mk`, `make/test.mk`, `make/docs.mk`, `make/desktop.mk`, `make/ai.mk`, and `make/diagnostics.mk`.
  - Keep root `Makefile` as an index/help layer.
  - Preserve current command names during migration.
- **Why now:** The root `Makefile` is broad enough that even safe edits create review overhead.
- **Definition of done:** Root `Makefile` mostly includes modular target files and help output still works.

### MW-005 — Define blessed verification lanes
- **Priority:** P1
- **Effort:** M
- **Outcome:** Local development and CI use the same mental model.
- **Scope:**
  - Introduce lanes such as `bootstrap`, `verify-fast`, `verify-full`, `verify-docs`, `verify-desktop`, and `verify-release`.
  - Map existing Make targets and GitHub Actions jobs onto those lanes.
  - Document when each lane should be used.
- **Why now:** This reduces "works locally but not in CI" mismatches.
- **Definition of done:** A short list of blessed lanes exists and is reused in both docs and workflows.

### MW-006 — Clean up Dependabot duplication and add path validation to CI
- **Priority:** P1
- **Effort:** S
- **Outcome:** Dependabot configuration becomes trustworthy and easier to maintain.
- **Scope:**
  - Remove duplicate update blocks.
  - Align referenced directories with the actual repo layout.
  - Reuse metadata validation in CI.
- **Why now:** Dependency automation should generate signal, not noise.
- **Definition of done:** Each Dependabot entry is intentional, unique, and validated.

### MW-007 — Add a tooling architecture document
- **Priority:** P2
- **Effort:** M
- **Outcome:** Contributors can understand the toolchain as a system instead of a list of commands.
- **Scope:**
  - Document command layering, ownership, generated artifacts, and local-to-CI mapping.
  - Clarify which commands are authoritative versus convenience aliases.
  - Link from developer onboarding docs.
- **Why now:** The repo has enough automation surface area that narrative architecture guidance now pays for itself.
- **Definition of done:** `docs/development/tooling-architecture.md` exists and is linked from the developer docs index.

### MW-008 — Ratchet global warning suppressions
- **Priority:** P2
- **Effort:** L
- **Outcome:** Warning policy becomes visible, owned, and incremental.
- **Scope:**
  - Inventory each suppression in `Directory.Build.props`.
  - Add justification/owner notes.
  - Move suppressions to project level where possible.
  - Add warning-count reporting in CI.
- **Why now:** Broad suppression hides real upgrade and maintenance costs.
- **Definition of done:** Each suppression category has an owner and a plan, and new suppressions require explicit review.

### MW-009 — Reframe AI automation into required vs advisory lanes
- **Priority:** P2
- **Effort:** M
- **Outcome:** Contributors can quickly tell what is blocking versus optional.
- **Scope:**
  - Classify AI-related Make targets and workflows into required quality gates, advisory tooling, and maintenance/reporting.
  - Reflect that split in docs and help output.
- **Why now:** The current AI surface area is useful, but cognitively expensive.
- **Definition of done:** AI tooling is grouped by role and documented as such.

---

## Suggested GitHub Issue Titles

- `tooling: canonicalize Node entrypoints used by make and npm`
- `tooling: add metadata contract tests for make/package/dependabot paths`
- `docs: generate workflow inventory instead of hand-maintaining counts`
- `build: split root Makefile into modular include files`
- `ci: introduce blessed verification lanes shared by local and CI`
- `deps: remove Dependabot duplication and validate configured paths`
- `docs: add tooling architecture guide for contributors`
- `build: ratchet global warning suppressions by category and owner`
- `ai: separate required automation gates from advisory tooling`

---

## Success Metrics

Track whether the cleanup is working by measuring:

- Number of broken path references caught by metadata validation before merge.
- Number of docs that embed hand-maintained workflow counts.
- Number of CI jobs and local commands that already map to blessed lanes.
- Warning counts by suppression category over time.
- Time-to-fix for small tooling regressions reported by contributors.

---

## Related Files

- [`Makefile`](https://github.com/rodoHasArrived/Meridian/blob/main/Makefile)
- [`package.json`](https://github.com/rodoHasArrived/Meridian/blob/main/package.json)
- [`.github/dependabot.yml`](https://github.com/rodoHasArrived/Meridian/blob/main/.github/dependabot.yml)
- [`.github/workflows/README.md`](https://github.com/rodoHasArrived/Meridian/blob/main/.github/workflows/README.md)
- [`README.md`](https://github.com/rodoHasArrived/Meridian/blob/main/README.md)
- [`Directory.Build.props`](https://github.com/rodoHasArrived/Meridian/blob/main/Directory.Build.props)
