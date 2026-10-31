# Solo-developer and AI-assisted development

**Status:** proposed development guidance; does not waive GitHub rules or release controls.

Meridian is developed by one product owner assisted by multiple AI agents in a public repository. Optimize for useful changes, fast feedback, financial correctness, and honest evidence—not fictional teams or mandatory access to private financial accounts.

## Default workflow

1. Define the requested behavior and the smallest useful acceptance test.
2. Implement in a task branch; preserve unrelated work.
3. Run the narrowest meaningful build, test, lint, or documentation checks. Expand based on changed behavior and risk, not a universal checklist.
4. For financial, authorization, persistence, migration, or security changes, ask a separate agent to look for consequential defects, then resolve or clearly record material findings. An AI agent is **not** an independent human reviewer.
5. Report what changed, what actually ran, what could not run, and remaining important risks in a concise PR.
6. Leave merge and sensitive activation decisions to the developer and the repository's enforced rules. No AI agent may self-approve, bypass required checks, or merge without explicit authorization.

## Risk-based validation

| Risk | Examples | Minimum useful evidence |
| --- | --- | --- |
| Routine | prose, UI copy/style, isolated utility | diff/format check and relevant lightweight check; compile only if applicable |
| Domain | shared contract, accounting, lot, ledger, reconciliation, reference data, storage | targeted build and positive/negative tests of changed invariants; cross-backend tests when applicable; independent AI critique |
| Sensitive | credentials, auth/tenancy, destructive migration, real-money/trading activation, release deployment | adversarial and rollback/fail-closed tests, independent critique, explicit owner decision, and applicable required GitHub checks |

A targeted check is not a claim that the entire suite passed. When the required GitHub workflow runs, it remains authoritative for its own checks. Do not weaken or skip a failing safety test to obtain green CI. Do not silently classify a test failure as flaky.

If the full local `bash scripts/ci.sh` is unavailable or disproportionate to a routine change, record that honestly; let applicable hosted checks run. A mandatory branch-protection check cannot be waived by this document. Changes to actual workflow policy must be reviewed separately.

## Public repository and synthetic data

- Default tests and demonstrations to seeded, deterministic **synthetic** institutions, accounts, custodian statements, broker events, prices, transactions, accounting outputs and failure scenarios.
- Never commit secrets, actual customer/account details, or sensitive financial data.
- Live-provider credentials and real institutional connections are **not** prerequisites to routine development, acceptance of synthetic behavior, or a public PR.
- Describe provider evidence accurately: `synthetic-verified`, `live-verified`, `live-untested`, or `unsupported`. Simulated success is not evidence of live compatibility.
- Keep real-money execution and unsafe real-account operations disabled unless separately authorized and safely configured.

## Completion vocabulary

- **Planned:** not started.
- **In progress:** implementation or testing underway.
- **Verified:** expected behavior proven within a **stated scope**, with significant unresolved limitations disclosed.
- **Released:** included in an identified supported release.

A blocked reason can accompany any status. Keep current roadmap schema values until a dedicated, separately validated migration is approved: this vocabulary guides human communication and does **not** silently rewrite canonical registry schemas or historic statuses.

**Development verification is not production certification.** The production-readiness tracker remains a separate future deployment checklist. It does not mean every routine PR needs human operator sign-off, a real provider, a signed installer, or a full release artifact.

## Keep agent coordination lightweight

One small task needs no agent orchestra, multiple issue mirrors, new architecture decision record, or evidence manifest. Use handoff and context-pack machinery when multiple agents are actually coordinating or a change crosses architectural boundaries. Prefer small independently reviewable PRs; size is a guideline, not a hard line-count gate.

Keep governance documents and generated artifacts consistent, but do not create duplicate narrative status reports. Historical review and certification evidence must never be rewritten to claim that a requirement passed when it did not.

## Non-negotiable invariants

Preserve authorization and tenancy isolation, balanced journals, immutable posted records, idempotency, period locks, deterministic replay, reliable source provenance, and migration recovery where applicable. A solo developer may simplify process, not falsify financial results or bypass a material security control.
