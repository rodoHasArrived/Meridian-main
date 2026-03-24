# Meridian Test Patterns

Use this file to pick the right destination and test style quickly.

## Test Project Map

- `tests/Meridian.Tests/`: general backend, storage, providers, application services, endpoint coverage
- `tests/Meridian.FSharp.Tests/`: F# modules and interop-focused coverage
- `tests/Meridian.Ui.Tests/`: shared UI-service behavior
- `tests/Meridian.Wpf.Tests/`: WPF-specific behavior — **delayed implementation, not in active solution build**

## Component Routing

- Historical provider -> `tests/Meridian.Tests/`
- Streaming provider -> `tests/Meridian.Tests/`
- Storage sink / WAL / `AtomicFileWriter` -> `tests/Meridian.Tests/Storage/`
- Pipeline component -> `tests/Meridian.Tests/Application/Pipeline/`
- Pure application service -> `tests/Meridian.Tests/Application/Services/`
- UI service or config/status service -> `tests/Meridian.Ui.Tests/`
- WPF ViewModel or WPF-specific service -> `tests/Meridian.Wpf.Tests/` *(delayed — add to solution when WPF build resumes)*
- F# module or interop boundary -> `tests/Meridian.FSharp.Tests/`
- Endpoint integration -> `tests/Meridian.Tests/` integration areas

## Default Checklist

- Build a local `CreateSut()` helper where it improves readability.
- Keep assertions semantic and specific.
- Prefer deterministic fakes, mocks, and temp directories.
- Validate cleanup behavior when file handles, channels, sockets, or timers are involved.
