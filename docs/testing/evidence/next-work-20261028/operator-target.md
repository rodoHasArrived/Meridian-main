# Recommended nonproduction operator target

**Status:** proposed target; no Windows host or retained population provisioned
**Source baseline:** `a5b2589f0395d054bec0b59672a990419ef237ed`

Use Andrew's normal Windows x64 development workstation first, provided it can run both
installed WPF and Chrome/Edge. Give this exercise a separate DataRoot and PostgreSQL database.
Point both clients at that one host and use the same application commit. This is the lowest-friction
way to prove shared state and compare browser/desktop behavior. Use a dedicated Windows VM instead
if that workstation cannot isolate its data or is needed for active production operations.
ARM64 and clean signed-installer lifecycle certification remain separate release exercises.

Use the existing [representative population and walkthrough](../../w10-mark-seam-operator-acceptance.md),
not a new fixture model: a dedicated nonproduction tenant/company, one entity, a USD ledger book,
an open October 2026 period, two owned accounts and two schedules with eight positions. Bind one
complete entity/book/account/period scope at a time in both clients. Keep the foreign-scope refusal
population separate. Capture actual service-returned identities; logical fixture names do not prove
retained ownership. Keep the walkthrough's declared valuation/evaluation dates and policy explicit.

Preserve implementation order LOT → MARK → SEAM → RECON. Within the joint live acceptance session,
retain the documented SEAM-before-MARK handshake:

1. Confirm same host/client SHA, operator identities, book currency, scope and period version.
2. Run LOT's remaining governed successor/amortization and consumer-parity cases through their
   owning services. Record compatibility-only consumers as deferred until canonical inputs exist.
3. For close readiness, retain both clients' blocked results for missing/stale/mismatched evidence,
   repair the owned contributor, and reload both against the same shared readiness result.
4. For MARK, enumerate the full eight-position denominator. Retain the five blocked positions
   (stale, undated, future, low-confidence and missing mark), two affected batches and no partial
   approval/journal creation. Repair owned evidence, repeat, and retain eight assessed/zero blocked.
5. Record attributed accept/reject/defer decisions, evidence hashes, screenshots and command IDs.

A development build can support preacceptance, but cannot substitute for signed installed release
proof. Do not select or tag an RC from this recommendation: signing prerequisites remain unavailable.
The current Linux environment cannot launch WPF or operate a user Windows workstation. Next input:
the chosen workstation/VM's reachable host identity and the operator's session; no credentials belong
in this receipt.
