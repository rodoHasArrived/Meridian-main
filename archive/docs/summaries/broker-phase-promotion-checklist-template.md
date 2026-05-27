# Broker Phase Promotion Checklist Template

Use this template for each broker implementation before phase promotion. Save each completed checklist as a dated artifact under `artifacts/provider-validation/` (or another agreed evidence path) and link it from the provider validation packet.

## Promotion Gate Requirement

- [ ] **Required:** A completed checklist artifact is attached for this broker implementation.
- [ ] **Required:** Validation links are included for every checklist section below.
- [ ] **Required:** Promotion is blocked until all required items are complete or have an approved exception with owner/date.

## Implementation Metadata

- Broker implementation:
- Provider/kernel version:
- Promotion phase target:
- Environment(s) validated:
- Checklist owner:
- Review date (UTC):
- Related issue/epic:

## Validation Checklist

### 1) Endpoint Inventory Completion

- [ ] All broker-facing and shared endpoints are inventoried.
- [ ] Endpoint ownership and consumers are documented.
- [ ] Additive vs. breaking endpoint deltas are called out.
- Evidence links:
  - 

### 2) DTO Mapping Completeness

- [ ] Request/response DTO mappings are complete for all covered endpoints.
- [ ] Nullability/default/fallback behaviors are documented.
- [ ] Enum/value translation mappings are verified.
- Evidence links:
  - 

### 3) Test Slice Coverage

- [ ] Focused unit and integration test slices are identified and executed.
- [ ] Endpoint mapping regression tests are included.
- [ ] Failures/flakes and dispositions are documented.
- Evidence links:
  - 

### 4) Compatibility Review

- [ ] Contract compatibility matrix entries are updated.
- [ ] Cross-module/consumer compatibility impacts are reviewed.
- [ ] Backward compatibility exceptions (if any) are approved.
- Evidence links:
  - 

### 5) Calibration Output

- [ ] Provider degradation calibration output is generated for the candidate kernel/version.
- [ ] Calibration report quality checks are complete.
- [ ] Promotion gate thresholds/pass-fail rationale is documented.
- Evidence links:
  - 

### 6) Readiness Projection Verification

- [ ] Readiness projection output is verified against current replay/evidence expectations.
- [ ] Account-scoped and global readiness paths are validated as applicable.
- [ ] Readiness drift/freshness concerns are documented with mitigations.
- Evidence links:
  - 

### 7) Sign-Off Artifacts

- [ ] Operator sign-off artifact is attached.
- [ ] Technical owner sign-off artifact is attached.
- [ ] Product/risk acceptance (if required) is attached.
- Evidence links:
  - 

## Standard Effort/Risk Scoring

Score each dimension from **1 (low)** to **5 (high)**.

### Effort

- Delivery complexity (1-5):
- Integration depth (1-5):
- Test execution burden (1-5):
- Documentation/update burden (1-5):
- **Effort total (sum):**

### Risk

- Runtime reliability risk (1-5):
- Contract/compatibility risk (1-5):
- Data quality/reconciliation risk (1-5):
- Operational readiness risk (1-5):
- **Risk total (sum):**

### Sequencing Guidance

- Composite priority score (recommended: `Risk total × 2 + Effort total`):
- Recommended phase sequencing rank:
- Reprioritization notes (dependencies, blockers, opportunities):

## Exceptions and Conditional Approvals

- Exception requested? (yes/no):
- Exception owner:
- Approver:
- Expiration date (UTC):
- Compensating controls:
- Follow-up task link(s):

## Final Promotion Decision

- Decision: `Approve` / `Approve with conditions` / `Do not promote`
- Decision date (UTC):
- Decision makers:
- Promotion package links:
  - 
