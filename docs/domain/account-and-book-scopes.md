# Account and Book Scopes

**Status:** active
**Owner:** Accounting and Ledger
**Reviewed:** 2026-10-28

These definitions align the [domain model](../architecture/meridian-domain-model.md) with the
[design charter's object model](../product/meridian-design-document.md#10-core-business-object-model).
They describe required identity and ownership; they do not claim delivery of the planned W12
operator workflows or change existing persisted contracts.

## Definitions and Relationships

| Concept | Meaning and authority |
| --- | --- |
| Financial Account (`Account`) | Entity-scoped bank, custody, brokerage, operating, vendor, or customer container for assets, cash, obligations, and observed activity. Transactions and positions retain this identity. |
| Accounting Book (`Book`) | Entity-owned accounting record with a declared basis, functional currency, fiscal calendar, and policy versions. Its periods, chart of accounts, journals, and close state establish posting context. |
| Ledger Account | A debit/credit classification in a book's chart of accounts. Financial-account identity and ledger-account identity are separate; their policy-controlled mapping is explicit. |
| Accounting Period (`Period`) | A book-scoped interval whose review, close, lock, and reopen authority applies to postings and corrections. |
| Capital Account | Investor/owner economic subledger for contributions, allocations, distributions, fees, and balances. It reconciles to the applicable book and issued investor statement. |

An entity can own multiple financial accounts and books. A portfolio groups declared holdings and
activity; it does not replace the entity that owns the accounting book. Organization, fund,
portfolio, account, and consolidated reporting views retain their source books and perimeter.
An aggregate or operational record set cannot acquire posting authority merely by being labelled
a book in a view.

## Rules and Examples

- Every journal binds the server-owned entity, book, period, basis, currency, policy, and applicable
  dimensions. A financial-account assertion alone does not establish that posting authority.
- One custody account may map cash, receivables, and investments to different ledger accounts.
  The custody identifier and those ledger classifications remain separately inspectable.
- One economic event may have different treatments in two supported books. Each book retains
  its own approved postings and period authority; a combined view cannot conceal a missing side.
- Investor capital allocations retain participation and fee history and reconcile to ledger
  postings. An investor's capital account is not interchangeable with the capital ledger account.
- A consolidated report identifies its constituent entities and books. It does not silently
  grant permission to post into another entity's book.

Future implementations must define durable contracts, supported financial combinations, and
migration/compatibility rules before changing stored identities. Existing journal and approval
guards remain authoritative while the planned book and fund-operations workflows are delivered.
