# Contract Compatibility Matrix

Last reviewed: 2026-04-29
Scope: workstation contracts and shared service/ledger interfaces consumed by workstation APIs.

This matrix defines compatibility commitments for:

- `src/Meridian.Contracts/Api/UiApiRoutes.cs`
- `src/Meridian.Contracts/Workstation/`
- `src/Meridian.Strategies/Services/`
- `src/Meridian.Ledger/`
- workstation endpoint payloads in `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs`

## Versioning Baseline

| Surface | Current contract baseline | Version signal | Owner |
| --- | ---: | --- | --- |
| `Meridian.Contracts.Api.UiApiRoutes` route constants | `v1` | route string + parameter-name compatibility | API + UI shared maintainers |
| `Meridian.Contracts.Workstation` DTOs | `v1` | additive DTO evolution + release notes | Application + UI shared maintainers |
| `Meridian.Strategies.Services` service contracts | `v1` | interface/member compatibility + migration notes | Strategies maintainers |
| `Meridian.Ledger` domain contracts | `v1` | public type/member compatibility + migration notes | Ledger maintainers |
| `WorkstationEndpoints` response/request payload contracts | `v1` | route + request/response shape compatibility | UI shared maintainers |

## Compatibility Rules

### Backward compatibility (required by default)

1. **Do not remove or rename public DTO/service/ledger members** in a minor release.
2. **Do not remove positional record constructor parameters** from scoped DTOs without a deprecation period, even when the generated property remains source-compatible for some callers.
3. **Do not remove enum members** from scoped DTOs or shared contracts without a deprecation period and migration notes.
4. **Do not remove workstation routes or change route parameter names** without a deprecation period.
5. **Additive-only payload changes** (new nullable/optional fields) are allowed in `v1`.
6. **Enum additions** are allowed only when callers can safely ignore unknown values.
7. **Service interface additions** must be additive and must not break existing implementations in the same release wave.

### Forward compatibility (required for clients lagging one release)

1. Clients should tolerate unknown JSON fields and preserve defaults for missing optional fields.
2. New request fields must be optional for at least one full deprecation window.
3. Endpoint handlers must continue accepting prior payload shapes during the deprecation window.


## Continuity DTO/API Route Change Policy (Run/Portfolio/Ledger/Cash-Flow/Reconciliation)

The following continuity contract families are shared between workstation clients and service boundaries:

- Run continuity DTOs (execution session/replay and run status payloads).
- Portfolio continuity DTOs (position, exposure, account summary, and snapshot payloads).
- Ledger continuity DTOs (entries, balances, posting snapshots, and ledger health summaries).
- Cash-flow continuity DTOs (cash ladder/event projections and run cash-flow projections).
- Reconciliation continuity DTOs and API routes (break queues, operator inbox/review routes, and sign-off projections).

Policy requirements:

1. **Additive changes are preferred.** New optional/nullable members are allowed when old clients can ignore them safely.
2. **Non-additive changes require migration notes.** Any DTO member removal, rename, type narrowing, enum member removal, route removal, or route parameter rename is treated as potential breaking continuity impact.
3. **Migration notes must be explicit.** For each non-additive change, add a dated entry in **Migration Notes** documenting:
   - impacted continuity DTOs/routes,
   - compatibility shim/deprecation window (or waiver reference),
   - downstream consumer action required (desktop, web, automation, or external integrations).
4. **PR evidence is mandatory.** PRs with non-additive continuity changes must include a contract-review packet artifact link plus a PR-body migration-note snippet summarizing consumer impact and rollout guidance.

## Deprecation and Migration Policy

| Change type | Minimum deprecation window | Required mitigation |
| --- | --- | --- |
| DTO/contract field rename or removal | 2 minor releases (or 60 days, whichever is longer) | Keep old field as alias/compat shim; document migration notes |
| Service interface signature change | 2 minor releases | Introduce parallel method or adapter; mark old path obsolete |
| Ledger public API removal/rename | 2 minor releases | Keep compatibility wrapper/extension and migration examples |
| Workstation endpoint route or payload break | 2 minor releases | Keep legacy route/payload handling and explicit migration steps |

**Emergency break policy:** same-release hard breaks are only allowed for security/compliance incidents and must include a release-manager waiver plus same-day migration notes.

## Required Test Gates for Contract Changes

Any pull request touching scoped surfaces must pass:

1. **Standard PR gates:** existing build/test/format gates in `.github/workflows/pr-checks.yml`.
2. **Contract review packet:** `scripts/generate_contract_review_packet.py` for the same base/head range. The packet summarizes tracked surfaces, change category, migration-note status, and reviewer checklist items for the weekly interop cadence.
3. **Contract compatibility gate:** `scripts/check_contract_compatibility_gate.py` from the `contract-compatibility` PR job and the release workflow. The gate flags public type/member/route removals, shared `UiApiRoutes` constant removals or value changes, plus scoped record constructor parameter and enum-member removals.
4. **Contract regression tests (required when contract code changes):**
   - targeted workstation contract serialization/deserialization tests,
   - strategy service interface compatibility tests,
   - ledger read/write snapshot compatibility tests,
   - workstation endpoint request/response shape tests.
