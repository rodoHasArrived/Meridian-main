# Meridian Codex Environment Tools

This repository exposes Codex environment entrypoints through:

- `scripts/ai/setup.sh`
- `scripts/ai/cleanup.sh`

The matching Codex environment config lives in `.codex/environments/environment.toml`.

## Script Access

Codex runs with workspace write sandboxing for this repository, and script execution may require
operator approval depending on the active approval policy.
You can execute repository scripts (after approval when prompted), for example:

```bash
bash scripts/ai/setup.sh
bash scripts/ai/cleanup.sh
python3 scripts/check_contract_compatibility_gate.py --help
pwsh ./scripts/dev/run-desktop.ps1 -Help
```

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

The browser workstation and the reactivated WPF desktop workstation are two active co-equal operator
UI lanes. Keep both behind shared contracts, local/web API endpoints, or shared read models so
neither client forks product state.
