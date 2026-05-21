# Add Diagnostics Panel

Objective: add a diagnostics panel that helps operators understand system, provider, data, or
workflow health.

Constraints:
- Inventory existing diagnostics, activity log, notification, system health, provider health, and
  telemetry surfaces first.
- Reuse shared status badges, evidence rows, timeline entries, and recovery actions.
- Avoid high-frequency polling; throttle or subscribe with lifecycle cleanup.
- Keep diagnostic explanation and command state in view models.
- Add tests for healthy, degraded, failed, loading, stale, and recovery states.

Final summary must include telemetry sources, update cadence, lifecycle cleanup, and tests.