5. **Migration-note gate (required for breaking changes):** matrix doc update + PR migration note declaration.

## Weekly Interop Review Cadence

Run the contract review packet before the weekly shared-interop review and attach the JSON/Markdown
output to the review notes or PR artifact bundle:

```bash
python3 scripts/generate_contract_review_packet.py --base origin/main --head HEAD --output artifacts/contract-review/<yyyy-mm-dd>/contract-review-packet.json --markdown-output artifacts/contract-review/<yyyy-mm-dd>/contract-review-packet.md
```

The packet is ready for cadence review when it has no blocking findings. For potential breaking
changes, that requires a matrix migration-note entry and PR-body migration notes; pass
`--pr-body-file <path>` when evaluating a pull request so the packet can verify those notes. A ready
packet is still a review input, not owner approval; record the owner decision and any follow-up in
the weekly interop notes and, when readiness status changes, in `kernel-readiness-dashboard.md`.
Pull request checks also upload a `contract-review-packet` artifact and append the Markdown packet
to the contract-compatibility job summary so reviewers can inspect tracked surfaces and blocking
findings even when the compatibility gate fails.

## Weekly Interop Owner Decisions

| Review date | Packet evidence | Gate result | Owner decision | Follow-up |
| --- | --- | --- | --- | --- |
| 2026-04-27 | `artifacts/contract-review/2026-04-27/contract-review-packet.json` and `artifacts/contract-review/2026-04-27/contract-review-packet.md` | Baseline packet for `origin/main...HEAD` had 0 tracked surface changes, no blocking findings, and `readyForCadenceReview=true`. | Shared Platform Interop owner approved and locked the weekly Wednesday shared-interop review cadence. Every cadence review must attach the generated JSON/Markdown packet, run the same-range compatibility gate, and record the owner decision before Wave 3/Wave 4 shared-contract work can claim readiness. | Run the next packet before the 2026-05-06 shared-interop review, and add migration notes plus PR-body evidence for any potential breaking contract change. |
| 2026-04-29 | `artifacts/contract-review/2026-04-29/contract-review-packet.json` and `artifacts/contract-review/2026-04-29/contract-review-packet.md` | Refresh packet for `HEAD~1...HEAD` captured the `StrategyRunContinuityStatus` DTO delta and confirmed no blocking removals (`readyForCadenceReview=true`). | Shared Platform Interop owner requires merge/release to stay blocked on the compatibility gate plus packet review while this continuity contract evolves. | Keep cadence by attaching this packet to the next interop review and rerun packet + compatibility checks for each follow-up DTO delta. |

## Migration Notes

Use this section for every potential contract-breaking change. Entries must be append-only.

- 2026-04-21: Established initial compatibility matrix and CI policy gate for workstation contracts/services/ledger/endpoints. No runtime break introduced in this change.
- 2026-04-25: Wired the contract compatibility gate into `.github/workflows/pr-checks.yml` so pull requests and releases both enforce the matrix. No runtime contract break introduced.
- 2026-04-26: Expanded the compatibility gate to detect scoped record constructor parameter and enum-member removals, with focused Python regression coverage. No runtime contract break introduced.
- 2026-04-26: Added `UiApiRoutes.cs` route constants to the compatibility scope and gate heuristics so shared route removals or string changes require migration notes. No runtime contract break introduced.
- 2026-04-26: Added additive trading-readiness control evidence fields (`TradingControlEvidenceDto`, `TradingControlReadinessDto.RecentEvidence`, explainability counts, and warnings) plus `OperatorWorkItemKindDto.ExecutionControl`. Older clients can ignore the new payload fields; enum-aware clients should treat the new work-item kind as an execution-risk blocker.
- 2026-04-26: Added additive operator-inbox route `GET /api/workstation/operator/inbox`, `OperatorInboxDto`, and optional navigation fields on `OperatorWorkItemDto` so desktop/web consumers can open shared readiness and reconciliation work items from one queue. Older clients can continue reading readiness `WorkItems` without consuming the new route or optional fields.
- 2026-04-27: Added `scripts/generate_contract_review_packet.py` as the repeatable weekly shared-interop review artifact for tracked contract changes. No runtime contract break introduced.
- 2026-04-29: Updated `StrategyRunContinuityStatus` with additive `HasFills` coverage and tightened continuity warning-code expectations for run-centered readiness consumers. Older clients that do not read `HasFills` should continue defaulting to `false`/missing-field handling and can ignore unknown warning codes; consumers that branch on continuity warnings should treat new codes as additive and map unknown values to their existing generic warning UX.
- 2026-04-29: Merge/release cadence now explicitly depends on passing both `scripts/check_contract_compatibility_gate.py --base <base> --head <head>` and the same-range `scripts/generate_contract_review_packet.py` review packet before owner sign-off.

## Pull Request Author Checklist

When your PR touches any scoped surface above:

- [ ] I confirmed whether the change is additive vs. breaking.
- [ ] I updated this matrix when compatibility behavior/policy changed.
- [ ] If breaking, I added a dated item under **Migration Notes** and concrete migration instructions in the PR body.
- [ ] I validated required tests for contract changes.
