# Modularity Checklist

- Reuses an existing control, service, command, read model, DTO, or shell primitive when one fits.
- Adds a shared module only after confirming the pattern is repeated or likely to repeat.
- Keeps workspace-specific classes thin and delegates reusable behavior to shared services or view
  models.
- Avoids duplicate provider, research, trading, accounting, and diagnostics models.
- Keeps navigation, command state, disabled reasons, loading, error, and empty-state projection in
  view models or services.
- Names ownership boundaries clearly and keeps dependencies flowing inward through interfaces.
- Includes tests at the reusable seam, not only at the outer screen.
- Leaves a smaller, easier next refactor path than the code had before.
