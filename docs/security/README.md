# Security Documentation

Security-related documentation for the Meridian.

## Documents

| Document | Description |
|----------|-------------|
| [Known Vulnerabilities](known-vulnerabilities.md) | Assessed and accepted dependency vulnerabilities with documented mitigations |

## Security Practices

- API credentials are stored as environment variables, never in config files
- See [Environment Variables](../reference/environment-variables.md) for credential configuration
- See [Operator Runbook](../operations/operator-runbook.md) for operational security guidance
- The [security.yml](https://github.com/rodoHasArrived/Meridian/blob/main/.github/workflows/security.yml) workflow runs CodeQL analysis and dependency auditing
