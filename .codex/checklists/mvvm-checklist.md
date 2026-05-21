# MVVM Checklist

- XAML declares layout, bindings, resources, and visual states.
- Code-behind contains only view lifecycle glue, generated handlers, or control-specific adaptation.
- View models own selected item, command state, validation state, loading state, error state, empty
  state, progress, and operator-facing messages.
- Services own workflow orchestration, provider access, persistence, and long-running work.
- Commands prevent duplicate execution and expose disabled reasons when the operator needs them.
- Async flows accept and forward `CancellationToken` where the work can outlive a click.
- Tests cover view model state transitions and commands without launching the UI.
- Bindings to read-only view-model properties use `Mode=OneWay` where needed.
