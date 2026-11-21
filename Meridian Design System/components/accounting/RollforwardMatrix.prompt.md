Compare standardized opening → movement → closing rollforwards across portfolios, bases, accounts, or periods. Mark closing rows as `total` and residual rows as `check` with a tolerance.

```jsx
<RollforwardMatrix columns={portfolios} rows={movementRows} />
```

Every column must use the same movement order and number convention so differences are typological, not layout artifacts.
