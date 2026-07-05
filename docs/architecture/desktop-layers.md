# Desktop & Local API Layer Architecture

## Overview

Meridian uses an active browser workstation and a deferred WPF desktop workstation with local API surfaces:

1. **Browser workstation (`src/Meridian.Ui/dashboard`)** for browser operator workflow delivery, with built assets served from `src/Meridian.Ui/wwwroot/workstation/`.
2. **WPF Desktop (`Meridian.Wpf`)** as the deferred Windows desktop shell retained for compatibility and historical evidence.
3. **Desktop-local API host (`src/Meridian`)** for localhost-only workstation APIs, Swagger, and supporting background services.

These surfaces share contracts and application logic through shared libraries, with clear boundaries between the browser shell, WPF desktop shell, the local host, and reusable UI functionality.

## Layer Diagram

```
┌────────────────────────────────────────────────────────────────────────────┐
│                          UI Host Layer                                    │
│  ┌────────────────────────────┐     ┌──────────────────────────────────┐  │
│  │ Browser workstation        │     │ Meridian.Wpf + src/Meridian     │  │
│  │ (active operator UI)       │     │ (active desktop + API host)   │  │
│  │ - React/Vite dashboard     │     │ - XAML desktop shell      │  │
│  │ - /workstation assets      │     │ - localhost APIs + Swagger      │  │
│  └──────────────┬─────────────┘     └──────────────────┬───────────────┘  │
└─────────────────┼────────────────────────────────────────┼──────────────────┘
                  │                                        │
                  │                                        ▼
                  │                    ┌──────────────────────────────────┐
                  │                    │ Meridian.Ui.Shared              │
                  │                    │ - Endpoint mapping               │
                  │                    │ - Desktop-local API services     │
                  │                    │ - Host composition helpers       │
                  │                    └──────────────────┬───────────────┘
                  │                                        │
                  ▼                                        ▼
┌────────────────────────────────────────────────────────────────────────────┐
│                      Shared UI Services Layer                             │
│  ┌──────────────────────────────────────────────────────────────────────┐  │
│  │ Meridian.Ui.Services                                     │  │
│  │ - API/client orchestration                                          │  │
│  │ - Validation, fixture mode, notifications, config helpers           │  │
│  │ - Shared collections/contracts for desktop-facing features          │  │
│  └──────────────────────────────────────┬───────────────────────────────┘  │
└─────────────────────────────────────────┼──────────────────────────────────┘
                                          │
                                          ▼
┌────────────────────────────────────────────────────────────────────────────┐
│               Contracts + Backend Application Layers                      │
│  Meridian.Contracts  +  Application/Core/Domain/...            │
│  (DTOs, API contracts, orchestration, pipelines, providers, storage)      │
└────────────────────────────────────────────────────────────────────────────┘
```

## Project Responsibilities

### `src/Meridian.Ui/dashboard/` (Active browser workstation)

- Owns browser operator workflow surfaces and route-level workstation UX.
- Builds static workstation assets served from `src/Meridian.Ui/wwwroot/workstation/`.
- Consumes shared contracts, workstation endpoints, and browser view models instead of WPF shell state.

### `src/Meridian.Wpf/` (Active desktop host)

- Owns XAML views, viewmodels, and WPF shell/navigation.
- Registers DI container and composes page/service graph.
- Contains truly platform-specific implementations (theme, keyboard shortcuts, windowing, retained feature modules, etc.).
- References `Meridian.Ui.Services` for shared UI/domain helpers and desktop-local API clients.

#### WPF shell MVVM boundary

- `MainWindowViewModel` owns shell-level commands and transient shell state such as fixture/clipboard banners, collector actions, and status-bar orchestration.
- `MainPageViewModel` owns workstation workspace focus, current-page metadata, command-palette state, recent-page history, and shell navigation commands.
- `MainWindow.xaml.cs` and `Views/MainPage.xaml.cs` stay intentionally thin: they handle WPF-only concerns such as window lifecycle hooks, `Frame` initialization, focus management, drag/drop, and other visual-tree interactions that do not belong in reusable state.
- Navigation and shared operator behavior continue to flow through `Meridian.Wpf.Services` and `Meridian.Ui.Services`; code-behind should not become the source of truth for shell state.
- Detailed shell notes: see [WPF Shell MVVM](wpf-shell-mvvm.md).

### `src/Meridian.Ui.Shared/` (Desktop-local API shared module)

- Contains endpoint mapping and reusable local-host/service glue.
- Bridges the desktop-local API host to application/contract layers without duplicating wiring in each host.
- References `Meridian.Application` and `Meridian.Contracts`.

### `src/Meridian.Ui.Services/` (Cross-feature shared UI services)

- Shared service logic used by desktop workflows (API, fixture data, validation/utilities, etc.).
- Includes linked contract source files for desktop compatibility scenarios.
- Keeps platform-neutral behavior out of WPF-specific code.

### `src/Meridian.Contracts/` (Canonical contracts)

- Request/response DTOs, domain event models, enums, config models, API routes.
- Pure contract layer with no UI framework dependencies.

## Dependency Rules

### ✅ Allowed

1. **Browser dashboard → Ui.Shared endpoint contracts / local API**
2. **WPF host → Ui.Services**
3. **Desktop-local API host (`src/Meridian`) → Ui.Shared**
4. **Ui.Shared → Application + Contracts**
5. **Ui.Services → Contracts models (linked/shared consumption pattern)**
6. **All UI-facing layers → Contracts**

### ❌ Forbidden

1. **Ui.Services → WPF host types** (no dependency back into desktop UI shell)
2. **Ui.Shared → WPF-only APIs** (must stay host-agnostic)
3. **Browser dashboard → WPF shell state** (new operator workflows should be shared-contract/API-backed)
4. **WPF host → Ui.Shared endpoint mapping directly** (desktop UI should consume the local API seam or shared services, not re-host endpoint code)
5. **Contracts → UI or application hosts**

## Communication Flow

### Browser workstation path

```
Route/View (React)
   → Dashboard view model / API client
   → Desktop-local API (`src/Meridian`)
   → Ui.Shared endpoint/service mapping
   → Application services
```

### WPF path

```
View/Page (WPF)
   → WPF platform service (optional)
   → Ui.Services shared logic
   → Backend API / Application service endpoints
```

### Desktop-local API path

```
HTTP Request
   → local host (`src/Meridian`)
   → Ui.Shared endpoint/service mapping
   → Application services
   → Contracts DTO response
```

## Why this layering

- Keeps each host thin and focused on platform concerns.
- Keeps the desktop-local API host aligned with the browser workstation and WPF desktop shell without duplicating business workflow logic.
- Preserves reusable business-facing UI logic in shared libraries.
- Supports local tooling and automation against the same APIs without coupling them to WPF code.

---

*Last Updated: 2026-05-22*
