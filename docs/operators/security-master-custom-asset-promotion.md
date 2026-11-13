# Security Master CustomAsset Promotion Operations

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-11-13

## Purpose and Scope

Use this procedure to move `CustomAsset` records that are pinned to a reclassifying profile onto
the first-class asset class that profile maps to ([ADR-022](../adr/022-canonical-asset-class-homes.md)).
For example, a record pinned to `private-fund-interest` is promoted to `PrivateFundInterest`.

The reclassifying profiles are the ids each class declares in
`SecurityAssetClassDescriptor.ReclassifyingProfileIds` (`src/Meridian.Contracts/SecurityMaster/SecurityAssetClassCatalog.cs`):

| Profile id | Target class |
| --- | --- |
| `structured-credit-io-po` | `StructuredCredit` |
| `private-fund-interest` | `PrivateFundInterest` |
| `private-company-equity` | `PrivateCompanyEquity` |
| `real-estate-holding` | `RealEstateHolding` |
| `commitment-guarantee` | `CommitmentGuarantee` |

Success means that each promoted record has the target class and one more version, keeps its
`SecurityId` and identifiers, still stores its profile envelope (`termsFormat` is `customProfile`),
and has a row in the target class's registry projection table instead of
`custom_asset_profile_field_projection` rows.

Boundaries:

- The report is read-only. Promotion is a normal governed amendment that resubmits the record's
  pinned envelope. There is no bulk-apply endpoint.
- A write without a terms patch (a common-terms-only amendment, such as a rename) and a deactivation
  keep a stored `CustomAsset` class. A record is never promoted as a side effect of a rename or a
  retirement.
- Any write that carries the pinned envelope **is** the promotion for a record pinned to a
  reclassifying profile. That includes an envelope amendment, an approved workbench field edit and
  a term-block patch: the last two rebuild a full envelope patch from the stored terms, so the write
  is decoded and validated under the target class. There is no way to edit profile fields on such a
  record and keep it `CustomAsset`. Repair and promotion are one amendment (step 3).
- Records pinned to a profile that does not reclassify (for example `co-invest-spv`) stay
  `CustomAsset`. To move one, repin it to a reclassifying profile in the same amendment, with that
  profile's fields.
- Legacy securitised `Bond` rows are not covered here. Use the structured-credit reclassification
  report (`POST /api/security-master/structured-credit-reclassification/report`) for those.

## Prerequisites

