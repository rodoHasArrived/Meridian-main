# Review MVVM Compliance

Objective: review a Meridian desktop area for MVVM boundary violations and practical refactoring
steps.

Constraints:
- Inventory views, code-behind, view models, services, commands, and tests first.
- Separate verified findings from inferred risks.
- Prioritize business logic in views, provider calls from UI, missing command state, missing
  loading/error/empty states, and untestable workflow logic.
- Include resource risks such as sync I/O, blocking waits, unbounded collections, and polling.
- Recommend the smallest safe sequence of fixes.

Final summary must include findings, file references, suggested tests, and refactor order.
