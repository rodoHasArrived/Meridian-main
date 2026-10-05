# Security Documentation

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-05

This lane contains Meridian threat-model, vulnerability, remediation, and compliance guidance.
Operator procedures live in `docs/operators/`; stable configuration lookup belongs in
`docs/reference/`.

## Find the right record

| Task | Start here | Authority and scope |
| --- | --- | --- |
| Review threats and trust boundaries | [Threat model](threat-model-current-state.md) | Maintained model; check its review date and source evidence for the release under review. |
| Track remediation | [Security remediation backlog](security-remediation-backlog.md) | Finding owners, required proof, and recorded dispositions. |
| Check dependency risk decisions | [Known vulnerabilities](known-vulnerabilities.md) | Accepted, retired, and fixed advisory records; fresh scans still apply to each release. |
| Plan compliance evidence | [SOC 2 scope](compliance/soc2-scope.md), [control matrix](compliance/soc2-control-matrix.md), and [evidence calendar](compliance/soc2-evidence-calendar.md) | Program guidance and evidence responsibilities. |
| Review proposed audit milestones | [SOC 2 roadmap](compliance/soc2-roadmap.md) | Dated target windows; elapsed dates do not establish audit completion. |
| Prepare buyer diligence | [Buyer packet index](buyer-packet/document-index.md) | Versioned draft with its own review dates and refresh checklist. |
| Read prior remediation evidence | [May 20 remediation record](codex-security-remediation-2026-05-20.md) | Historical findings and test results for that pass. |

Security and compliance records describe their own evidence scope. They do not establish product
certification; consult the [readiness tracker](../product/implementation-todo-list.md) and evidence
for the intended release commit. Keep the original dates and dispositions of historical records.

## Security Practices

- Never commit credentials, tokens, keys, or production secrets.
- Provider credentials saved by workstation flows use the shared encrypted credential store under
  the resolved data root. Environment variables are legacy read-only fallback where supported.
- See [Provider Credentials and Access](../operators/provider-credentials.md) for operator procedure
  and [Environment Variables](../reference/environment-variables.md) for lookup details.
- See [Operator Documentation](../operators/README.md) and the
  [Lifecycle Control Plane](../reference/lifecycle-control-plane.md) for operational security and
  fail-closed startup guidance.
- The [CodeQL workflow](../../.github/workflows/codeql.yml) owns repository static-analysis checks;
  required GitHub Actions remain the merge authority.
