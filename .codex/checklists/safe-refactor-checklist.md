# Safe Refactor Checklist

- Behavior preservation constraints are written before editing.
- Current behavior has a test, characterization test, snapshot, or manual evidence path.
- The refactor is split into small reversible steps.
- Public contracts, serialization shapes, route names, command names, and operator copy are preserved
  unless explicitly changed.
- Reusable code is extracted behind a clear seam before removing old code.
- Tests are updated after each meaningful step, not only at the end.
- No broad formatter, namespace churn, or unrelated cleanup is included.
- Rollback path and residual risks are recorded.
