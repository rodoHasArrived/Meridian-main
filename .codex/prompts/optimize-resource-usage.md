# Optimize Resource Usage

Objective: reduce memory, CPU, I/O, rendering, or lifecycle cost without changing user-visible
behavior unless explicitly requested.

Constraints:
- Measure or inspect the current bottleneck before editing.
- Prefer bounded collections, virtualization, cancellation, batching, and clear invalidation.
- Avoid speculative rewrites or new dependencies.
- Preserve behavior with characterization tests or focused existing tests.
- Run `resource-review.ps1` and relevant unit/UI tests after changes.

Final summary must include bottleneck evidence, selected fix, validation, and remaining tradeoffs.
