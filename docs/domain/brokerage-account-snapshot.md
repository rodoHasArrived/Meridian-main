# Brokerage Account Snapshot

**Status:** active guidance
**Owner:** core-team
**Reviewed:** 2026-07-18

## Definition

A Brokerage Account Snapshot is provider-reported, point-in-time evidence of one external
brokerage or custody account. It captures account identity, cash, equity, buying power, margin,
restrictions, positions, and source freshness without becoming Meridian ledger truth or order
placement authority.

## Relationships

- Belongs to an organization account and can be linked to an entity, portfolio, book, or fund.
- References the provider connection, external account identifier, retrieval timestamp, retained
  source payload, and import or sync run.
- Can include positions, typed activity events, option lifecycle events, borrow state, and tax lots.
- Supports provider-to-ledger reconciliation, margin monitoring, execution readiness, close
  evidence, and audit reconstruction.

## Business Rules

- Provider-reported values and Meridian-calculated shadow values must remain distinguishable.
- Missing provider values are unknown, not zero.
- Position risk contributions carry nullable Meridian Security Master identity plus explicit
  provenance. A connector-only symbol is labeled `ProviderStatementSymbolUnresolved` until a
  governed Security Master match is available; the UI must not imply that symbol text is a
  resolved Meridian security identity.
- Margin regime is separate from legal or tax account kind; for example, a taxable brokerage
  account can be cash, Regulation T, or portfolio margin.
- Every snapshot retains an as-of timestamp and source authority. Stale or partial snapshots must
  remain visibly stale or partial.
- The provider remains authoritative for live buying power, margin requirements, restrictions, and
  liquidation posture. Meridian shadow calculations are review and pre-trade control evidence.
- Snapshot evidence can draft reconciliation or journal candidates, but cannot post, approve, or
  overwrite ledger records automatically.
- Activity pagination must either complete or fail closed. A truncated provider page cannot be
  presented as a complete account history.

## Examples

- Alpaca reports current equity, buying power, initial margin, maintenance margin, restrictions,
  positions, and a complete paginated activity window.
- An IB Flex activity report supplies account, cash, positions, financing charges, transfers,
  option lifecycle events, and tax-lot evidence, while a margin report supplies end-of-day margin
  requirements.
- A Margin Control Center compares provider maintenance margin with a Meridian shadow estimate and
  opens a reconciliation exception when the variance exceeds policy.

## Future Expansion Notes

Snapshots should support multi-account and multi-prime aggregation without flattening account
ownership or source authority. Intraday events can update provisional state, but end-of-day
statement evidence must certify or break that state explicitly.
