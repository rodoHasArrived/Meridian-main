<!--
generated: true
generator: build/scripts/docs/render-roadmap-docs.py
generator_version: 1.0.0
render_contract: meridian.generated-docs.v1
schema_versions:
  - meridian.roadmap-items@1.0.0
inputs:
  - docs/roadmap/data/decision-log.yml
  - docs/roadmap/data/document-index.yml
  - docs/roadmap/data/program-state.yml
  - docs/roadmap/data/risk-register.yml
  - docs/roadmap/data/roadmap-items.yml
  - docs/roadmap/data/stage-gates.yml
do_not_edit: true
-->

# Roadmap Register

Snapshot date: 2026-05-20

## W1-DATA-001 - Provider trust gate and data confidence baseline
| Field | Value |
| --- | --- |
| Wave | W1 |
| Status | done |
| Health | green |
| Priority | critical |
| Owner lane | Data Confidence and Validation |
| Evidence posture | complete |
| Last reviewed | 2026-05-20 |

### Current Summary

Provider validation packets and DK1 operator sign-off are the baseline evidence for trusted data operations.

### Exit Criteria

- Provider parity packet exists and is linked from status documentation.
- Operator sign-off evidence is available for DK1 readiness.
- Provider validation matrix remains the readable front door.

### Source Modules

- `SRC-HOST`
- `SRC-APP`
- `SRC-CONTRACTS`

## W2-PROMO-001 - Paper promotion evidence and operator acceptance
| Field | Value |
| --- | --- |
| Wave | W2 |
| Status | planned |
| Health | yellow |
| Priority | high |
| Owner lane | Execution and Fund Accounts |
| Evidence posture | support_evidence |
| Last reviewed | 2026-05-20 |

### Current Summary

Promotion review must connect research lineage, paper-session evidence, and operator approval in one auditable path.

### Exit Criteria

- Promotion candidates show evidence lineage before acceptance.
- Operator approval records link to the paper-session context.
- Follow-up TODOs are registry-backed and assigned.

### Source Modules

- `SRC-APP`
- `SRC-CONTRACTS`
- `SRC-UI-SERVICES`
- `SRC-UI-DASHBOARD`

## W2-TRD-001 - Paper trading cockpit reliability
| Field | Value |
| --- | --- |
| Wave | W2 |
| Status | in_progress |
| Health | yellow |
| Priority | critical |
| Owner lane | Execution and Fund Accounts |
| Evidence posture | support_evidence |
| Last reviewed | 2026-05-20 |

### Current Summary

Trading readiness, operator inbox routing, and replay durability are the active cockpit-readiness concerns.

### Exit Criteria

- Trading readiness endpoint works for global and account-scoped checks.
- Operator inbox exposes actionable readiness and reconciliation routing.
- Paper session replay verification remains durable across restart.
- Browser workstation shows the readiness posture through the active web UI lane.

### Source Modules

- `SRC-HOST`
- `SRC-APP`
- `SRC-CONTRACTS`
- `SRC-UI-SERVICES`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`

## W3-CONT-001 - Research to paper continuity
| Field | Value |
| --- | --- |
| Wave | W3 |
| Status | planned |
| Health | green |
| Priority | high |
| Owner lane | Strategy and Research |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-05-20 |

### Current Summary

Strategy research outputs need traceable handoff into paper-session validation and promotion review.

### Exit Criteria

- Research lineage persists through paper-session handoff.
- Strategy run evidence links to promotion candidates.
- Validation commands are documented in source READMEs.

### Source Modules

- `SRC-APP`
- `SRC-CONTRACTS`
- `SRC-UI-SERVICES`
- `SRC-UI-DASHBOARD`

## W4-RECON-001 - Portfolio ledger reconciliation readiness
| Field | Value |
| --- | --- |
| Wave | W4 |
| Status | planned |
| Health | yellow |
| Priority | high |
| Owner lane | Governance and Ledger |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-05-20 |

### Current Summary

Ledger and reconciliation workflows need evidence-linked acceptance before governed reporting can claim readiness.

### Exit Criteria

- Reconciliation queue actions link to ledger evidence.
- Break resolution and sign-off states are operator-visible.
- Shared contracts remain compatible across browser and WPF workstation surfaces.

### Source Modules

- `SRC-CONTRACTS`
- `SRC-UI-SERVICES`
- `SRC-UI-SHARED`
- `SRC-WPF`

## W4-RPT-001 - Governed report pack readiness
| Field | Value |
| --- | --- |
| Wave | W4 |
| Status | planned |
| Health | yellow |
| Priority | high |
| Owner lane | Governance and Ledger |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-05-20 |

### Current Summary

Report-pack generation needs approval, export evidence, and clear ownership before being treated as governed output.

### Exit Criteria

- Report-pack lifecycle includes approval evidence.
- Export output records are linked to source data and acceptance status.
- Documentation states the operator value and validation commands.

### Source Modules

- `SRC-CONTRACTS`
- `SRC-UI-SERVICES`
- `SRC-UI-SHARED`
- `SRC-WPF`

## W5-BTSTUDIO-001 - Backtesting studio evidence loop
| Field | Value |
| --- | --- |
| Wave | W5 |
| Status | planned |
| Health | green |
| Priority | medium |
| Owner lane | Strategy and Research |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-05-20 |

### Current Summary

Backtesting studio work remains downstream of paper-session and research continuity readiness.

### Exit Criteria

- Backtest result evidence links to strategy lineage.
- Operator-facing acceptance criteria are checklist-backed.
- Source READMEs explain module ownership and test lanes.

### Source Modules

- `SRC-APP`
- `SRC-CONTRACTS`

## W6-LIVE-001 - Live-readiness governance
| Field | Value |
| --- | --- |
| Wave | W6 |
| Status | planned |
| Health | green |
| Priority | medium |
| Owner lane | Governance and Ledger |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-05-20 |

### Current Summary

Live-readiness remains gated by trusted data, paper validation, reconciliation, and governed reporting evidence.

### Exit Criteria

- Live action surfaces remain paper-first until acceptance gates are green.
- Credential and provider checks stay secret-safe and read-only by default.
- Governance sign-off is linked before any live-readiness claim.

### Source Modules

- `SRC-HOST`
- `SRC-APP`
- `SRC-CONTRACTS`
- `SRC-UI-DASHBOARD`
