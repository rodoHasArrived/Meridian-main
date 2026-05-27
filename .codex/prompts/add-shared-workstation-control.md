# Add New Shared Workstation Control

Objective: add a reusable WPF workstation control or primitive that solves a repeated operator UI
need.

Constraints:
- Prove the control has at least two real or near-term call sites.
- Inspect existing `Controls`, `Styles`, `Templates`, `Behaviors`, and view-model helpers first.
- Keep the control visual and binding-oriented; place state and decisions in a view model or adapter.
- Support accessibility, keyboard flow, automation IDs, and deterministic tests where applicable.
- Avoid memory-heavy templates, excessive bindings, or full visual-tree rebuilds.

Final summary must include intended call sites, API shape, tests, and resource considerations.
