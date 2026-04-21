# DK1 Pilot Parity Runbook (Alpaca, Robinhood, Yahoo)

**Last Updated:** 2026-04-21  
**Owners:** Data Operations + Provider Reliability  
**Scope:** Evidence-backed parity execution for DK1 pilot provider set (Alpaca, Robinhood, Yahoo)

---

## Purpose

Provide a single reproducible runbook that proves DK1 pilot parity against the approved provider-validation evidence set.

## Canonical evidence set (exact links)

### Governing matrix and execution command

- Provider validation matrix (authoritative provider evidence table): [`provider-validation-matrix.md`](./provider-validation-matrix.md)
- Wave 1 validation command script (must be executed for every parity run): [`scripts/dev/run-wave1-provider-validation.ps1`](../../scripts/dev/run-wave1-provider-validation.ps1)
- Generated automation outputs (must be attached for the run date):
  - `artifacts/provider-validation/_automation/<yyyy-mm-dd>/wave1-validation-summary.json`
  - `artifacts/provider-validation/_automation/<yyyy-mm-dd>/wave1-validation-summary.md`

### Provider-specific evidence links

- **Alpaca row evidence:** [`provider-validation-matrix.md#wave-1-matrix`](./provider-validation-matrix.md#wave-1-matrix) (Alpaca core provider confidence tests)
- **Robinhood row evidence:** [`provider-validation-matrix.md#wave-1-matrix`](./provider-validation-matrix.md#wave-1-matrix) + runtime packet path `artifacts/provider-validation/robinhood/2026-04-09/`
- **Yahoo row evidence:** [`provider-validation-matrix.md#wave-1-matrix`](./provider-validation-matrix.md#wave-1-matrix) (historical/fallback test suites)

---

## Run procedure

1. **Sync evidence baseline**
   - Verify `docs/status/provider-validation-matrix.md` is current for the pilot window.
2. **Execute Wave 1 command matrix**
   - Run `./scripts/dev/run-wave1-provider-validation.ps1` from repo root.
3. **Capture output artifacts**
   - Archive both generated files from `artifacts/provider-validation/_automation/<yyyy-mm-dd>/`.
4. **Assemble provider packet**
   - Confirm Alpaca, Robinhood, Yahoo evidence remains aligned with the matrix row expectations.
5. **Publish parity packet**
   - Add links to output artifacts in the weekly DK1 review note and in the dashboard row.

---

## Pass / fail criteria

### Pass

- Wave 1 script exits successfully.
- Generated JSON/Markdown summaries exist for the run date.
- No unresolved parity drift between dashboard claim and matrix evidence rows.

### Fail

- Script failure.
- Missing run-date summary artifacts.
- Matrix row evidence stale or contradictory to dashboard status.

---

## Operator handoff checklist

- [ ] Run date recorded (UTC).
- [ ] `wave1-validation-summary.json` linked.
- [ ] `wave1-validation-summary.md` linked.
- [ ] Robinhood manual runtime packet linked (if applicable).
- [ ] Dashboard DK1 parity status updated with evidence references.
