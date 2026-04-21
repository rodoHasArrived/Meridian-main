# Provider Validation Artifacts

This subtree is the committed Wave 1 evidence root for provider-confidence and checkpoint proof work.

## Layout

- `artifacts/provider-validation/_automation/<yyyy-mm-dd>/wave1-validation-summary.json`
- `artifacts/provider-validation/_automation/<yyyy-mm-dd>/wave1-validation-summary.md`
- `artifacts/provider-validation/<provider>/<yyyy-mm-dd>/manifest.json`
- `artifacts/provider-validation/<provider>/<yyyy-mm-dd>/<scenario>/summary.md`

## Result meanings

- `validated`: the scenario has a committed sanitized runtime capture or a repo-backed offline replay that closes the Wave 1 claim.
- `bounded`: the scenario is explicitly limited by vendor runtime, entitlement, package, or manual-session requirements; the repo points to the exact test suites and the missing runtime condition.

## Policy

- Keep only small sanitized summaries and attachments in git.
- Keep raw broker logs, secrets, and entitlement-bearing payloads out of the repo.
- Reference external raw logs from `manifest.json` when they exist.
- Use [`scripts/dev/run-wave1-provider-validation.ps1`](/C:/Users/Andrew%20James%20Rowden/OneDrive/Documents/OneDrive/Documents/Desktop/Meridian-main/scripts/dev/run-wave1-provider-validation.ps1) for the offline/CI command matrix.
