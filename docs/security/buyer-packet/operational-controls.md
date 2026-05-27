# Meridian Buyer Security Packet — Operational Controls

- **Document Owner:** Platform Operations
- **Version:** 2026.05.27.1
- **Last Reviewed:** 2026-05-27
- **Next Review Due:** 2026-08-31
- **Classification:** Buyer Diligence / Controlled Distribution

## Access Control
- Access to operational workflows is controlled through role-oriented responsibilities across trading, portfolio, accounting, reporting, strategy, data, and settings surfaces.
- Administrative and high-impact actions are executed through explicit commands/scripts that leave observable outputs/artifacts.
- Credential- and connectivity-related checks are available via focused diagnostics/configuration commands for controlled verification.

## Change Control
- Code and configuration changes are routed through branch/PR workflows with required review and targeted validation commands.
- Change validation uses narrowest-scope tests first, then broader bundles where shared contracts/routes are impacted.
- Evidence of change safety is retained through build/test outputs and scripted workflow artifacts.

## Backup and Recovery
- Data/package/import workflows support export/import and validation routines for recoverability.
- Replay, package validation, and WAL-repair style commands support integrity checks and recovery preparation.
- Artifact retention policies on automation outputs preserve recent recovery-relevant evidence while controlling storage growth.

## Incident Response
- Incident triage is supported by diagnostics endpoints/commands, operator inbox/readiness views, and status dashboards.
- Response playbooks rely on repeatable scripts and evidence packet generation for post-incident verification.
- Recovery confidence is reinforced through replay verification, provider validation gates, and sign-off artifacts.

## Control Operation Assurance
- Operational controls are periodically exercised through normal runbooks and dedicated validation scripts.
- Control failures or drift trigger documented remediation and packet refresh updates.
- Freshness metadata ensures due diligence consumers can verify control statements are current.

## Freshness and Quarterly Refresh Checklist
- [ ] Confirm access-control narrative matches current role/surface model.
- [ ] Revalidate change-control gates and current pre-PR/test workflows.
- [ ] Verify backup/recovery command paths and retention assumptions remain accurate.
- [ ] Reconcile incident-response section with latest operational runbooks.
- [ ] Update metadata and `document-index.md` revision details.
