# Interactive Brokers `market-data-entitlements`

- Result: `bounded`
- Bound by: IB entitlements depend on the operator account and TWS/Gateway configuration.
- Repo evidence: `IBRuntimeGuidanceTests` and the setup guide.
- Wave 1 treatment: never imply entitlement coverage from compile-only or simulation paths.
