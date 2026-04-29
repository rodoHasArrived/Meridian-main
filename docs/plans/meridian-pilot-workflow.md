# Meridian Pilot Workflow

**Last Updated:** 2026-04-29
**Status:** Golden-path productization filter for the active Waves 2-4 operator-readiness path

---

## Purpose

This document defines the one pilot workflow Meridian should optimize first. It does not replace
the canonical roadmap in [`../status/ROADMAP.md`](../status/ROADMAP.md). It is the release filter
for deciding whether new code, UI, providers, reports, or documentation improve the product path
that matters most.

The product emphasis is:

> Evidence-backed investment operations for research-to-ledger-to-reporting continuity.

Meridian already has broad platform foundations. The next highest-return work is to make one
operator lifecycle clear, dependable, and defensible before expanding sideways. Commercially,
that lifecycle should establish Meridian as the system of record for investment decision evidence.

---

## Golden Path

```text
Trusted data
-> research run
-> run comparison
-> paper promotion
-> paper session
-> portfolio / ledger review
-> reconciliation
-> governed report pack
```

Every release should improve at least one stage in this path, unblock a later stage, or reduce
scope that distracts from it.

The commercial name for this path is the **Meridian Assurance Loop**:

```text
Data Trust Passport
-> Run Evidence Graph
-> Promotion Passport
-> Accounting-Grade Paper Trading
-> Governed Report Pack
```

Those names are roadmap targets, not current completion claims. Implementation should land shared
contracts and retained evidence first, then expose the workflow through the browser dashboard and
retained support surfaces.

The accounting-led extension of the same path is: prove, book, reconcile, approve, and report the
investment decision. Books Before Broker, Transaction Lab, Close the Books, broker statement
reconciliation, controls, and evidence packet actions are useful names for future slices, but they
remain planned until shared contracts, durable evidence, and browser-visible workflows exist.

The umbrella commercial name for the evidence layer is **Meridian Evidence OS**. In this pilot
workflow it means every artifact, decision, blocker, and report output should eventually link back
to a governed evidence subject; it does not create a new wave or imply an implemented evidence
platform.

---

## Workspace Mapping

The current operator shell has seven visible top-level workspaces. The pilot workflow should make
each workspace answer where the operator is in the lifecycle, what evidence is trusted, what is
blocked, and what the next action is.

| Workspace | Pilot responsibility | Primary questions |
| --- | --- | --- |
| `Data` | Trusted provider, dataset, storage, and validation evidence | Is the input data trusted enough to run and promote? |
| `Strategy` | Research runs, comparisons, QuantScript handoffs, and promotion review packets | Which run is the candidate and why? |
| `Trading` | Paper promotion, paper session, replay verification, controls, and broker-shaped evidence | Is the paper workflow restart-safe and auditable? |
| `Portfolio` | Positions, exposure, attribution, account posture, and balance evidence | What portfolio state did the run or session create? |
| `Accounting` | Ledger, cash-flow, trial balance, reconciliation, and sign-off casework | Does accounting evidence match the portfolio story? |
| `Reporting` | Governed report-pack previews, generated artifacts, approvals, and restatements | What output can be reviewed, approved, and retained? |
| `Settings` | Credentials, provider setup, storage roots, evidence paths, and environment posture | Can the workflow be reproduced in this environment? |

Legacy planning terms such as `Research`, `Data Operations`, and `Governance` remain useful
grouping language, but the visible product path should be expressed through the seven-workspace
operator shell.

---

## Stage Gates

| Stage | Required evidence | Exit signal |
| --- | --- | --- |
| Trusted data | Provider validation posture, dataset coverage, freshness, checkpoint proof, and DK1 packet/sign-off state when provider trust is in scope | The pilot symbols and date range are backed by reproducible data evidence. |
| Research run | Run ID, dataset snapshot, parameters, engine, result summary, fills, metrics, and warnings | A candidate run can be reconstructed and compared from retained evidence. |
| Run comparison | Baseline run, candidate run, metric deltas, drawdown/risk deltas, and rejected alternatives | The operator can explain why one run is eligible for paper promotion. |
| Paper promotion | Promotion checklist, approver, rationale, override state, risk/control evidence, and audit references | The `Backtest -> Paper` decision survives restart and remains explainable. |
| Paper session | Session identity, orders, fills, positions, ledger entries, replay audit, and stale-replay diagnostics | Session state can be persisted, restored, replayed, and verified against counts. |
| Portfolio / ledger review | Portfolio state, account posture, ledger journals, trial balance, cash-flow view, and continuity warnings | Portfolio and accounting views tell the same run/session story. |
| Reconciliation | Break queue, tolerance profile, exception route, owner, status, sign-off role, and audit trail | Open breaks are visible as casework with owner, reason, and next action. |
| Governed report pack | Report definition, period, artifact version, lineage links, approval state, and restatement posture | A retained report pack can prove which evidence and decisions it summarizes. |

---

## Sellable Module Mapping

