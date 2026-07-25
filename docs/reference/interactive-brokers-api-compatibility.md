# Interactive Brokers API Compatibility

**Status:** active runtime release evidence
**Owner:** core-team
**Reviewed:** 2026-07-22

## Supported release configuration

Meridian's real Interactive Brokers runtime is an opt-in build. Both
`EnableIbApiVendor` and its derived `EnableIbApiRuntime` default to `false`; a normal build does
not load an IB API assembly or claim live broker connectivity. The supported release build sets
`EnableIbApiVendor=true` and supplies **exactly one** official SDK input:

- `IBApiProjectPath` pointing to the official `CSharpAPI.csproj`, or
- `IBApiDllPath` pointing to the official `CSharpAPI.dll`.

The project fails closed when vendor mode is requested without an official project or DLL. The
compile-only `EnableIbApiSmoke=true` path is separate and must never be used as runtime evidence.

## Compatibility evidence

| Evidence | Current supported value | Enforcement |
| --- | --- | --- |
| Official client SDK baseline | TWS API 10.19 / client API server-version baseline 178 | `IBApiVersionValidator.MinSupportedClientVersion` and the protected runtime workflow build Meridian against the official project or DLL. |
| Minimum Gateway/TWS server | API server version 70 (TWS 966+) | Runtime rejects lower server versions before continuing. |
| Highest tested Gateway/TWS server | API server version 178 (TWS 10.19) | Higher versions stay fail-safe for routing but emit an explicit compatibility warning until this evidence is refreshed. |
| Runtime build evidence | Weekly and manually dispatched `IB API Official Runtime` workflow | Protected `interactive-brokers-paper` environment uses a self-hosted Windows runner with an unmodified official SDK. |
| Connectivity evidence | TCP reachability to the configured paper-only TWS/Gateway socket | The workflow fails if the configured paper endpoint cannot accept a socket connection. This is connectivity evidence, not order-placement evidence. |

## Protected integration environment contract

The `interactive-brokers-paper` GitHub environment must provide these non-secret variables on a
self-hosted runner labelled `Windows`:

| Variable | Meaning |
| --- | --- |
| `IB_API_PROJECT_PATH` | Absolute path to the installed official `CSharpAPI.csproj`; set this **or** `IB_API_DLL_PATH`. |
| `IB_API_DLL_PATH` | Absolute path to the installed official `CSharpAPI.dll`; set this **or** `IB_API_PROJECT_PATH`. |
| `IB_SMOKE_HOST` | Paper TWS/Gateway host reachable only from the protected runner. |
| `IB_SMOKE_PORT` | Paper TWS/Gateway API port (normally 7497 or 4002). |

Do not place the SDK, account credentials, session tokens, or endpoint secrets in this repository.
The workflow accepts exactly one SDK location and validates it before building. It runs only on a
scheduled/manual protected integration lane rather than on pull requests, so forks and ordinary
CI runners cannot access the official SDK or paper environment.

## Revalidation procedure

1. Upgrade the official SDK on the protected runner and record the TWS API release plus reported
   server version.
2. Dispatch **IB API Official Runtime** against `interactive-brokers-paper`.
3. Confirm the official runtime build succeeds and the paper socket smoke passes.
4. Complete the staged market-data, historical-bar, and paper-order checks in the
   [Interactive Brokers onboarding runbook](../operators/provider-onboarding-interactive-brokers.md).
5. If the tested server bound changes, update `IBApiVersionValidator`, this evidence record, and
   its tests in the same pull request.

## Entitlement-aware IB data services

The vendor runtime exposes `IBDataServices` for IB-specific data that is not safely represented as
a generic quote feed: scanner discovery, contract details, option-chain definitions, historical and
article news, fundamental reports, tick-by-tick subscriptions, live account P&L, market rules, and
depth-exchange metadata. Each request receives an `IBDataLineage` record before it is sent.

Persist that lineage with any materialized data. In particular retain the request/service identity,
contract or account, exchange, market-rule IDs and minimum-increment table, scanner/news/fundamental subscription descriptor,
IB-reported live/frozen/delayed data type, status, and observation time. Do not infer that data is
live because a request succeeded: permissions, exchange coverage, and delayed-data eligibility are
account-specific and can change during a session.
