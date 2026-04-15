# Review Modes

Treat the simulated-user-panel system as two layers:

1. **Invocation mode** — why the review is being run now
2. **Focus areas** — what lens the personas should emphasize

## Invocation Modes

| Mode | Use when | Expected recommendation style |
|---|---|---|
| `design_partner` | Early critique, roadmap shaping, surface reviews, and owner-level product direction | steer, refine, prototype, or defer |
| `release_gate` | The feature is near shipping and needs explicit launch confidence | ship, ship_with_caveats, or hold |
| `usability_lab` | You want repeatable comparison, trend tracking, or benchmarkable persona output | compare against prior runs and cluster repeated complaints |

Default: `design_partner`

## Focus Areas

| Focus area | Use when | Best personas |
|---|---|---|
| `first_impression` | screenshots, demos, onboarding, or first-look UI critique | Hobbyist Builder, Individual Trader, Support / Onboarding Lead |
| `workflow_fit` | daily-use screens and job completion | Fund Manager, Fund Operations Lead, Quantitative Analyst |
| `trust_and_controls` | accounting, governance, lineage, approvals, and auditability | Fund Accountant, Risk / Compliance Lead, Data Operations Manager |
| `power_user_depth` | research, export, scripting, and dense operator workflows | Quantitative Analyst, Academic Researcher, Data Engineer |
| `adoption_and_positioning` | roadmap, packaging, audience fit, and differentiation | Owner-Operator, Hobbyist Builder, Implementation Consultant |
| `release_readiness` | near-ship launch risk and delight analysis | Owner-Operator plus 3 role-specific personas |

## Artifact-Type Defaults

| Artifact type | Best invocation mode | Best focus areas |
|---|---|---|
| `screen-review` | `design_partner` | `first_impression`, `workflow_fit` |
| `workflow-walkthrough` | `release_gate` or `usability_lab` | `workflow_fit`, `trust_and_controls` |
| `roadmap-review` | `design_partner` | `adoption_and_positioning`, `power_user_depth` |
| `ship-readiness` | `release_gate` | `release_readiness`, `trust_and_controls` |

## Selection Heuristics

- If the evidence is mostly screenshots or static UI, start with `screen-review`.
- If the evidence includes manifests, step notes, and smoke outputs, prefer
  `workflow-walkthrough`.
- If the target is a concept, blueprint, or roadmap item, use `roadmap-review`.
- If the user asks "is this ready?", use `ship-readiness` with `release_gate`.
- If the user wants cross-run comparison or trend tracking, switch to `usability_lab`.
