# Solo-developer and AI-assisted development

**Status:** adopted development guidance; does not waive GitHub rules, branch protection, or release controls.

Meridian is developed by one product owner assisted by multiple AI agents in a public repository. Optimize for useful changes, fast feedback, financial correctness, and honest evidence—not fictional teams or mandatory access to private financial accounts.

## Default workflow

1. Define the requested behavior and the smallest useful acceptance test.
2. Implement in a task branch; preserve unrelated work.
3. Run the narrowest meaningful build, test, lint, or documentation checks. Expand based on changed behavior and risk, not a universal checklist.
4. For financial, authorization, persistence, migration, or security changes, ask a separate agent to look for consequential defects, then resolve or clearly record material findings. An AI agent is **not** an independent human reviewer.
5. Report what changed, what actually ran, what could not run, and remaining important risks in a concise PR.
6. Leave merge and sensitive activation decisions to the developer and the repository's enforced rules. No AI agent may self-approve, bypass required checks, or merge without explicit authorization.

## Verification tiers

Choose the tier by what the change can break, then do the checks for that tier. Skipped or blocked checks are reported, not hidden.

| Tier | Applies to | Compile | Targeted tests | AI review | Owner approval |
| --- | --- | --- | --- | --- | --- |
| **Level 1 - routine** | docs, styling, UI features, isolated utilities, refactors, low-risk features | when applicable | relevant and lightweight | optional | normal merge decision |
| **Level 2 - financial or architectural** | ledger and accounting rules, storage migrations, Security Master, provider adapters, shared domain contracts, shared business logic | yes | yes, including invariants and PostgreSQL-backed tests where persistence changes | required, from a separate agent where feasible, plus a short residual-risk summary | merge decision; external human review welcome, not universally required |
| **Level 3 - sensitive or destructive** | money movement, live trading, destructive migrations, authentication and secret handling, production deployment | yes | extended: adversarial, negative, rollback and fail-closed cases | required, stronger independent review when risk demands | explicit and recorded by the owner |

Real-world activation is a separate milestone from development completion.

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

## Agent roles

| Role | Responsibility |
| --- | --- |
| Developer (product owner) | Sets direction, scope and risk tolerance; owns every merge and activation decision |
| Planning agent | Converts goals into small, non-overlapping tasks |
| Implementation agents | Build independent, nonconflicting changes |
| Verification agent | Runs the checks for the tier and the relevant invariants |
| Review agent | Looks for consequential defects and security risks; does not demand extra documentation for its own sake |

One task should produce one coherent change, its tests, and a short PR summary: what changed, what ran, significant risks, and the decision required. Agents must not impersonate an independent human reviewer, fabricate approval evidence, or invent new gates, issues or decision records for small corrections.

## Development readiness and production readiness

- **Development readiness:** Meridian builds, tests and demonstrates its documented capabilities using reproducible synthetic data, with safeguards on dangerous operations. It does not depend on production readiness.
- **Production readiness:** Meridian is suitable for a specifically defined real operating environment, with secret management, independent validation, disaster recovery, deployment assurance and operational controls. The production-readiness tracker is the checklist for that objective; it is not a condition of ordinary PRs.

## Keep agent coordination lightweight

One small task needs no agent orchestra, multiple issue mirrors, new architecture decision record, or evidence manifest. Use handoff and context-pack machinery when multiple agents are actually coordinating or a change crosses architectural boundaries. Prefer small independently reviewable PRs; size is a guideline, not a hard line-count gate.

Keep governance documents and generated artifacts consistent, but do not create duplicate narrative status reports. Historical review and certification evidence must never be rewritten to claim that a requirement passed when it did not.

## Non-negotiable invariants

Preserve authorization and tenancy isolation, balanced journals, immutable posted records, idempotency, period locks, deterministic replay, reliable source provenance, and migration recovery where applicable. A solo developer may simplify process, not falsify financial results or bypass a material security control.
