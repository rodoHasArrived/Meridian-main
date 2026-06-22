# Analytics Diagrams

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-06-20

This folder groups committed analytics and simulation architecture diagrams. It is a routing index
for rendered assets and source DOT files; current product scope and implementation status remain in
the product, roadmap, and source documentation lanes.

## Diagram Set

| Diagram | Source | Rendered assets | Purpose |
| --- | --- | --- | --- |
| Backtesting engine | `backtesting-engine.dot` | `backtesting-engine.svg`, `backtesting-engine.png` | Replay, fill-model, portfolio simulation, metrics, and paper-readiness handoff flow. |

## Maintenance

- Keep DOT source, SVG, and PNG files together as a reviewed diagram triplet.
- Prefer updating the source DOT and rerendering assets instead of editing generated image output.
- Keep detailed Backtesting implementation guidance in `src/Meridian.Backtesting/README.md` and
  roadmap status in `docs/roadmap/data/*.yml`.
