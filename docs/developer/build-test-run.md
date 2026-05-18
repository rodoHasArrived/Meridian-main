# Build, Test, Run

**Status:** Active
**Owner:** Core Team
**Reviewed:** 2026-05-18

Use the narrowest command that covers your change.

## Build

```powershell
dotnet restore Meridian.sln /p:EnableWindowsTargeting=true
dotnet build Meridian.sln /p:EnableWindowsTargeting=true
```

For automation or concurrent local builds, prefer isolated output:

```powershell
python3 build/python/cli/buildctl.py build --project Meridian.sln --configuration Release --isolation-key cleanup-pass
```

## Test

```powershell
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "Category!=Integration" --logger "console;verbosity=normal"
dotnet test tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj --logger "console;verbosity=normal"
npm --prefix src/Meridian.Ui/dashboard run test
npm run ui:dashboard:test
```

Broaden to WPF, UI-service, MCP, or integration tests only when the touched
files require it.

## Run

Main local host and CLI:

```powershell
dotnet run --project src/Meridian/Meridian.csproj -- --mode desktop --http-port 8080
```

Browser workstation development server:

```powershell
npm --prefix src/Meridian.Ui/dashboard run dev
```

The browser workstation route is `http://localhost:<vite-port>/workstation/`
for Vite and `http://localhost:8080/workstation/` when served by the Meridian
host.

Retained WPF shell:

```powershell
pwsh ./scripts/dev/run-desktop.ps1
```
