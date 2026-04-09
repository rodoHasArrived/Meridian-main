# NYSE `rate-limit`

- Result: `bounded`
- Bound by: REST rate-limit handling is covered offline; live exchange throttling still needs a sanitized runtime note.
- Repo evidence: `NYSECredentialAndRateLimitTests` and the provider-confidence baseline.
- Wave 1 treatment: keep rate-limit behavior explicit instead of assuming it from generic resiliency code.
