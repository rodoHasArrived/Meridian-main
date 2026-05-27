# Resource Management Checklist

- Large grids use virtualization, paging, or incremental loading.
- Data workflows avoid full materialization unless the dataset is bounded and documented.
- View models avoid duplicate copies of large result sets.
- Expensive filters, search, metrics, and telemetry updates are debounced or batched.
- File, database, and network operations are asynchronous and cancelable.
- Provider calls use retry/backoff only where appropriate and avoid noisy polling.
- Timers, event subscriptions, provider connections, streams, and disposables are released.
- Long operations surface progress, cancellation, failure, and recovery actions.
- Cache retention and invalidation rules are explicit.
- UI updates stay on the dispatcher boundary and avoid blocking the UI thread.