| Product package | Pilot stages | Current posture |
| --- | --- | --- |
| Meridian Core | Trusted data, Security Master, account/entity context, portfolio posture, audit trail direction | Strong provider, Security Master, storage, and account support evidence exists; commercial package boundaries remain planning language. |
| Meridian Research Assurance | Trusted data, research run, run comparison, promotion readiness | Support evidence exists; Data Trust Passport and Run Evidence Graph remain planned projections. |
| Meridian PaperOps | Paper promotion, paper session, replay verification, Books Before Broker, paper books direction | Partial support exists through readiness/replay/audit metadata; accounting-grade paper books and accounting-impact previews remain open. |
| Meridian FundOps | Portfolio / ledger review, Transaction Lab, reconciliation, statement reconciliation, close workflow, report-pack inputs | Partial support exists through ledger, reconciliation, and account posture seams; durable casework, statement import, insurance accounting, shadow books, and close workflow remain open. |
| Meridian Controls | Approval history, promotion rationale, policy controls, audit events, governed report pack | Partial support exists through approval checklist and report-pack checks; Controls-as-Code, policy mapping, evidence vault, restatement tracker, and explorer modules remain planned. |
| Meridian Command Center | Readiness console, buyer demo mode, role-based demo views, blocker queue, report-pack readiness, operational health | Partial browser support exists through the read-only Operator Readiness Console; SLA aging, role-based demos, evidence packet actions, and full command-center scope remain planned. |

Future implementation should favor shared-contract additions such as `EvidenceCompletenessSummary`, Run Evidence Graph lineage, Promotion/Data Trust Passport projections, accounting-impact previews, close checklist/readiness, statement-import reconciliation cases, Security Master confidence, report restatement tracking, controls-as-code policy summaries, evidence packet readiness, reconciliation casework, and governed report-pack readiness before expanding client-specific UI.

Evidence OS follow-ons should use the same rule: model evidence bundle identity, proof/certificate projections, report-line provenance, strategy-to-ledger lineage, instrument passport projections, close-readiness scoring, break explanation summaries, evidence SLA freshness, decision memory, no-orphan-evidence validation, and Evidence Vault artifact metadata in shared contracts before adding screen-specific workflow.

---

## Release Rules

1. Do not add broad provider, broker, or page scope unless it directly strengthens the pilot path.
2. Put workflow decisions in shared services and contracts before exposing them in the web dashboard or retained WPF surfaces.
3. Keep the web dashboard as the active operator UI lane, but do not make browser-only state the product boundary; retained WPF state remains compatibility and support evidence only.
4. Prefer generated readiness dashboards over repeated manual status prose when a claim depends on tests, artifacts, or source metadata.
5. Treat report packs as product artifacts, not only export files: they need lineage, approval state, and restatement posture.
6. Treat reconciliation as casework: owner, status, tolerance, decision, sign-off, and audit history are part of the workflow.
7. Keep live-readiness and additional broker scope behind trusted data, dependable paper replay, and explicit promotion evidence.

---

## First Pilot Scenario

The first canonical scenario should be deterministic and narrow:

1. Use the active Wave 1 provider boundary and a small pilot symbol/date set.
2. Produce one trusted dataset snapshot with coverage and freshness evidence.
3. Run one baseline strategy and one candidate strategy.
4. Compare the candidate against the baseline and record the promotion rationale.
5. Promote the candidate into a paper session.
6. Generate orders and fills through the paper path.
7. Restart and replay the session.
8. Verify order, fill, position, and ledger counts.
9. Open portfolio, ledger, cash-flow, and reconciliation views from the same run/session context.
10. Resolve or sign off the pilot reconciliation breaks.
11. Generate a governed report pack that links back to the run, session, ledger, reconciliation, and approvals.

The scenario should fail loudly if any stage has only display state and no retained evidence.

---

## Near-Term Implementation Order

1. **Pilot fixture and acceptance harness:** one repeatable dataset, strategy pair, paper session, restart/replay verification, reconciliation queue, and report-pack output.
2. **Run Evidence Graph skeleton:** a shared evidence model that links provider evidence, dataset snapshots, run parameters, fills, orders, positions, ledger entries, reconciliation breaks, promotion decisions, report artifacts, and audit events.
3. **Generated readiness dashboard:** one dashboard that reports pilot-stage readiness, blockers, source evidence, and the validation command or artifact proving the current state.
4. **Paper-session health and mismatch diagnostics:** operator-visible stale replay, count divergence, and replay mismatch reasons.
5. **Governed report-pack MVP:** retained report artifact with period, lineage, approval state, and source evidence references.
6. **Reconciliation casework MVP:** durable case status, ownership, tolerance/sign-off metadata, decision rationale, and audit trail.
7. **Read-only operator readiness console:** local web support surface for readiness, blockers, evidence, and report-pack posture after the shared contracts are stable.

---

## Scope Control

Promote into the core path:

- Yahoo or deterministic historical data for repeatable research fixtures
- Alpaca/Robinhood/Yahoo evidence only where the active trust gate requires it
- paper-session persistence, replay, audit, and promotion traceability
- shared run / portfolio / ledger / reconciliation continuity
- Security Master-backed accounting and reporting evidence
- governed report packs with retained lineage

Demote, hide, or archive until the pilot path needs them:

- speculative providers without current validation evidence
- broad live-trading claims
- duplicate broker abstractions
- orphan workstation pages that do not expose lifecycle stage, evidence, blocker, or next action
- dashboards that restate status without test, artifact, or source backing
- WPF-only workflow logic that cannot be consumed through shared contracts

---

## Product Exit Signal

The pilot is credible when a reviewer can watch one scenario move from trusted data to governed
report pack and answer five questions from retained evidence:

1. What data was trusted, and why?
2. Which run was promoted, and why?
3. Did the paper session survive restart and replay verification?
4. Do portfolio, ledger, cash-flow, and reconciliation agree?
5. Which report pack was generated, approved, and retained?

Until those questions are easy to answer, more feature breadth is lower ROI than improving the
pilot path.
