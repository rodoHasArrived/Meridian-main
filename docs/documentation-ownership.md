# Documentation Ownership Contract

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-05

This contract defines where Meridian documentation belongs in the current model and during the
remaining compatibility-lane cleanup.

## Ownership Model

| Documentation type | Owner location | Rule |
| --- | --- | --- |
| First-run orientation | `docs/start/` | Keep short, current, and command-oriented. |
| Stakeholder/product narrative | `docs/product/` | Explain product direction, capability posture, and roadmap interpretation without duplicating registry truth. |
| Developer and agent workflow | `docs/engineering/` and `docs/ai/` | Engineering owns contributor entrypoints and the shortest current build/test/run and validation path; AI owns assistant contracts. Link detailed techniques from their supporting owners. |
| Architecture and domain guidance | `docs/architecture/`, `docs/domain/`, and `docs/adr/` | Keep current system design, MDIF context, business vocabulary, and active decisions explicit and linked from Engineering. |
| Operator procedures | `docs/operators/` | Link to setup, provider, runbook, deployment, troubleshooting, and support procedures. |
| Lookup material | `docs/reference/` | Keep APIs, environment variables, CLI flags, schemas, glossary, and matrices discoverable. |
| Roadmap truth | `docs/roadmap/data/*.yml` | Validate and render; do not hand-author competing durable roadmap truth. |
| Source-module truth | `docs/source/data/*.yml` and registered `src/**/README.md` | Update with source behavior, ownership, validation, TODO, and diagram changes. |
| Generated output | `docs/generated/`, `docs/roadmap/generated/`, `docs/source/generated/`, `docs/ai/generated/`, `docs/ai/exports/`, `docs/reference/source-api/` | Update generators or inputs, not emitted files by hand. |
| Automation-owned status output | `docs/status/` | Keep generated reports and compatibility artifacts at the paths consumed by tooling; do not create competing roadmap truth. |
| Detailed supporting guidance | `docs/development/`, `docs/security/`, `docs/testing/`, `docs/integrations/`, and related specialist folders | Development holds active implementation, extension, testing, and tooling guides reached from Engineering. Link specialist material from its canonical owner and avoid duplicating entrypoint commands or product status. |
| Transitional compatibility paths | `docs/operations/` and `docs/plans/` | Retain only when active code, tests, tooling, or strong links require the path; do not add new durable guidance. |
| Historical or superseded material | `archive/docs/` | Preserve useful history with replacement links or explicit archive rationale. |

## Placement Classes

- `canonical`: active audience, architecture, domain, AI, roadmap, source, or generated-system owner.
- `supporting`: active specialist material linked from a canonical owner.
- `transitional`: compatibility content retained because active tools, tests, or strong links still
  require its path; do not add new durable guidance.
- `source-material`: historical facts awaiting extraction into a canonical or supporting owner.
- `generated`: preserve the output and update its registry input or generator only.
- `archived`: retain under `archive/docs/` with replacement links where useful.
- `delete-candidate`: use only for untracked generated junk or explicitly approved removals.

## Source-Of-Truth Rules

- Do not keep two active docs claiming the same durable truth.
- Prefer registry data and generated views for roadmap and source-module status.
- Prefer root shims for `README.md`, `AGENTS.md`, and `CLAUDE.md`; avoid long duplicated command catalogs.
- Keep `docs/README.md` as the canonical documentation front door.
- Keep AI workflow rules synchronized through `docs/ai/assistant-workflow-contract.md` and assistant-specific indexes.
- Use repository-relative paths in committed docs. Machine-specific checkout paths belong in local
  environment instructions, not portable project guidance.

Placement and lifecycle are separate: `supporting` describes where a guide fits, not whether it is
obsolete. Mark maintained guidance `active`, proposals `draft`, replacement-linked compatibility
guidance `deprecated`, and historical records `archived`. Preserve the evidence date and commit in
historical reviews. Keep these meanings consistent in the document header and in index link labels.

Use the [Documentation Contribution Guide](development/documentation-contribution-guide.md) for
writing and lifecycle conventions, and the [Operator Runbook Template](operators/runbook-template.md)
for executable procedures.

