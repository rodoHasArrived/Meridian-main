# Contract Compatibility Matrix

Last reviewed: 2026-04-26
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
2. **Contract compatibility gate:** `scripts/check_contract_compatibility_gate.py` from the `contract-compatibility` PR job and the release workflow. The gate flags public type/member/route removals, shared `UiApiRoutes` constant removals or value changes, plus scoped record constructor parameter and enum-member removals.
3. **Contract regression tests (required when contract code changes):**
   - targeted workstation contract serialization/deserialization tests,
   - strategy service interface compatibility tests,
   - ledger read/write snapshot compatibility tests,
   - workstation endpoint request/response shape tests.
4. **Migration-note gate (required for breaking changes):** matrix doc update + PR migration note declaration.

## Migration Notes

Use this section for every potential contract-breaking change. Entries must be append-only.

- 2026-04-21: Established initial compatibility matrix and CI policy gate for workstation contracts/services/ledger/endpoints. No runtime break introduced in this change.
- 2026-04-25: Wired the contract compatibility gate into `.github/workflows/pr-checks.yml` so pull requests and releases both enforce the matrix. No runtime contract break introduced.
- 2026-04-26: Expanded the compatibility gate to detect scoped record constructor parameter and enum-member removals, with focused Python regression coverage. No runtime contract break introduced.
- 2026-04-26: Added `UiApiRoutes.cs` route constants to the compatibility scope and gate heuristics so shared route removals or string changes require migration notes. No runtime contract break introduced.
- 2026-04-26: Added additive trading-readiness control evidence fields (`TradingControlEvidenceDto`, `TradingControlReadinessDto.RecentEvidence`, explainability counts, and warnings) plus `OperatorWorkItemKindDto.ExecutionControl`. Older clients can ignore the new payload fields; enum-aware clients should treat the new work-item kind as an execution-risk blocker.

## Pull Request Author Checklist

When your PR touches any scoped surface above:

- [ ] I confirmed whether the change is additive vs. breaking.
- [ ] I updated this matrix when compatibility behavior/policy changed.
- [ ] If breaking, I added a dated item under **Migration Notes** and concrete migration instructions in the PR body.
- [ ] I validated required tests for contract changes.
