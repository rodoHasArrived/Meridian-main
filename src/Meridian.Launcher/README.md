# Meridian Launcher

Windows consumer entry point. It is intentionally a thin shim over the bundled
`Meridian.LifecycleSupervisor.exe`, which owns port selection, the dedicated database,
the host process, readiness, shutdown receipts, and browser launch. The installer creates
the single Start Menu entry for this binary.
