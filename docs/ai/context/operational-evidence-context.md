# Operational Evidence Context

**Status:** active AI context pack
**Owner:** core-team
**Reviewed:** 2026-06-16

## Purpose

Use this context before generating or reviewing code, tests, UI, reports, or documentation that changes source evidence, normalized records, reconciliation, exception casework, ledger impact, capital-account impact, close readiness, report provenance, delivery records, or audit support.

## Meridian Evidence Rules

- Meridian should prove operational records, not just display them.
- Source evidence must retain enough identity to reconstruct origin, receipt time, source hash or version, provider/file/API context, and review state.
- Normalized records must remain traceable back to source evidence and mapping or extraction version.
- Reconciliation breaks need owner, age, due date or SLA posture, materiality, root cause, supporting evidence, approval state, and blocked outputs where applicable.
- Exceptions that affect close, reporting, ledger, capital accounts, or delivery must fail closed until evidence and review state satisfy the relevant policy.
- Report lines should link to approved source records, calculations, ledger or capital-account impact, package version, delivery evidence, approval, and restatement history where applicable.
- Audit evidence must be retained as a manifest or support packet that can explain what changed, why it changed, who approved it, and where it was used.

## Active Proof Chain

```text
source evidence
-> normalized record
-> validation
-> reconciliation
-> exception resolution
-> journal / ledger impact
-> capital account impact
-> close package
-> report line
-> delivery record
-> audit evidence
```

## AI Usage

Load this context before work on:

- Operational Evidence Graph features.
- Fund Event Command Center flows.
- close cockpit, exception queues, and reconciliation casework.
- report-line provenance, governed report packages, exports, and restatement workflows.
- evidence vault, request lists, audit support, tax support, and retained support packages.
- AI-assisted extraction, matching, summarization, variance explanation, or draft preparation.

## Review Checklist

- Can the user trace the record from source evidence to output?
- Does the workflow retain evidence before the record is approved, posted, delivered, or reported?
- Are unresolved exceptions close-blocking or release-blocking when they should be?
- Are approval, scope, and segregation-of-duties concerns explicit?
- Are browser and WPF surfaces using shared identifiers, status definitions, read models, and endpoint contracts?
- Does any AI-assisted step remain a reviewed draft instead of an autonomous control action?
