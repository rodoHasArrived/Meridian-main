---
module_id: SRC-WPF
owner: Meridian
status: active
last_verified: 2026-05-21
---

# Meridian WPF (Retained Scope)

## Module Purpose
Retained desktop shell and compatibility surface for operator workflows that still run in WPF.

## Ownership and Runtime
- Owner: Meridian
- Runtime lane: Meridian WPF (Retained Scope)

## Dependencies and Integrations
- Provide desktop shell navigation and route hosting for retained workflows.
- Maintain compatibility posture alongside active browser workstation delivery.
- Dependency: `src/Meridian.Ui.Services`
- Dependency: `src/Meridian.Ui.Shared`

## Operational Notes
Keep retained desktop support aligned with shared contracts and governance posture.
Convention-based view-model wiring is handled by `Services/ViewModelViewResolver.cs`; shell pages
that follow the `*Page` to `*ViewModel` naming convention can receive a DI-constructed DataContext
without page-specific registration, while pages that set their own DataContext remain authoritative.
Runtime desktop capability toggles are declared by feature modules and surfaced in Settings through
the feature capability gate.
The retained Security Master page projects the workstation trust snapshot's `scheduleBook` and
`openLotReadModel` payloads into operator-visible schedule, factor, provenance, and open-lot review
sections.

<!-- GENERATED:MODULE_OVERVIEW BEGIN -->
Generated overview content is maintained by documentation automation.
<!-- GENERATED:MODULE_OVERVIEW END -->