- A running host with Security Master storage configured (`MERIDIAN_SECURITY_MASTER_CONNECTION_STRING`;
  see [Database Persistence](../reference/environment-variables.md#database-persistence)). Without it
  the report route is not mapped and returns 404.
- An operator session or API key with `ViewSecurityMaster` and `AdminMaintenance` for the report,
  and `ModifySecurityMaster` for the amendment. Start from the
  [Operator Preflight](preflight-checklist.md) authentication steps.
- The target profile is approved in the asset-profile catalog at the amendment's effective time.
  The report flags a profile catalog failure as a blocker.

## Execution Context

- Platform: any supported host; the commands below use PowerShell 7.
- Working directory: repository root.
- Target: the host's loopback URL, for example `http://localhost:8080` for a local workstation host.
  Confirm you are pointed at the intended environment before step 4.

## Procedure

1. Sign in and keep the session, as in the [preflight](preflight-checklist.md):

   ```powershell
   $meridianBaseUrl = 'http://localhost:8080'
   # $operatorSession comes from the preflight login step.
   ```

2. Build the report for one profile:

   ```powershell
   $report = Invoke-RestMethod "$meridianBaseUrl/api/security-master/profile-promotion/private-fund-interest/report" `
       -Method Post -WebSession $operatorSession
   $report | Select-Object profileId, targetAssetClass, securitiesScanned, alreadyPromotedCount, candidateCount, readyCount, blockedCount
   $report.candidates | Select-Object securityId, displayName, version, disposition, @{ n = 'blockers'; e = { $_.blockers.code -join ', ' } }
   ```

3. Handle each `Blocked` candidate. Each blocker has a `code` and a `message`; see
   [Failure and Recovery](#failure-and-recovery). Do not repair profile fields with a separate
   field edit, term-block patch or envelope amendment first: on a record pinned to a reclassifying
   profile each of those writes is the promotion. A partial repair (for example, one of two missing
   fields) is refused under the target class, and a complete one promotes the record outside step 4.
   Choose one of:

   - **Repair and promote in one amendment.** Correct the envelope (`profileFields`, `category`,
     `subType`, `accountingClassification`) and submit the corrected envelope as the
     `assetSpecificTermsPatch` in step 4 instead of the unchanged one. Review the corrected envelope
     as part of that promotion.
   - **Hold the record as `CustomAsset`.** Repin it in one amendment to a profile that does not
     reclassify (for example `co-invest-spv`), with that profile's fields, and repair it there. It
     becomes a promotion candidate again only when you repin it to the reclassifying profile, which
     is itself a promotion amendment.
   - **Escalate.** If neither fits, raise it with the Security Master owner for an out-of-band data
     correction. Do not amend the record.

4. Promote one `Ready` candidate (or a `Blocked` one with its corrected envelope, from step 3).
   Read its current detail, then amend it with its pinned envelope and its current version:

   ```powershell
   $securityId = '<securityId from the report>'
   $detail = Invoke-RestMethod "$meridianBaseUrl/api/security-master/$securityId" -WebSession $operatorSession
   $body = @{
       securityId = $securityId
       expectedVersion = $detail.version
       commonTerms = $null
       assetSpecificTermsPatch = $detail.assetSpecificTerms   # the pinned envelope; corrected for a step 3 repair
       identifiersToAdd = @()
       identifiersToExpire = @()
       effectiveFrom = (Get-Date).ToUniversalTime().ToString('o')
       sourceSystem = 'operator'
       updatedBy = 'operator'   # the host records the signed-in actor instead
       sourceRecordId = $null
       reason = 'Promote CustomAsset to PrivateFundInterest (ADR-022)'
   } | ConvertTo-Json -Depth 20
   Invoke-RestMethod "$meridianBaseUrl/api/security-master/amend" -Method Post `
       -WebSession $operatorSession -ContentType 'application/json' -Body $body
   ```

   Check that the response's `assetClass` is the target class before you promote the next record.
   Promote records one at a time; each is its own event and can be reviewed on its own.

5. Run step 2 again. The promoted records now count in `alreadyPromotedCount` and are no longer
   candidates.

## Expected Results

- Step 2 returns `200` with a `SecurityMasterProfilePromotionReportDto`. Candidates are listed
  `Ready` first. A `Ready` candidate has no blockers.
- Step 4 returns `200` with the record's detail: `assetClass` is the target class and `version` is
  one higher than `expectedVersion`. The identifiers are unchanged.
- The new `TermsAmended` event's economic definition carries `legacyAssetClass` set to the target
  class, so a rebuild from events reproduces the promoted record.
- In PostgreSQL, the record's `custom_asset_profile_field_projection` rows are gone and the target
  table (for example `private_fund_interest_projection`) has one row for the `security_id`.

## Failure and Recovery

| Symptom | Diagnostic | Safe next action |
| --- | --- | --- |
| Step 2 returns `400` | The profile id is not in the table above. | Use a reclassifying profile id. A non-reclassifying profile has nothing to promote to. |
| Step 2 returns `403` | The session lacks `AdminMaintenance`. | Use an account with that permission. |
| Step 2 returns `404` | Security Master storage is not configured on this host. | Configure storage and restart, or use the host that owns Security Master. |
| Blocker `profile_promotion_target_decode_failed` | The envelope is missing a field the target class requires. | Add the field to `profileFields` in the envelope you submit at step 4 (repair and promotion are one amendment; see step 3). |
| Blocker `profile_promotion_classification_conflict` | The envelope's `category`, `subType` or `accountingClassification` names another class. | Correct the metadata in the envelope you submit at step 4 so it agrees with the profile (see step 3). |
| Blocker `profile_promotion_admission_refused` | Asset-pack admission for the target class refused the envelope (schema write validation or a schedule-terms write rule). | Correct the named field in the envelope you submit at step 4 (see step 3). |
| Blocker `profile_promotion_source_terms_unreadable` | The stored row does not decode as `CustomAsset`. | Stop. Raise it with the Security Master owner; do not amend the record. |
| Any other blocker code | A domain invariant or profile catalog issue for the target class; the message names the field. | Correct the field in the envelope you submit at step 4 (see step 3). |
| Step 4 returns `403` with the governed-amendment title | The deployment requires governed term amendments. | Make the same envelope amendment through the governed workbench route ([Governed Edit Rollout](security-master-governed-edit-rollout.md)). |
| Step 4 returns `400` with a refusal detail | The write path refused the promotion. The report does not run every write-path guard (it decodes read-tolerantly and skips the re-serialization round-trip guards), and the record or catalog may have changed after the report was built. | Read the detail, correct the envelope you submit, and rebuild the report before retrying. Nothing was written. |
| Step 4 fails with an expected-version conflict | Someone else amended the record after you read it. | Read the detail again and retry with the new version. |
| Step 4 response still says `CustomAsset` | The patch did not carry the envelope, or the envelope names a non-reclassifying profile. | Check the request body; resubmit with the pinned envelope. |

A promotion cannot be undone by deleting the event. To reverse one, amend the record again with an
envelope pinned to a profile that does not reclassify, which returns it to `CustomAsset`; that is a
new governed amendment with its own evidence.

## Evidence and Handoff

Retain, for each batch: the report JSON from step 2 before and after, the list of promoted
`securityId` values with the version before and after, and the operator who ran the amendments.
Remove credentials and session cookies from anything you retain. The Security Master owner accepts
the batch.

## Related References

- [ADR-022 canonical asset-class homes](../adr/022-canonical-asset-class-homes.md)
- [API reference: Security Master reclassification reports](../reference/api-reference.md#security-master-reclassification-reports)
- [Adding a Security Master asset class](../engineering/security-master-adding-an-asset-class.md)
- [Governed Edit Rollout](security-master-governed-edit-rollout.md)
- Source: `src/Meridian.Application/SecurityMaster/SecurityMasterProfilePromotionService.cs`,
  `src/Meridian.Application/SecurityMaster/SecurityMasterService.cs` (`GetProfileBackedAssetClassOverride`)
