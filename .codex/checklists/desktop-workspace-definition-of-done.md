# Desktop Workspace Definition Of Done

- Workspace uses the approved top-level navigation taxonomy.
- Screen composition reuses shared shell, toolbar, command, table, inspector, status, and diagnostics
  primitives where practical.
- View, view model, services, DTO/read models, navigation registration, and DI registration have
  clear ownership.
- Loading, empty, partial, error, stale, busy, disabled, and success states are represented.
- Large data surfaces are virtualized, paged, or streamed.
- Commands are cancelable or guarded when work can be long-running.
- Tests cover view-model state, command behavior, service mapping, and important bindings.
- Narrow validation has been run and the result is recorded.
- Resource risks and tradeoffs are summarized for the operator or reviewer.
