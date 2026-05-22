# Developer Setup

**Status:** Active
**Owner:** Core Team
**Reviewed:** 2026-05-18

Meridian is developed from `D:\Meridian-main`.

## Prerequisites

- .NET SDK pinned by [global.json](../../global.json)
- PowerShell 7 for Windows helper scripts
- Node.js and npm for the browser workstation in `src/Meridian.Ui/dashboard`
- Python 3 for build and documentation automation
- Optional: GNU Make. On Windows, use the explicit commands below when `make`
  is unavailable.

## Restore

```powershell
dotnet restore Meridian.sln /p:EnableWindowsTargeting=true
npm install
npm --prefix src/Meridian.Ui/dashboard install
```

The root `npm install` restores build-tool dependencies. The dashboard install
restores the browser workstation dependencies.

## Configuration

Local runtime configuration resolves in this order:

1. `--config <path>`
2. `MDC_CONFIG_PATH`
3. `config/appsettings.json`

Use `config/appsettings.sample.json` as the template. Do not commit local
`appsettings*.json`, secrets, provider credentials, logs, or generated data.

## First Checks

```powershell
dotnet run --project src/Meridian/Meridian.csproj -- --help
dotnet run --project src/Meridian/Meridian.csproj -- --validate-config
```

For full repository conventions, see
[docs/development/repository-organization-guide.md](../development/repository-organization-guide.md)
and [docs/development/repository-rule-set.md](../development/repository-rule-set.md).

## Production-safe domain persistence defaults

Meridian now treats in-memory domain services as **explicit local-only opt-ins**.
Production-safe startup defaults require persistence-backed services for:

- Fund Structure (`IFundStructureService`)
- Fund Accounts (`IFundAccountService`)
- Direct Lending (`IDirectLendingService`)
- Banking (`IBankingService`)
- Money Market / Treasury (`IMoneyMarketFundService`, `IMmfLiquidityService`)

Set `MERIDIAN_USE_INMEMORY_GOVERNANCE=true` only for non-production fixture/dev workflows.
If `DOTNET_ENVIRONMENT` or `ASPNETCORE_ENVIRONMENT` is `Production`, startup now fails fast when
an in-memory governance profile is requested.
