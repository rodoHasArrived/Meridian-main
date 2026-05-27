# Add Audit Timeline

Objective: implement an audit timeline that shows ordered evidence, decisions, actions, and
operator handoffs.

Constraints:
- Inventory existing audit, activity, notification, replay, reconciliation, ledger, and provider
  evidence models first.
- Use a shared timeline entry model when possible.
- Keep ordering stable, timestamps explicit, and source/evidence links traceable.
- Use paging or virtualization for long histories.
- Add tests for ordering, filtering, empty state, stale evidence, and detail selection.

Final summary must include evidence source, retention/paging behavior, tests, and privacy/logging
considerations.
