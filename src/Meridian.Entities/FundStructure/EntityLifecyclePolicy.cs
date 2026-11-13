using Meridian.Contracts.FundStructure;

namespace Meridian.Entities.FundStructure;

/// <summary>Effective-dated lifecycle policy shared by agreement, posting and timeline readers.</summary>
public static class EntityLifecyclePolicy
{
    public static EntityLifecycleEligibilityDto Evaluate(
        EntityLifecycleRevisionDto? revision,
        DateTimeOffset effectiveAt,
        EntityLifecycleOperationDto operation,
        bool hasRetainedEvidence,
        bool correctionExceptionApproved = false)
    {
        if (!Enum.IsDefined(operation))
            return new(false, "operation-unknown", "The lifecycle operation is not supported.", null, revision?.Revision);
        if (revision is null)
            return new(false, "lifecycle-history-missing", "An evidenced entity lifecycle baseline is required.", null, null);
        var states = revision.Timeline.Where(state => state.EffectiveFrom <= effectiveAt
            && (!state.EffectiveTo.HasValue || effectiveAt < state.EffectiveTo.Value)).ToArray();
        if (states.Length != 1)
            return new(false, "lifecycle-history-unknown", "The entity lifecycle is unknown or ambiguous on the effective date.", null, revision.Revision);
        var status = states[0].Status;
        if (operation is EntityLifecycleOperationDto.Read or EntityLifecycleOperationDto.Consolidation)
            return new(true, "lifecycle-readable", "Retained entities remain visible, including residual balances.", status, revision.Revision,
                status is LegalEntityLifecycleStatusDto.Dissolved or LegalEntityLifecycleStatusDto.Merged);
        if (status is LegalEntityLifecycleStatusDto.Draft or LegalEntityLifecycleStatusDto.Forming)
            return new(false, "entity-not-operational", "Draft and forming entities cannot create material postings or activate agreements.", status, revision.Revision);
        if (status is LegalEntityLifecycleStatusDto.Active or LegalEntityLifecycleStatusDto.AmendmentPending or LegalEntityLifecycleStatusDto.Restructuring)
            return new(true, "entity-operational", "The entity is operational on the effective date.", status, revision.Revision,
                status != LegalEntityLifecycleStatusDto.Active);
        if (status == LegalEntityLifecycleStatusDto.WindingDown)
        {
            var closeOperation = operation is EntityLifecycleOperationDto.Settlement or EntityLifecycleOperationDto.ClosingExpense
                or EntityLifecycleOperationDto.Tax or EntityLifecycleOperationDto.Revaluation or EntityLifecycleOperationDto.Correction
                or EntityLifecycleOperationDto.AgreementSettlementAmendment;
            return new(closeOperation && hasRetainedEvidence,
                closeOperation ? hasRetainedEvidence ? "controlled-close-allowed" : "controlled-close-evidence-required" : "new-commitment-blocked",
                closeOperation ? hasRetainedEvidence ? "Retained evidence supports this controlled-close operation."
                    : "Winding-down operations require retained evidence of an existing obligation or controlled close."
                    : "Winding-down entities cannot create new commitments.", status, revision.Revision);
        }
        var approvedCorrection = operation == EntityLifecycleOperationDto.Correction && hasRetainedEvidence && correctionExceptionApproved;
        return new(approvedCorrection, approvedCorrection ? "approved-correction" : "entity-read-only",
            approvedCorrection ? "An approved correction exception governs this exact retained obligation."
                : "Dissolved and merged entities are read-only outside a separately approved correction exception.", status, revision.Revision);
    }

    public static void ValidateTimeline(Guid entityId, IReadOnlyList<EntityLifecycleStateDto> timeline)
        => EntityLifecycleRevisionIntegrity.ValidateTimeline(entityId, timeline);

    public static EntityLifecycleRevisionDto Seal(EntityLifecycleRevisionDto revision)
        => EntityLifecycleRevisionIntegrity.Seal(revision);

    public static void ValidateRevision(EntityLifecycleRevisionDto revision, EntityLifecycleRevisionDto? previous, long expectedRevision)
        => EntityLifecycleRevisionIntegrity.ValidateRevision(revision, previous, expectedRevision);
}
