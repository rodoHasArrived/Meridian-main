# Generate Tests For ViewModel And Commands

Objective: add focused tests for WPF view models, commands, services, and workflow projection.

Constraints:
- Inventory existing test naming, fixture builders, fake services, and related view-model tests.
- Cover loading, empty, error, disabled, busy, cancel, retry, success, and selected-item states.
- Test command guards and async race prevention without launching the UI where possible.
- Prefer deterministic data and no live provider calls.
- Run the narrowest `tests/Meridian.Wpf.Tests` filter that covers the changed area.

Final summary must include scenarios covered, test command, and remaining untested risk.
