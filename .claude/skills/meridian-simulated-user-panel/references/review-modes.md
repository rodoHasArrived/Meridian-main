# Review Modes

Choose an invocation mode for why the review runs and focus areas for what the panel emphasizes.

## Invocation Modes

| Mode | Use when | Evidence minimum | Recommendation |
| --- | --- | --- | --- |
| `design_partner` | Early critique, roadmap shaping, and product-direction review | One accessible artifact plus explicit constraints | `steer`, `prototype`, or `defer` |
| `release_gate` | A feature is near shipping and needs advisory user-fit evidence | Current functional evidence plus success criteria | `ship`, `ship_with_caveats`, or `hold` |
| `usability_lab` | Repeatable comparison or quality-drift tracking | Stable manifest version and comparable artifact bundle | `advance_to_release_gate`, `rerun_after_changes`, or `defer` |

Default: `design_partner`.

## Focus Areas

| Focus area | Emphasis |
| --- | --- |
| `first_impression` | Purpose, hierarchy, first action, and onboarding clarity |
| `workflow_fit` | Core job completion, state, handoffs, and cadence |
| `trust_and_controls` | Approvals, auditability, policy, and explainability |
| `power_user_depth` | Dense workflows, exports, research, and automation leverage |
| `adoption_and_positioning` | Audience fit, support burden, and differentiation |
| `release_readiness` | Blockers, recovery, validation, and operational confidence |
| `accessibility_and_inclusion` | Keyboard, semantics, contrast, cognitive load, and accessible alternatives |
| `failure_recovery` | Error visibility, ownership, retry, rollback, and continuity |
| `evidence_traceability` | Source, freshness, lineage, evidence, and reproducibility |
| `role_permissions` | Entitlements, segregation of duties, and scoped actions |
| `cross_surface_parity` | Shared product state and workflow parity across browser and WPF |
| `supportability` | Diagnostics, teachability, prerequisites, and likely support load |

## Artifact Defaults

| Artifact type | Best mode | Default focus |
| --- | --- | --- |
| `screen-review` | `design_partner` | `first_impression`, `workflow_fit` |
| `workflow-walkthrough` | `release_gate` or `usability_lab` | `workflow_fit`, `failure_recovery`, `trust_and_controls` |
| `roadmap-review` | `design_partner` | `adoption_and_positioning`, `power_user_depth` |
| `ship-readiness` | `release_gate` | `release_readiness`, `trust_and_controls` |
| `cross-surface-review` | `design_partner` or `usability_lab` | `cross_surface_parity`, `workflow_fit`, `supportability` |

Use `hold` when a release-gate bundle lacks current functional evidence. Do not use a visually
complete screenshot as proof that a workflow succeeds, persists, authorizes, or recovers correctly.
