# Robinhood `quote-polling`

- Result: `bounded`
- Bound by: the adapter uses REST polling; there is no public websocket replay surface to commit.
- Repo evidence: `RobinhoodMarketDataClientTests`, `RobinhoodExecutionPath_SubmitsOrderThroughStableExecutionSeam`.
- Wave 1 treatment: trust the offline polling seam, but keep runtime cadence and broker-session behavior as manual evidence.
