# Security Documentation

**Status:** supporting
**Owner:** core-team
**Reviewed:** 2026-07-19

This lane contains Meridian threat-model, vulnerability, remediation, and compliance guidance.
Operator procedures live in `docs/operators/`; stable configuration lookup belongs in
`docs/reference/`.

## Documents

| Document | Description |
|----------|-------------|
| [Known Vulnerabilities](known-vulnerabilities.md) | Assessed and accepted dependency vulnerabilities with documented mitigations |
| [Threat Model (Current State)](threat-model-current-state.md) | Current trust boundaries, attack surfaces, mitigations, and severity calibration |
| [SOC 2 Compliance Workspace](compliance/) | SOC 2 program scope, control matrix, evidence calendar, and roadmap for procurement/audit reviewers |
| [SOC 2 Scope](compliance/soc2-scope.md) | In-scope systems and trust-boundary coverage for SOC 2 readiness |
| [SOC 2 Control Matrix](compliance/soc2-control-matrix.md) | Trust Services Criteria mapping to Meridian controls, owners, and evidence sources |
| [SOC 2 Evidence Calendar](compliance/soc2-evidence-calendar.md) | Monthly and quarterly cadence for SOC evidence collection and review ownership |
| [SOC 2 Roadmap](compliance/soc2-roadmap.md) | Dated milestones for readiness, Type I, remediation, and Type II observation period |

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
