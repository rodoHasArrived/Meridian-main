# Desktop End-User High-Value Improvement Shortlist

## Objective

Identify the highest-value improvements for the Windows desktop platform from an end-user perspective, prioritizing:

1. **Trust** (is the system working, and can users rely on what they see?)
2. **Workflow speed** (how quickly can users complete common tasks?)
3. **Operational resilience** (can users recover from errors, restarts, and provider issues?)

This document is intentionally concise enough for planning meetings while still specific enough for implementation sequencing.

---

## Target End-User Personas

| Persona | Primary Goal | Top Friction Today | What “better” looks like |
|---|---|---|---|
| Active Trader | Confirm live collection health quickly | Unclear if dashboard data is real vs simulated | Immediate confidence from live status, freshness, and provider badges |
| Quant Researcher | Run repeatable historical backfills | Interrupted jobs require manual restart/reconfiguration | Crash-safe resumable jobs with checkpoints and ETA |
| Data Engineer | Operate reliable ingestion pipelines | Debugging failures is slow and context-poor | Actionable errors with root-cause hints and guided remediation |
| Portfolio Analyst | Manage large symbol sets efficiently | Bulk edits/import validation are tedious | Fast import/validate/fix workflows with previews |
| New User | Reach first successful data capture quickly | Setup is broad and overwhelming | Role-based onboarding with sensible defaults |

---

## Prioritized Improvement Backlog (P0/P1/P2)

### P0 — Critical Trust and Continuity

| Priority | Improvement | End-User Value | Why It Matters | Indicative Effort |
|---|---|---|---|---|
| P0 | Replace demo/simulated values with live backend state by default | Very High | Users can trust what they see and quickly confirm system health. | M |
| P0 | Add resumable jobs with crash recovery for backfill/exports | Very High | Long-running work is not lost after restart/crash. | M-L |
| P0 | Show explicit staleness + source provenance on key metrics | Very High | Prevents decisions on stale/ambiguous data. | S-M |
| P0 | Add hard visual distinction for sample/offline mode | Very High | Eliminates confusion in test/demo environments. | S |

### P1 — Productivity and Incident Response

| Priority | Improvement | End-User Value | Why It Matters | Indicative Effort |
|---|---|---|---|---|
| P1 | Guided failure remediation (what failed, why, and next step) | High | Reduces recovery time and support burden. | M |
| P1 | First-run onboarding wizard with role presets (trader/researcher/ops) | High | Accelerates adoption and first success. | M |
| P1 | Workspace persistence (filters/layout/providers/recent pages) | High | Removes repeated session setup. | M |
| P1 | Command palette + global search (pages/actions/settings) | High | Faster navigation across broad feature set. | M |
| P1 | Alert quality upgrades (dedup/group/severity/quiet hours) | High | Reduces alert fatigue, keeps critical signal. | M |

### P2 — Scale and Polish

| Priority | Improvement | End-User Value | Why It Matters | Indicative Effort |
|---|---|---|---|---|
| P2 | Backfill planning UX (ETA, expected size/cost, fallback preview) | Medium-High | Better run planning and provider choices. | M |
| P2 | Bulk symbol workflows (import/validate/fix/preview) | Medium-High | Faster portfolio-scale setup and maintenance. | M |
| P2 | Export presets + output validation templates | Medium | Safer handoff to analysis/backtesting tools. | S-M |
| P2 | Offline-friendly diagnostics bundle (1-click collect/share) | Medium | Faster support loops and self-service debugging. | S-M |

---

## User Journey Improvements (Before → After)

### Journey 1: “Is the system running correctly?”

**Before:** Dashboard shows plausible numbers, but users cannot reliably tell if they are live or sample.  
**After:** Every KPI includes freshness timestamp, provider source badge, and stale-state highlighting; offline mode is visibly labeled.

### Journey 2: “My backfill failed halfway — now what?”

**Before:** User restarts manually, repeats configuration, and may duplicate work.  
**After:** Backfill resumes from checkpoint automatically, with clear resume/cancel choices and remaining ETA.

### Journey 3: “I got an error, what should I do next?”

