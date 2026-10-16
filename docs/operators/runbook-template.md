# Operator Runbook Template

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-05
**Audience:** operator-procedure authors

Use this structure for new or substantially revised procedures in [Operators](README.md).
Replace the placeholders with verified instructions. This is an authoring template, not an
executable procedure. Keep configuration tables in [Reference](../reference/README.md) and link
them from the runbook.

## Required Sections

| Section | What the reader needs |
| --- | --- |
| Purpose and scope | Task, successful outcome, supported deployment, and explicit boundaries. |
| Prerequisites | Software, permissions, persistence, credentials, configuration, and setup links. |
| Execution context | Operating system, shell, working directory, host/port, and separate terminals for blocking processes. |
| Procedure | Numbered actions with complete commands and checkpoints before consequential actions. |
| Expected results | HTTP status, response fields, receipt IDs, output files, and completion conditions. |
| Failure and recovery | Symptom, diagnostic, safe next action, stop condition, and rollback where supported. |
| Evidence and handoff | Sanitized artifacts to retain, accountable owner, and acceptance requirements. |
| Related references | Owning configuration/API reference, source contract, and adjacent procedures. |

## Copyable Outline

```markdown
# <Task> Operations

**Status:** draft
**Owner:** <accountable-team>
**Reviewed:** <YYYY-MM-DD>

## Purpose and Scope

<Who runs this, in which supported environment, and what success means.>

## Prerequisites

- <Installed software and version; link to setup.>
- <Persistence and configuration requirements; link to exact reference section.>
- <Required role, scope, and approved credential source.>

## Execution Context

- Platform: <supported operating system>
- Shell: <Bash / PowerShell version>
- Working directory: repository root (or a specified installation directory)
- Target: <host/port and environment; distinguish local development from production>

## Procedure

1. <Verify prerequisites and effective configuration.>
2. <Terminal 1: start the host if required; explain that it remains running.>
3. <Terminal 2: perform an authenticated check with complete commands.>
4. <Perform the bounded task and record its result.>

## Expected Results

<Expected status/fields/output and the exact condition that permits the next step.>

## Failure and Recovery

| Symptom | Diagnostic | Safe next action |
| --- | --- | --- |
| <Failure> | <Log, response, or receipt to inspect> | <Retry, stop, recover, or escalate> |

<State which changes can be rolled back and which require a governed correction.>

## Evidence and Handoff

<Retain sanitized receipts, timestamps, configuration provenance, and verification results.>
<Name the owner and any required acceptance decision.>

## Related References

<Link configuration, API contracts, and adjacent procedures.>
```

## Author Checks

- Verify each option and endpoint against current source or CLI help. Distinguish market-data
  settings from execution settings when both appear in a provider procedure.
- Use repository-root commands with explicit project paths. Do not require readers to correct
  command casing or invent omitted flags.
- Explain whether authentication uses an API key or an operator session and what scope it grants.
  Never imply that possessing a key supplies tenant or company context.
- Provide expected results and concrete recovery actions; a successful HTTP response alone may
  still report a blocked operation.
- Label unexecuted examples as source-verified when reporting validation. Preserve the distinction
  between a local check, operator acceptance, and release certification.
- Follow the [contribution guide](../development/documentation-contribution-guide.md) for metadata,
  links, and documentation checks.
