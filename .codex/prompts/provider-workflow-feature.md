# Add Provider Workflow Feature

Objective: implement a provider-management workflow feature for setup, credentials, health,
degradation, calibration, validation, or operator recovery.

Constraints:
- Inventory existing provider services, credential store, health/degradation docs, commands, and WPF
  provider view models before coding.
- Keep secrets out of chat, logs, snapshots, and test artifacts.
- Reuse provider contracts and shared credential/readiness models.
- Use async calls, cancellation, bounded retries, and explicit disabled reasons.
- Add tests for view-model state, service orchestration, error handling, and provider-safe fallback.

Final summary must include credential handling, provider contracts reused, tests, and live-action
safety limits.
