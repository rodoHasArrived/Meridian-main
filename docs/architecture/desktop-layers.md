# Desktop & UI Layer Architecture

## Overview

Meridian now uses a **dual UI surface**:

1. **WPF Desktop (`Meridian.Wpf`)** for rich Windows-first operator workflows.
2. **Web Dashboard (`Meridian.Ui`)** for browser-based monitoring/configuration.

Both surfaces share contracts and application logic through shared libraries, with clear boundaries between platform host code and reusable UI functionality.

## Layer Diagram

```
┌────────────────────────────────────────────────────────────────────────────┐
│                          UI Host Layer                                    │
│  ┌────────────────────────────┐     ┌──────────────────────────────────┐  │
│  │ Meridian.Wpf    │     │ Meridian.Ui           │  │
│  │ (Windows desktop host)     │     │ (ASP.NET Core web host)          │  │
│  │ - XAML views/viewmodels    │     │ - Thin Program.cs host           │  │
│  │ - WPF-only services        │     │ - Serves dashboard/static assets │  │
│  └──────────────┬─────────────┘     └──────────────────┬───────────────┘  │
└─────────────────┼────────────────────────────────────────┼──────────────────┘
                  │                                        │
                  │                                        ▼
                  │                    ┌──────────────────────────────────┐
                  │                    │ Meridian.Ui.Shared    │
                  │                    │ - Endpoint mapping               │
                  │                    │ - Shared web UI services         │
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

### `src/Meridian.Wpf/` (Desktop host)

- Owns XAML views, viewmodels, and WPF shell/navigation.
- Registers DI container and composes page/service graph.
- Contains truly platform-specific implementations (theme, keyboard shortcuts, windowing, etc.).
- References `Meridian.Ui.Services` for shared UI/domain helpers.

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

1. **WPF host → Ui.Services**
2. **Desktop-local API host (`src/Meridian`) → Ui.Shared**
3. **Ui.Shared → Application + Contracts**
4. **Ui.Services → Contracts models (linked/shared consumption pattern)**
5. **All UI-facing layers → Contracts**

### ❌ Forbidden

1. **Ui.Services → WPF host types** (no dependency back into desktop UI shell)
2. **Ui.Shared → WPF-only APIs** (must stay host-agnostic)
3. **Host-to-host references** (`Wpf` ↔ `Ui`)
4. **Contracts → UI or application hosts**

## Communication Flow

### WPF path

```
View/Page (WPF)
   → WPF platform service (optional)
   → Ui.Services shared logic
   → Backend API / Application service endpoints
```

### Web path

```
HTTP Request
   → Ui host (Program.cs)
   → Ui.Shared endpoint/service mapping
   → Application services
   → Contracts DTO response
```

## Why this layering

- Keeps each host thin and focused on platform concerns.
- Avoids duplicating endpoint/configuration wiring between web surfaces.
- Preserves reusable business-facing UI logic in shared libraries.
- Supports future host additions (another desktop/web shell) with minimal coupling.

---

*Last Updated: 2026-03-31*
