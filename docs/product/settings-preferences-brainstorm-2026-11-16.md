# Settings & Preferences Configurability Brainstorm (2026-11-16)

**Status:** brainstorm (decision input; not delivery or readiness evidence)
**Owner:** core-team
**Reviewed:** 2026-11-16
**Related direction:** `W8-WPF-PARITY-001`

> **Mode:** Domain-Focused / UX — what an operator can change inside the app (browser
> workstation and WPF desktop). Grounded in source at `main` `c9ca7382`; file anchors may drift.

## 1. What users can configure today

**Browser workstation preferences** (Settings > Preferences)
- The Appearance card (`src/Meridian.Ui/dashboard/src/screens/settings-screen.profile-sections.tsx:164`) has four settings:
  - Theme (`lib/theme.ts`, key `meridian.workstation.appearance.v1`)
  - Density (`meridian.workstation.density.v1`)
  - Display time zone (`settings-screen.time-zone-preference.tsx`, `lib/display-time-zone.ts`). Validates IANA zone names; defaults to UTC.
  - Page descriptions on/off (`meridian.workstation.pageDescriptions.v1`)
- All four are stored only in that browser's localStorage, so they don't follow the user to another browser.
- About 20 other `meridian.*.v1` localStorage keys hold preference-like state: layouts, open panes, pinned quote symbols (`meridian.liveMarketData.pinnedSymbolSet.v1`), price alerts (`meridian.priceAlerts.v1`), notifications, onboarding, operating context/recent scopes, companion panes, data query, table columns and others. Each module owns its own key; nothing lists them in one place.
- Saved layouts (`lib/saved-layouts.ts`) store route, scope, density and panes in a versioned envelope, capped at `MAX_SAVED_LAYOUTS = 40` per browser. Team presets are read-only and defined in code.

**Saved server-side, per operator**
- Default operating scope: GET/PUT in `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.OperatorScopePreference.cs`, through a file store, keyed by `ResolveOperatorPreferenceKey`.
- Table views (column set, search, CSV export): `components/meridian/table-workbench.tsx` with `WorkstationEndpoints.TableViews.cs`.
- These two are the only real cross-device preferences.

**Host configuration changeable from the UI**
- `ConfigEndpoints.cs`: data source, Alpaca, storage, symbols, derivatives. Requires `ModifyConfig`; secrets masked.
- `/api/config/effective` reports where each value came from (config file, `MDC_*` env var, or default).
- `/api/config/changes` + `SettingsChangeImpactBanner` (`settings-change-impact.tsx`, `ConfigurationChangeTracker.cs`) label each saved change Live, ReconnectProvider or RestartHost. Good foundation.

**WPF desktop**
- `SettingsPage.xaml`: Capabilities, Operations Control, Asset Profiles and more.
- `DesktopShellPreferences` (`src/Meridian.Ui.Services/Services/DesktopShellPreferences.cs`) is a record with one field, `ShellDensityMode`.
- `KeyboardShortcutService` (`src/Meridian.Wpf/Services/KeyboardShortcutService.cs`) registers 31 default shortcuts and has `UpdateShortcut` (:320), `SetShortcutEnabled` (:334) and `HasConflict` (:464), but nothing saves a changed binding.

