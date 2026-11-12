# guidelines/ — design references

The canonical rulebooks (each with a specimen `*.card.html` rendered in the Design System tab):

| Doc | Owns |
| --- | --- |
| `VISUAL_FOUNDATIONS.md` | the "why" of Programmed Institutionalism — color, type, surface, motion |
| `CONTENT_FUNDAMENTALS.md` | voice, casing, number formatting, evidence-first copy |
| `PATTERNS.md` | when to reach for each component; composition & decision trees; keyboard map |
| `VISUALIZATION_GRAMMAR.md` | analytical law — job model, reading path, series roles, plates, uncertainty, graphs, QA gate |
| `BRAND_GUIDELINES.md` | marks, wordmark, palette usage, do/don't |
| `ICONOGRAPHY.md` | the 24px line-icon set |
| `TOKEN_REFERENCE.md` | 3-tier token model, white-label entry points, which-token-when |
| `ACCESSIBILITY.md` | measured WCAG 2.2 AA conformance, keyboard/focus/ARIA matrix |
| `ENTITY_SCHEMAS.md` | shared wire shapes (Order, Fill, Alert, Run, Instrument) |
| `EXTENDING.md` | extension recipes + stable-vs-internal API surface + robustness gates |
| `STATES_AND_ANTIPATTERNS.md` | the five interface states and the eleven data conditions every surface owes; do→don't |
| `OPERATING_LANGUAGE.md` | the six operating primitives — context, selection, datum, continuity, figure, capsule — and the rule of use for each |
| `TIME_SEMANTICS.md` | the twelve time roles, the three clocks, formatting law, and what makes a date set impossible |
| `OBJECT_MODEL.md` | the sixteen financial objects and the ten-facet identity contract every one of them answers |
| `WORKSTATION_HIERARCHY.md` | context → object → evidence → consequence → action; how a full workstation screen is composed |
| `VALUE_ENVELOPE.md` | the one contract for what is known about a figure — condition, authority, evidence, clocks, materiality — its three disclosure tiers and five coherence rules |
| `TABULAR_CONTRACT.md` | one grid: capabilities as props, domain tables as column schemas, and arithmetic as a proof contract |
| `PROGRESSIVE_DISCLOSURE.md` | the two reading depths, what may never withdraw, the residue a collapsed region owes, and the row → drawer → screen drill ladder |
| `SESSION_CONTINUITY.md` | what a screen owes a **returning** operator — arrangement, traversal, delta — and why a layout may never carry data context |
| `ADAPTER_SEMANTICS.md` | the five outbound adapter postures, retry as a schedule rather than a spinner, verbatim errors, rotation as a clock |

Developer-facing docs (architecture, getting started, cookbook, performance, upgrading) live in `../docs/`. New guideline docs go **here**, never at the package root.
