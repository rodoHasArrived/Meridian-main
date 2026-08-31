# Meridian Competitive Landscape

Use this file when brainstorming against competitors or positioning Meridian's strengths. Categories
follow `docs/product/meridian-design-document.md` §1.4.

**The gap Meridian sells against:** portfolio systems *show* numbers but cannot prove them,
accounting systems *record* numbers without the operational evidence behind them, close-management
tools *track tasks* around numbers they do not own, and fund administrators *certify* numbers as a
service. Meridian sells the number with its proof attached.

## Useful Comparisons

- Close and controls managers (BlackLine, Trintech): strong certification, reviewer workflow, and
  close-calendar ergonomics, but they sit beside the ledger and certify work about data they do not own
- Fund administration suites (Carta, FundStudio, Investran-class): strong fund economics and LP
  statement quality, weaker general-purpose accounting and evidence chains; often fund-shaped at the root
- Asset servicing and portfolio accounting (SS&C Advent Geneva, eFront, Addepar-class): multi-book
  rigor, valuation discipline, deep report libraries; expensive and vendor-service dependent
- Ledger and payment APIs (Modern Treasury and similar): excellent double-entry and bank-format
  primitives, but the customer builds the operator product on top
- Market data and trading infrastructure (Bloomberg, Databento, Polygon; LEAN/Backtrader/Zipline):
  relevant to the Trading, Strategy, and Data lanes; none carry an accounting, close, or evidence lane

## Meridian Strengths

- Self-hosted and local-first, with no per-query cloud fees
- Owns the data, the reconciliation, and the ledger — so close state is derived, not asserted
- Evidence chain as a first-class product object: packets, manifests, provenance, approval history
- Truthful by construction: loud simulation labels, fail-closed persistence, `review-required` and
  `blocked` instead of plausible-looking numbers
- Decision-to-delivery continuity on one governed spine, from research through delivery
- Two co-equal operator lanes (browser and WPF desktop) over shared contracts
- AI/MCP integration points under governed-autonomy boundaries

## Good Borrowing Targets

- Certification rigor and preparer/approver separation UX from close managers
- LP statement, capital-call, and distribution-notice quality from fund administration suites
- Multi-book, consolidation, and elimination discipline from asset servicing platforms
- Idempotency and bank-format handling (BAI2, CAMT.053) from ledger APIs
- Data lineage and anomaly surfacing from institutional market-data vendors
- Packaging and client ergonomics polish from developer-first data products

## Bad Borrowing Targets

- Cloud-only assumptions with no local ownership
- Vendor-service-dependent configuration models
- Fund-shaped root data models that demote non-fund customers
- Checklist-only close tracking that asserts completion without record backing
- Any surface that presents unwired capability as finished
- Features that ignore Meridian's seven-root navigation, evidence discipline, operator workflow, or
  the co-equal browser and desktop lanes
