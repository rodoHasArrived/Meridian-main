# docs/ — developer documentation

| Doc | Owns |
| --- | --- |
| `AUDIT_4_3_0.md` | Current PR #3501 export audit: source census, original gate failures, operating-contract coverage, and integration boundaries |
| `EXPORT_4_3_0.md` | Reproducible build/export, archive verification, required assets, and dependency handoff |
| `ARCHITECTURE.md` | compile model, layer/dependency rules, component anatomy, quality gates |
| `GETTING_STARTED.md` | load the bundle, mount components, consume tokens |
| `COOKBOOK.md` | assembly recipes — which components, in what order, for five common screens |
| `WORKSTATION_BLUEPRINT.md` | v2.0 composition law for a workstation screen — archetype, work plane, surface hierarchy, data states, quality gate (card: `workstation-blueprint.card.html`) |
| `COORDINATED_COMPONENTS.md` | 3.1 state ownership, compound component map, reference compositions, responsive and performance contracts |
| `RELEASE_NOTES_<version>.md` | per-release narrative — what shipped and why, what was deferred (latest: `RELEASE_NOTES_4.3.0.md`) |
| `QA_REPORT_<version>.md` | per-release evidence — what was run, what was inherited, what is not claimed (latest: `QA_REPORT_4.3.0.md`; `release.json › qaReport` names the one in force) |
| `COMPONENT_API_REFERENCE.card.html` | live API reference story |
| `PERFORMANCE.md` | what a consumer pays — measured payload, CSS injection behavior |
| `UPGRADING.md` | per-release consumer migration notes |
| `CONTRACT_AUDIT.md` | prop-contract audit across the inventory |
| `UX_DEPTH_4_1.md` | **proposal, not law** — six tracks deepening the layer around the decision (continuity, in-flight state, attention, keyboard, annotation, coverage debt). Card: *UX depth proposal* |
| `ACCOUNTING_INTERFACE_4_3.md` | **proposal, not law** — assurance as carried state (O-4); shipped in 4.3 |
| `ACCOUNTING_INTERFACE_4_2.md` | **proposal, not law** — reorganizing the books around scope and obligation: the books spine (period × basis × entity), account as destination, the evidence/consequence archetype, six carried-state concepts. Card: *Books interface proposal* |
| `reviews/` | archived audit artifacts — the 2026-08 adversarial review and template-improvements proposal (remediated in 1.24–1.25; kept as record, not live law) |

Design references live in `../guidelines/`. Contribution checklists: `../CONTRIBUTING.md`. Version history: `../CHANGELOG.md`.
