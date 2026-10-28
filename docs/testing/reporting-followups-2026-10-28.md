# Retained Reporting and Feed Follow-ups

**Status:** active review record
**Owner:** Core Team
**Reviewed:** 2026-10-28

This additive successor starts from public main `a6ad3fe1575530dc740167923ffd3bafeb27e7dd`.
It ports the unique authored deltas from #3175 commit/head
`a8a8c08c1ca3b902480c9e27343fd89bf6ec2552` and #3105 commit/head
`fa68bbdda46dfcb1814676147cad542dfe67ca54`. It does not bulk-merge their older snapshots or claim
those heads as Git ancestors. Earlier authored work is already consolidated in main.

| Source delta | Retained behavior and deliberate repair |
| --- | --- |
| #3175 local legacy feed backfill | Preserve genuine historical Plaid delivery continuity after restart. Replace bank-name/no-balance inference with exact complete batch and all-line MD5-to-Guid proof, account agreement, count, unique supported ASCII references and no retained balance. Duplicate batch metadata refuses upgrade. The original random-ID positive fixture represented an operator import rather than historical connector proof; fourteen restart cases now distinguish genuine history from unproven records. Existing guarded PostgreSQL migration 008 is untouched. |
| #3175 multi-day statement precedence | Retain the earliest-observed-transaction through retained-header bound, without inventing an earlier statement start. Bind suppression to account and currency, and refuse competing possible spans when they could change requested feed evidence. Keep unrelated currency/windows usable, highest-revision selection and the existing caller-level historical-as-of fence. |
| #3105 downloadable replay payload withholding | Copy only public retained-manifest and evidence-vault checkpoint representations with an empty replay payload. Preserve population identity/digest and all original private manifest/payload bytes used for replay and generated amount proof. Keep main's distinct composite artifact identities and every existing proof/replay regression. The new all-format theory also verifies the private source object and payload are unchanged. |

Generated outputs and old reviewed hashes on the source commits are intentionally superseded by
fresh narrowly reviewed module hashes after the additive changes. Other source deltas are not dropped.

## Validation boundary

A small C# compiler probe executes the two exact production selector/backfill files with actual
contract DTO definitions: nineteen cases pass. The original author's selector fails five controls on
the same inputs. This is focused executable source proof, not a project build or a substitute for
integration tests. Added file-restart and artifact-format xUnit cases await the combined .NET and
hosted gates. Independent execution and reporting reviews found no further concrete blocker in the
repaired source. No full .NET build was run in this lane.
