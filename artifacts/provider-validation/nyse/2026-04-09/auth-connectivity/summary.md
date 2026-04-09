# NYSE `auth-connectivity`

- Result: `bounded`
- Bound by: real NYSE OAuth and REST connectivity still require operator credentials outside CI.
- Repo evidence: `NYSECredentialAndRateLimitTests`, `NyseMarketDataClientTests`.
- Wave 1 treatment: keep auth/connectivity explicit and bounded rather than implying live credential proof from unit tests.
