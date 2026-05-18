# Provider Credential Management

Meridian provider credentials are managed through the Provider Connection Center in the browser
workstation Settings lane. The center is backed by the canonical provider connection API:

- `GET /api/providers/connections`
- `PUT /api/providers/{providerId}/credentials`
- `POST /api/providers/{providerId}/verify`
- `DELETE /api/providers/{providerId}/credentials`

These routes require `ManageCredentials`. Responses return credential state, masked key previews,
verification state, continuity health, affected workflows, and the next repair action. Raw secrets
are never returned.

## Local Encrypted Store

New credential saves write to an encrypted per-user vault under the resolved Meridian data root:

```text
<DataRoot>/.mdc/provider-credentials.vault
<DataRoot>/.mdc/provider-credentials.audit.jsonl
```

The vault is protected with the current Windows user profile when running on Windows. Non-Windows
hosts use a local profile key for encrypted storage. Audit entries record provider id, action,
actor, state, source, and field names, not secret values.

Do not store provider secrets in repo files, `appsettings.json`, generated docs, logs, screenshots,
or test fixtures. New browser flows must not mutate user-scoped environment variables.

Runtime data-provider construction reads the encrypted store first through
`StoredProviderCredentialResolver`. If no encrypted record is available, the runtime falls back to
the existing read-only environment/config resolver.

## Legacy Environment Fallback

Environment variables remain a read-only compatibility fallback for existing operator setups. If a
provider row shows `Legacy environment`, the recommended repair is to save the credential through
the Provider Connection Center so the encrypted Meridian store becomes the source of truth.

Deleting a provider credential removes the local encrypted value only. It does not clear process,
user, or machine environment variables.

## Compatibility Routes

The older credential endpoints remain as wrappers during migration:

- `GET /api/credentials`
- `GET /api/credentials/{provider}`
- `POST /api/credentials/{provider}`
- `DELETE /api/credentials/{provider}`
- `POST /api/credentials/{provider}/test`
- `POST /api/providers/{provider}/validate-credentials`
- `POST /api/providers/{provider}/test-connection`

These wrappers now save, delete, and verify through the shared provider store. They do not mutate
environment variables and they do not return raw secrets.

## Alpaca

Alpaca remains paper-first:

- Paper is the default credential environment.
- Live requires explicit acknowledgement in the Settings Alpaca panel.
- Paper verification uses `https://paper-api.alpaca.markets/v2/account`.
- The paper endpoint value `https://paper-api.alpaca.markets/v2` is accepted as a paper
  environment hint.
- `/api/brokerage-connections/alpaca/*` remains a compatibility route, but it now uses the shared
  credential store.
- Verification calls Alpaca `/v2/account` and records masked account evidence without logging or
  returning secrets.

Trading readiness, brokerage-sync blockers, and operator-inbox repair actions should route to
`/settings#alpaca-provider-setup` for Alpaca, or `/settings#provider-{providerId}-connection` for
other providers.
