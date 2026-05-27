# Refactor Screen Into Shared Components

Objective: extract repeated screen-specific UI or view-model logic into reusable workstation
components without changing behavior.

Constraints:
- Inventory similar controls, templates, commands, view models, and tests first.
- Define behavior-preservation constraints before editing.
- Extract one seam at a time and keep the old behavior covered by tests.
- Avoid broad rewrites, renames, or formatting churn.
- Preserve operator copy, routes, automation IDs, and public contracts unless explicitly changed.
- Run `safe-refactoring` and a resource review before finalizing.

Final summary must include before/after ownership, behavior proof, rollback path, and residual risk.
