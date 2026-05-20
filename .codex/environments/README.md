# Meridian Codex Environment Tools

This repository exposes Codex environment entrypoints through:

- `scripts/ai/setup.sh`
- `scripts/ai/cleanup.sh`

The matching Codex environment config lives in `.codex/environments/environment.toml`.

The active operator UI lane is the browser workstation. Use the host-served route
`http://localhost:8080/workstation/` for production-like smoke checks, and use
`src/Meridian.Ui/dashboard/` for Vite development, Vitest, and production bundle validation.

## Setup

```bash
bash scripts/ai/setup.sh
```

## Cleanup

```bash
bash scripts/ai/cleanup.sh
```

Both scripts support `--help` for optional flags.

## Browser Workstation

```bash
dotnet run --project src/Meridian/Meridian.csproj -- --mode desktop --http-port 8080
npm --prefix src/Meridian.Ui/dashboard run test
npm --prefix src/Meridian.Ui/dashboard run build
```

Retained WPF is still available for compatibility and regression work, but it is not the default
surface for new operator-facing implementation.