**Before:** Generic technical message with unclear next action.  
**After:** Error panel includes classification, likely cause, one-click actions (Retry/Test Connection/View Logs), and playbook link.

### Journey 4: “I use this daily, let me move fast.”

**Before:** Repeated clicks through nested menus and recurring reconfiguration.  
**After:** Command palette, recent pages, remembered layout/filters, and keyboard-first navigation reduce repeated effort.

---

## Quick Wins (1–2 Weeks Each)

1. Add stale-data badges (e.g., **Updated 37s ago**) to dashboard cards.
2. Persist per-page filter/sort/column preferences.
3. Add recent-pages history and top-workflow keyboard accelerators.
4. Improve error dialogs with action buttons (**Retry**, **Test Connection**, **View Logs**).
5. Add environment labeling for **Real Data** vs **Sample Data**.
6. Add provider health chip (Connected/Degraded/Disconnected) on dashboard header.

---

## 90-Day Rollout Recommendation

### Month 1 (Trust First)

- Deliver P0 live-state integration on dashboard, symbols, and backfill status.
- Add freshness/staleness indicators and source provenance.
- Add explicit sample/offline mode labeling.

**Definition of done:** A user can determine in under 30 seconds whether collection is truly live and healthy.

### Month 2 (Continuity + Recovery)

- Deliver resumable jobs with checkpoint persistence.
- Introduce guided remediation flows for top 5 incident categories.
- Launch one-click diagnostics bundle export.

**Definition of done:** Interrupted backfills resume without reconfiguration and typical incidents provide actionable next steps.

### Month 3 (Speed + Discoverability)

- Add onboarding wizard role presets.
- Deliver command palette/global search.
- Enable workspace persistence and recent-pages shortcuts.
- Improve alert quality (dedup/grouping/quiet hours).

**Definition of done:** Frequent users complete common workflows with materially fewer clicks and less repeated setup.

---

## Success Metrics and Targets

| Metric | Baseline Signal | Target |
|---|---|---|
| Time-to-first-value (fresh live data visible) | Onboarding friction | **< 15 minutes** |
| Time to verify “system is live” | Trust ambiguity | **< 30 seconds** |
| Backfill recovery time after interruption | Continuity gap | **< 5 minutes** |
| Repeat config actions per session | Workspace friction | **-50% or better** |
| Support tickets: “is it really running?” | Trust confusion | **-40% or better** |
| Alert acknowledgement latency | Alert quality | **-30% or better** |
| Advanced feature adoption (search/palette/shortcuts) | Discoverability | **+2x within 90 days** |

---

## Instrumentation Suggestions

To ensure these improvements are measurable, track:

- Dashboard freshness badge interactions and stale-state duration.
- Backfill resume events, resume success rate, and checkpoint recovery time.
- Error-action button usage (Retry/Test Connection/View Logs) and follow-up success.
- Command palette invocation frequency and action completion rate.
- Workspace restore success rate on app relaunch.

All telemetry should be privacy-conscious and configurable.

---

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Live API latency causes flicker or false stale warnings | User distrust | Add hysteresis/debounce and clear thresholds |
| Resume checkpoints become incompatible across versions | Failed recoveries | Version checkpoint schema + migration path |
| Too many onboarding prompts overwhelm users | Drop-off | Provide “Quick Start” + “Advanced Mode” paths |
| Alert grouping hides important edge alerts | Missed incidents | Keep raw alert stream accessible with audit trail |

---

## Implementation Notes (Pragmatic)

- Prefer incremental rollout: polling-based freshness first, then streaming enhancements.
- Start with high-traffic pages (Dashboard, Backfill, Symbols) before long-tail pages.
- Use feature flags to test UI behavior with small cohorts before full release.
- Keep remediation playbooks short and task-oriented; avoid dense troubleshooting prose.

---

## Existing Evidence in This Repository

The broader desktop evaluation already documents key gaps and confirms that the largest opportunities are:

- Live backend integration
- Resumable workflows
- Onboarding and discoverability improvements
- Persistent workspace state

Reference: `desktop-end-user-improvements.md`.
