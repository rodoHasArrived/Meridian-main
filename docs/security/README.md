# Security Documentation

Security-related documentation for the Meridian.

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

- API credentials are stored as environment variables, never in config files
- See [Environment Variables](../reference/environment-variables.md) for credential configuration
- See [Operator Runbook](../operations/operator-runbook.md) for operational security guidance
- The [security.yml](https://github.com/rodoHasArrived/Meridian-main/blob/main/.github/workflows/security.yml) workflow runs CodeQL analysis and dependency auditing
