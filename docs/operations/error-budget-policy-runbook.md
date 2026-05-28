# Error Budget Policy Runbook

This runbook operationalizes the error-budget policy in [Service Level Objectives](./service-level-objectives.md) and defines explicit deployment-freeze and reliability-sprint triggers.

## Scope

- Applies to all production-impacting changes across ingestion, data quality, availability, freshness, storage, and provider connectivity planes.
- Uses the monthly SLO report format in [Monthly SLO Review Template](./slo-review-template.md).

## Trigger Matrix

### A) Deployment Freeze Triggers

A **deployment freeze** starts immediately when any condition is true:

1. **Budget remaining < 25%** for any critical SLO at any point in the month.
2. **Burn-rate critical alert** fires at P1 threshold for 6h or 24h windows and is not resolved within 60 minutes.
3. **Any unmitigated P1 breach** of a zero-tolerance SLO (`SLO-ST-001`) or collector uptime (`SLO-AV-001`) during market hours.
4. **Two P2 breaches of the same SLO within 7 days** indicating instability trend.

Freeze policy while active:

- Block feature deployments and schema-changing migrations.
- Allow only incident mitigation, rollback, observability, and reliability fixes.
- Require Reliability Lead + Incident Commander approval for any exception.

### B) Reliability Sprint Triggers

A **mandatory reliability sprint** starts (or is scheduled for next sprint start) when any condition is true:

1. **Budget remaining < 10%** for any critical SLO.
2. **Budget exhausted** for any SLO.
3. **Two or more P1 incidents in a calendar month** tied to the same subsystem.
4. **Launch gate at risk:** streak cannot reach 3 compliant months without mitigation completion.

Reliability sprint minimum outcomes:

- Top three budget-burn drivers remediated or mitigated.
- Alert noise/threshold review completed.
- Postmortems complete for all P1/P2 incidents in period.
- Updated monthly report published under `docs/status/slo-reports/`.

## Response Workflow

1. Confirm trigger details in dashboards + alert history.
2. Open incident ticket and tag `slo-budget`.
3. Announce freeze/sprint mode in operator channel.
4. Assign owners:
   - Incident Commander
   - Reliability Lead
   - Service owner(s) for affected SLOs
5. Execute mitigations and record evidence links.
6. Update monthly SLO report and launch-gate streak table.
7. Lift freeze only after criteria are met.

## Freeze Exit Criteria

All conditions must be true:

- No active unmitigated P1 SLO breach.
- Budget remaining for impacted SLOs is stabilized above 25% or approved exception is documented.
- Corrective actions for root cause are scheduled with owners/dates.
- A linked postmortem exists for every P1/P2 incident that contributed.

## Reliability Sprint Exit Criteria

All conditions must be true:

- Mitigations for all critical breaches are implemented or explicitly accepted by leadership with dated risk acceptance.
- Next-month burn-rate projection is within budget envelope.
- Monthly report and operator runbook references are updated.

## Evidence and Reporting

- Primary monthly artifact: `docs/status/slo-reports/YYYY-MM-slo-review.md`
- Include:
  - Compliance summary
  - Budget + burn-rate table
  - Incident/postmortem links
  - Action register and owners
  - Launch gate streak status
