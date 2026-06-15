# Developer Setup (Current)

**Status:** active  
**Owner:** core-team  
**Reviewed:** 2026-06-14

This guide gives a concise setup path for day-to-day contributor work and agent task execution.

Current local project path: `D:\Meridian-main`.

## Prerequisites

- .NET SDK pinned by [`global.json`](../../global.json)
- PowerShell 7
- Node.js + npm (for `src/Meridian.Ui/dashboard`)
- Python 3 (`python3`)
- Git
- Optional: GNU Make (for Makefile workflows; Windows-only fallback commands are included)

## Fast Checkout Setup

```powershell
cd D:\Meridian-main
dotnet restore Meridian.sln /p:EnableWindowsTargeting=true
npm --prefix src/Meridian.Ui/dashboard install
npm install --prefix src/Meridian.Ui/dashboard
```

## Host and Workstation Bootstrap

```powershell
dotnet run --project src/Meridian/Meridian.csproj -- --quickstart
```

Use:

- `--quickstart` for a local default bootstrap flow.
- `--mode workstation --http-port 8080` for browser-workstation host mode.
- `--mode desktop --http-port 8080` for desktop-host mode.

## Optional Make Path

If Make is available in your environment, prefer:

```powershell
make setup-dev
```

When Make is not available, use the explicit command list above and in this lane.

## Notes

- Active runtime and repo documentation is in:
  - [`docs/start/README.md`](../start/README.md)
  - [`docs/engineering/README.md`](../engineering/README.md)
  - [`docs/operators/README.md`](../operators/README.md)
- Avoid running local builds against mutable OneDrive/Desktop path assumptions.
- Keep `config/appsettings*.json` and local secrets out of Git.
