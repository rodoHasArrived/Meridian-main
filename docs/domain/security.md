# Security

**Status:** active guidance  
**Owner:** core-team  
**Reviewed:** 2026-06-16

## Definition

A Security represents a financial instrument, investable asset, or fund interest that can appear in positions, transactions, valuations, reconciliations, and reports.

## Relationships

- Belongs to or references an issuer, obligor, counterparty, or fund sponsor when applicable.
- Can have positions across multiple portfolios.
- Can be referenced by transactions, prices, corporate actions, expected cash flows, and reconciliation evidence.
- Can flow into accounting events when valuation, income, realized gain/loss, or capital allocation rules require it.

## Business Rules

- A Security must have stable identity separate from provider-specific symbols.
- Provider symbols, CUSIPs, ISINs, tickers, and local IDs are identifiers for matching, not the canonical security itself.
- Security type drives required attributes, valuation rules, and reporting treatment.
- Security master changes that affect accounting or reconciliation require retained evidence and review state.

## Examples

- Public equity share
- Bond
- Private loan
- Derivative contract
- Fund interest

## Future Expansion Notes

Future additions should support richer issuer hierarchies, multi-identifier matching, corporate actions, expected cash-flow models, private-asset attributes, and fund-interest look-through behavior without coupling provider ingestion directly to accounting records.

## Price and projected-cash-flow evidence

A raw price observation has security, source, effective timestamp, recorded timestamp and explicit
quote units. Golden-copy selection uses both economic and knowledge cutoffs with a retained
hierarchy version. An untyped legacy quote or an assumed class-level par value is insufficient
valuation evidence.

An absent coupon or fixing is unresolved economics, distinct from contractual zero. A normalized
per-100 schedule is analysis only until actual principal/notional is retained. Bills and discount
commercial paper pay principal rather than coupon interest; carrying-value accretion is separate.
