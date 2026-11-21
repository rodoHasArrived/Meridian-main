# Roadmap Schema Versioning

Every roadmap registry starts with:

```yaml
schema:
  id: meridian.<registry-name>
  version: "1.0.0"
  minimum_renderer_version: "1.0.0"
```

## Version rules

| Change | Version impact |
| --- | --- |
| Clarify description text | Patch |
| Add optional field | Minor |
| Add enum value | Minor |
| Add required field | Major |
| Rename or remove a field | Major |
| Change status meaning | Major |
| Change generated output semantics | Major or renderer major |

Validators must fail on unknown schema IDs, unsupported major versions, duplicate IDs, broken references, and accepted or done work without evidence.

## Structured status migration (2026-11-14)

The canonical roadmap-items and program-state registries use **2.0.0** and require
roadmap renderer **3.0.0**. The v1 contracts remain available for legacy tooling;
status meanings, sequence ranks, evidence posture and acceptance authority are unchanged.
The generated program summary uses `program-state-summary/v4`.

- Roadmap `current_summary` is replaced by `status_note` (320 characters),
  `decision_ids`, `pr_references` and a `status_detail` repository path. Titles are
  capped at 180 characters and each exit criterion/blocker at 1,000 characters.
- Program `active_scope` holds roadmap identifiers, decision identifiers, the
  explicitly ordered work queue and a retained-detail link. Generated counts use
  current item statuses. The queue changes priority only; it adds no dependency.
- Runtime validation loads the v2 JSON Schemas, enforces prose limits and rejects
  unknown fields, unknown decision/item references and missing detail files.
- [Per-item migration snapshots](snapshots/status-prose-2026-11-14/) retain the
  original summary scalar source verbatim, including YAML comments. The program
  scope snapshot retains its complete former prose. These snapshots preserve
  reservations and historical evidence; migration supplies no new verification.
- `production-readiness.yml` has its own v1 contract. Its generated tracker keeps
  delivery posture separate from release completion; the same-release-commit P0
  gate remains intact. Its linked migration snapshot retains full detail.

Write durable status once in structured fields. Keep detailed technical evidence
in the linked owner document or scoped evidence record; do not grow a new prose
log in `status_note`. Generated narratives and compatibility pages derive from
these fields. Decision and PR references are citations, not inferred acceptance.