**Only by editing a file**
- The `Settings` block in `config/appsettings.sample.json:847`: notification types, quiet hours (`QuietHoursEnabled` false, 22:00–07:00), `StatusRefreshIntervalSeconds` (2), `AccentColor` ("System"), `MaxReconnectAttempts` (10), service timeouts.
- `NotificationServiceBase.cs` reads the notification and quiet-hours values. No browser Settings control exists for them.
- `PaperTrading.Costs` (commission kind/rate, `SlippageBasisPoints` 5 in the sample; the code default is 0, as the sample's own comment warns): file only.

**Hard-coded**
- Browser keymap: `lib/keymap.ts` defines only `Mod+K` and `?`; not user-changeable.
- Poll intervals spread across ~13 non-test dashboard files (e.g. the 15 s change-impact poll, `settings-change-impact.tsx:54`).
- The common time-zone list.
- The month-name date format in `settings-screen.date-format.ts`.
- WPF config profiles (`SettingsConfigurationService.cs:41-43`): built-ins Strategy (id `research`), Low Latency and Archival. `CreateProfile` (:401) adds custom profiles to an in-memory `List<>` that is lost on restart.

## 2. Where users get stuck

1. **Preferences stay in one browser.** Theme, density, time zone, layouts and alerts are lost on a new machine or after clearing browser data, while default scope and table views do follow the operator. Inconsistent.
2. **Browser and WPF don't share preferences.** WPF density is `Standard`/`Compact`; the browser's is `compact`/`default`/`spacious`. WPF also has a theme service and `AccentColor`. Neither client reads the other's choices, against the shared-contracts direction of W8-WPF-PARITY-001.
3. **Notification settings need a file edit.** Quiet hours and per-type toggles exist in code but have no in-app control.
4. **Shortcut changes don't stick.** WPF can rebind but never saves; the browser can't rebind at all.
5. **Custom config profiles vanish** on WPF restart.
6. **No backup or move.** Nothing exports "my workstation setup" for a new machine or a teammate.
7. **Hard to find a setting.** No search by setting name in Settings; the command palette doesn't jump to individual settings.

## 3. Ideas

### Idea 1: Store operator preferences on the server
- **What:** `GET/PUT /api/workstation/operator-preferences` holding theme, density, time zone, page descriptions, notification preferences and the default-layout id. localStorage becomes a write-through cache.
- **Problem:** A quant sets Europe/London and compact density at the office; at home everything is back to UTC.
- **Fit:** Copy the `IOperatorScopePreferenceStore`/`FileOperatorScopePreferenceStore` pattern: sealed record DTO in `Meridian.Contracts.Workstation`, source-generated JSON context, atomic file writes, `context.RequestAborted` passed through, keyed by `ResolveOperatorPreferenceKey`. Browser hydrates on session start; if the server has no value, keep the local one and push it up.
- **Effort:** Medium. **Impact:** High.

### Idea 2: One preferences contract for browser and WPF
- **What:** Grow `DesktopShellPreferences` into a shared `OperatorPreferences` contract: one density vocabulary (WPF maps Spacious → Standard), one theme enum merging `AppTheme` and browser `Appearance`, time zone honoured by WPF formatters.
- **Problem:** WPF on one monitor and the browser on another show timestamps in two zones.
- **Fit:** Builds on Idea 1. Migrate legacy `IsCompactMode` payloads as the existing loader already does.
- **Effort:** Medium. **Impact:** High for the parity lane.

### Idea 3: Notifications & quiet-hours panel
- **What:** "Notifications" card under Settings > Preferences: per-type toggles (connection, errors, backfill complete, data gaps, storage), quiet hours in the operator's display time zone, a "test notification" button.
- **Problem:** Overnight backfill-complete toasts can only be silenced by editing `appsettings.json` and restarting.
- **Fit:** Fields already exist on the `NotificationServiceBase` settings. Store per operator through Idea 1, not as host config. Takes effect immediately, so the impact banner shows `Live`.
- **Effort:** Low–Medium. **Impact:** Medium–High.

### Idea 4: Rebindable, saved shortcuts
- **What:** WPF saves `UpdateShortcut` overrides into operator preferences. Browser adds `userOverrides` over `GLOBAL_KEY_BINDINGS`, with an editor on the keyboard-help sheet running a `HasConflict`-style check.
- **Problem:** `Ctrl+Shift+Q` "Stop collector" can clash with OS or remote-desktop shortcuts and can't be changed.
- **Guardrail:** High-consequence actions (`StartCollector`, `StopCollector`, `DeleteSelected` — currently bare `Delete` with no modifier — and anything that submits orders) must require a modifier and keep their confirmation dialogs.
- **Effort:** Medium. **Impact:** Medium.

### Idea 5: Export/import "My workstation" bundle
- **What:** Schema-versioned JSON bundle of preferences, user layouts, table views, pinned quote symbols and price alerts. Import offers "merge" or "replace" and shows a diff first.
- **Problem:** A two-person desk shares a reconciliation layout and column set; a user restores after a laptop rebuild.
- **Fit:** Reuse `lib/view-state-envelope.ts` and its defensive decoding. Export uses an allow-list of fields so credentials and host config can never be included. Later, team presets could be published from an imported bundle instead of living only in code.
- **Effort:** Medium. **Impact:** Medium–High.

### Idea 6: Searchable settings with per-setting deep links
- **What:** A settings registry (id, label, scope browser/operator/host, impact Live/Reconnect/Restart, required permission, route anchor) feeding the command palette and a search box on the Settings overview.
- **Problem:** Typing "time zone" or "slippage" into the palette returns nothing.
- **Fit:** Pairs with `settings-route-state.ts` and the existing `ConfigurationChangeImpact` vocabulary. A scope badge per entry ("this browser" / "you" / "whole host") also removes much of the scope confusion.
- **Effort:** Low–Medium. **Impact:** Medium.

### Idea 7: Saved, versioned config profiles with diff and rollback
- **What:** Persist WPF `ConfigProfile`s with the storage/backfill/derivatives values they apply; show a diff against effective config (using `/api/config/effective` source annotations) before applying; keep the last N applied configs for rollback.
- **Problem:** A user switches to "Low Latency" for an event day and can't get back to their tuned setup.
- **Fit:** Write through `ConfigStore` with atomic writes and an audit entry; keep behind `ModifyConfig` and run `ConfigValidatorCli` validation before apply.
- **Effort:** Medium–High. **Impact:** Medium.

## 4. Guardrails

- **Credentials and secrets** never go in preference stores or export bundles. Keep ADR-023 host-wide credential ownership and masking on read.
- **Financial settings stay host-level, permission-gated and audited:** `PaperTrading.Costs`; ledger mappings, approval policies, close calendar, asset profiles, canonicalization rules; risk limits. Never per-user overrides (two users would see different P&L). If surfaced, they need validation (e.g. slippage ≥ 0 bps), a reason field, a ConfigStore audit entry and a visible impact class.
- **Display settings never change stored data.** Time zone and date format are presentation only; UTC-clock accounting dates stay UTC, as `settings-screen.time-zone-preference.tsx` already states. Same for any future number/rounding preference.
- **Bundle imports and scope defaults** are checked against the importing user's permissions; an imported layout or scope pointing at inaccessible funds/accounts must not widen access.
- **Refresh and reconnect intervals** need lower bounds before exposure (no 0 s polls or unlimited reconnects against provider rate limits).
- **Storage, WAL and partition settings** stay `RestartHost`/`ModifyConfig` only.
- **Kill-switch and order-submission shortcuts:** see Idea 4.

## 5. Recommendation

**Top three to build next:**
1. **Server-side operator preferences (Idea 1)** — biggest friction, other ideas depend on it, and the operator-scope preference is a tested template.
2. **Notifications & quiet-hours panel (Idea 3)** — the backend exists; it only needs a control, and it removes a hand-edited file setting.
3. **One preferences contract for browser and WPF (Idea 2)** — directly advances W8-WPF-PARITY-001.

**Quick win (one day):** persist WPF shortcut overrides. *Update 2026-11-17: implemented —
overrides now persist with the desktop shell preferences and reapply at startup, with the
modifier guardrail from Idea 4 and a "Reset to defaults" button. Implementation found no in-app
rebinding editor, so rebinding itself still needs one (see Idea 4). Update 2026-11-17 (later):
the in-app rebinding editor on `KeyboardShortcutsPage` now captures chords, toggles and resets
individual shortcuts, and refuses guarded or conflicting chords inline.* Add an overrides dictionary to the `DesktopShellPreferences` record, save it through the existing `PersistDesktopShellPreferencesCore` (`SettingsConfigurationService.cs:557`), which already migrates legacy payloads, and add "Reset to defaults" on `KeyboardShortcutsPage`. No new endpoint needed.