## Review Rules

Before adding or moving docs:

1. Choose the owning lane from the table above.
2. Check whether a registry or generator already owns the truth.
3. Add or update the nearest `README.md` only when discoverability changes.
4. Archive superseded material instead of deleting it when it has historical or evidence value.
5. Run the narrowest docs validation command for the touched surface.

## Structure Enforcement

`build/scripts/docs/validate-docs-structure.py` enforces the current documentation model:

- Canonical top-level docs folders are `ai`, `architecture`, `domain`, `engineering`, `generated`,
  `operators`, `product`, `reference`, `roadmap`, `source`, and `start`.
- Supporting folders are allowed without migration warnings when their README routes readers to the
  canonical owner and explains any automation contract.
- `operations` and `plans` are transitional and continue to warn until their strong references and
  tooling contracts are retired or moved.
- Removed or unexpected top-level folders are errors. Add active documentation to a canonical or
  supporting lane, or archive historical material under `archive/docs/`.
- The validator still warns on missing lifecycle fields so legacy files can be cleaned up in batches instead of blocking unrelated slices.

### Historical reviews and evidence placement

Keep large dated review records and loose run/audit JSON snapshots under `archive/docs/`,
with a brief pointer from the active owner when needed. Current design, remediation plans and
registry status stay in their canonical lanes. The [archive index](../archive/docs/README.md)
records retained review and measurement history; archival does not resolve a finding.

Move related snapshot inputs, diagnostic policies and compressed reports together, preserve
JSON bytes and evidence dates, and update reproduction commands. Keep current generator-owned
reports at their tooling paths. Integrity-bound evidence packets require a separate coordinated
migration of registered paths and navigation; do not split a packet or rewrite checksums merely
to reduce the active documentation tree.

### Immutable evidence documents

The structure validator expands only the exact Markdown entries listed by the registered
`docs/testing/evidence/wal-checksum-2026-10-07/manifest.json`. It verifies that manifest's checksum
and each listed Markdown file's size and SHA-256 before classifying the file as an immutable
snapshot. It reports these separately from generated output and maintained guides. Unlisted files
in the same packet still need lifecycle metadata; checksum drift or missing evidence is an error
even without `--strict`. Preserve snapshot bytes and original evidence dates instead of adding
current review headers to them. Run the packet's `verify-evidence.py` for its full integrity check.

## Retained binary evidence

New PNG revisions under `docs/diagrams`, `docs/screenshots`, and `docs/testing/evidence`
use the `.gitattributes` Git LFS filters. Install Git LFS and run `git lfs install` before
adding or updating those artifacts. Preserve textual source diagrams and evidence manifests
in ordinary Git, including the capture commit, date, and hashes. Generated status reports
are refreshed through their owning commands; changing a report date alone is not refresh.

The 2026-11-12 source inventory contained 338 tracked PNGs (97.14 MiB). Current-tip PNGs
in the three scoped folders were converted to LFS pointers; their working-tree bytes and
local LFS object SHA-256 hashes were verified and recorded in
`docs/testing/evidence/review-remediation-2026-11-12/lfs-migration.json`. Upload the LFS
objects when publishing the branch and verify a fresh LFS-enabled clone. No LFS objects
were uploaded during local remediation. Existing history remains intact. A separate
owner-coordinated historical migration can run `git lfs migrate import --include="docs/diagrams/**/*.png,docs/screenshots/**/*.png,docs/testing/evidence/**/*.png"`
after arranging LFS hosting, backup, and fresh-clone verification. This rewrites history and
requires coordination with all consumers; it is not part of routine remediation. A new LFS
rule cannot remove blobs already present in historical commits. Retained evidence must not
be deleted merely to reduce the working tree or clone size.

Synchronization with upstream `63e84bb978b5a8b8cffbce3820b6842aa907423f` retains 329 scoped PNGs
(90,112,988 bytes). The upstream removal of 11 duplicate UI diagrams is preserved, and two new
upstream screenshots are included. All retained image bytes match the newest upstream version;
the inventory's `upstreamSynchronization` section records their verified local LFS hashes
without rewriting the original 338-image capture. Remote LFS upload remains a publication step.
