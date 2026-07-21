using System.Globalization;
using Meridian.Contracts.Api;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

public sealed partial class FinancialRecordExplorerReadService
{
    private async Task<IReadOnlyList<InstrumentJournalProof>> BuildInstrumentJournalProofsAsync(
        AssetOperationsDetailDto operations,
        CancellationToken ct)
    {
        var proofs = new List<InstrumentJournalProof>();
        foreach (var position in operations.BookPositions)
        {
            if (position.SecurityId != operations.Subject.SecurityId)
            {
                continue;
            }

            var lineage = new[] { position.ProjectionLineage }
                .Concat(operations.ProjectionLineages)
                .Where(static candidate => candidate is not null)
                .Select(static candidate => candidate!)
                .FirstOrDefault(candidate => IsAuthoritativeProjectionLineage(position, candidate));
            if (lineage is null ||
                !string.Equals(lineage.ModelKey, "mbs-factor-paydown", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var role = operations.InstrumentRoles.FirstOrDefault(candidate => candidate.RoleId == position.RoleId);
            var state = operations.PositionEconomicStates
                .Where(candidate => candidate.PositionId == position.PositionId)
                .OrderByDescending(static candidate => candidate.AsOfDate)
                .ThenByDescending(static candidate => candidate.Version)
                .FirstOrDefault() ?? position.CurrentEconomicState;
            var scopeMatches = IsAuthoritativeProjectionScope(position, role, state, lineage);
            AssetAccountingEventSpineDto? spine = null;
            if (_assetAccountingEventSpineService is not null && scopeMatches)
            {
                try
                {
                    var candidate = await _assetAccountingEventSpineService
                        .GetLatestAsync(lineage.TriggerEvent.EventId, lineage.TriggerEvent.EventVersion, ct)
                        .ConfigureAwait(false);
                    spine = candidate is not null && IsMatchingAssetAccountingSpine(position, lineage, candidate)
                        ? candidate
                        : null;
                }
                catch (NotSupportedException)
                {
                    spine = null;
                }
            }

            LedgerJournalEntryRecord? journal = null;
            if (_ledgerJournalStore is not null && spine?.PostedJournalImpact is not null)
            {
                try
                {
                    var journals = await _ledgerJournalStore.QueryAsync(
                        new LedgerJournalEntryQuery(
                            LedgerBookId: position.BookContext.LedgerBookId,
                            AggregateId: position.BookContext.LedgerBookId,
                            SourceEventId: lineage.TriggerEvent.EventId),
                        ct).ConfigureAwait(false);
                    journal = journals
                        .Where(candidate => IsMatchingDurableJournal(position, lineage, spine, candidate))
                        .OrderBy(static candidate => candidate.GlobalSequence)
                        .FirstOrDefault();
                }
                catch (NotSupportedException)
                {
                    journal = null;
                }
            }

            proofs.Add(new InstrumentJournalProof(position, role, state, lineage, spine, journal));
        }

        return proofs;
    }

    private static void AppendInstrumentJournalProofFields(
        List<FinancialRecordExplorerSummaryItemDto> fields,
        SecurityInstrumentEnrichment enrichment)
    {
        if (SelectDisplayedInstrumentJournalProof(enrichment) is not { } proof)
        {
            return;
        }

        fields.Add(new(
            "Factor Evidence",
            proof.Lineage.TriggerEvent.EvidenceLinks.FirstOrDefault() ?? "Missing",
            $"Source event {proof.Lineage.TriggerEvent.EventId:D}; content hash {proof.Lineage.TriggerEvent.SourceContentHash ?? "missing"}.",
            proof.Lineage.TriggerEvent.EvidenceLinks.Count > 0 ? FinancialRecordExplorerTone.Success : FinancialRecordExplorerTone.Warning));
        fields.Add(new(
            "Role / Position",
            $"{proof.Role?.RoleKind ?? "Unresolved"} / {proof.Position.PositionId:D}",
            $"Role {proof.Position.RoleId:D}; ledger book {proof.Position.BookContext.LedgerBookId:D}; position version {proof.Position.Version.ToString(CultureInfo.InvariantCulture)}.",
            proof.Role is null ? FinancialRecordExplorerTone.Warning : FinancialRecordExplorerTone.Success));
        fields.Add(new(
            "Accounting Projection",
            proof.Lineage.ModelKey,
            $"Projection run {proof.Lineage.ProjectionRunId:D}; projection event {proof.Lineage.ProjectionEventId?.ToString("D") ?? "none"}; trigger {proof.Lineage.TriggerEvent.EventId:D}; factors {proof.State?.PriorFactor?.ToString(CultureInfo.InvariantCulture) ?? "?"} to {proof.State?.CurrentFactor?.ToString(CultureInfo.InvariantCulture) ?? "?"}.",
            FinancialRecordExplorerTone.Info));

        if (TryGetAuthoritativeStage(proof, AssetAccountingLifecycleStageDto.Drafted, out var drafted))
        {
            fields.Add(new(
                "Posting Candidate",
                drafted.ReferenceId!,
                $"Typed Drafted lifecycle evidence was attested by {drafted.AttestedBy} at {drafted.AttestedAtUtc.ToString("u", CultureInfo.InvariantCulture)}.",
                FinancialRecordExplorerTone.Info));
        }

        if (TryGetAuthoritativeStage(proof, AssetAccountingLifecycleStageDto.Approved, out var approved))
        {
            fields.Add(new(
                "Approved",
                approved.ReferenceId!,
                $"Typed Approved lifecycle evidence was attested by {approved.AttestedBy} at {approved.AttestedAtUtc.ToString("u", CultureInfo.InvariantCulture)}.",
                FinancialRecordExplorerTone.Success));
        }

        var postedProof = SelectPostedJournalProof(enrichment);
        if (postedProof?.Journal is not { } journal)
        {
            return;
        }

        var currency = ResolveJournalCurrency(postedProof);
        var totalDebits = journal.Entry.Lines.Sum(static line => line.Debit);
        var totalCredits = journal.Entry.Lines.Sum(static line => line.Credit);
        fields.Add(new(
            "Posted Journal",
            journal.Entry.JournalEntryId.ToString("D"),
            $"Ledger book {postedProof.Position.BookContext.LedgerBookId:D}; accounting period {journal.PeriodId:D}; state Posted.",
            FinancialRecordExplorerTone.Success));
        fields.Add(new(
            "Journal Totals",
            $"{FormatAmountCurrency(totalDebits, currency)} debit / {FormatAmountCurrency(totalCredits, currency)} credit",
            $"Posted at {journal.CreatedAt.ToString("u", CultureInfo.InvariantCulture)}; balanced {journal.Entry.IsBalanced}; {journal.Entry.Lines.Count.ToString(CultureInfo.InvariantCulture)} immutable line{Plural(journal.Entry.Lines.Count)}.",
            journal.Entry.IsBalanced ? FinancialRecordExplorerTone.Success : FinancialRecordExplorerTone.Danger));
        fields.Add(new(
            "Journal Currency",
            currency,
            $"Functional currency for ledger book {postedProof.Position.BookContext.LedgerBookId:D}.",
            FinancialRecordExplorerTone.Info));
        fields.Add(new(
            "Balanced Journal Lines",
            journal.Entry.Lines.Count.ToString(CultureInfo.InvariantCulture),
            BuildJournalLineDetail(journal, currency),
            journal.Entry.IsBalanced ? FinancialRecordExplorerTone.Success : FinancialRecordExplorerTone.Danger));
    }

    private static void AppendInstrumentJournalProofImpacts(
        List<FinancialRecordExplorerRelationshipDto> impacts,
        WorkstationSecurityReference reference,
        SecurityInstrumentEnrichment enrichment)
    {
        if (SelectDisplayedInstrumentJournalProof(enrichment) is not { } proof)
        {
            return;
        }

        var assetHref = BuildAssetOperationsHref(reference.SecurityId);
        impacts.Add(new(
            "factor-evidence",
            "Factor evidence",
            $"{proof.Lineage.TriggerEvent.EvidenceLinks.FirstOrDefault() ?? "Missing evidence"}; source hash {proof.Lineage.TriggerEvent.SourceContentHash ?? "missing"}.",
            proof.Lineage.TriggerEvent.EvidenceLinks.FirstOrDefault() ?? BuildSecurityEvidenceHref(reference, enrichment),
            proof.Lineage.TriggerEvent.EvidenceLinks.Count > 0 ? FinancialRecordExplorerTone.Success : FinancialRecordExplorerTone.Warning));
        impacts.Add(new(
            "instrument-role-position",
            "Role / position",
            $"Role {proof.Position.RoleId:D}; position {proof.Position.PositionId:D}; ledger book {proof.Position.BookContext.LedgerBookId:D}.",
            assetHref,
            proof.Role is null ? FinancialRecordExplorerTone.Warning : FinancialRecordExplorerTone.Success));
        impacts.Add(new(
            "economic-projection",
            "Accounting projection",
            $"{proof.Lineage.ModelKey} {proof.Lineage.ModelVersion}; run {proof.Lineage.ProjectionRunId:D}; event {proof.Lineage.TriggerEvent.EventId:D}.",
            assetHref,
            FinancialRecordExplorerTone.Info));

        if (TryGetAuthoritativeStage(proof, AssetAccountingLifecycleStageDto.Drafted, out var drafted))
        {
            impacts.Add(new(
                "posting-candidate",
                "Posting candidate",
                $"Typed Drafted lifecycle evidence {drafted.ReferenceId}; actor {drafted.AttestedBy}; attested {drafted.AttestedAtUtc.ToString("u", CultureInfo.InvariantCulture)}.",
                assetHref,
                FinancialRecordExplorerTone.Info));
        }

        if (TryGetAuthoritativeStage(proof, AssetAccountingLifecycleStageDto.Approved, out var approved))
        {
            impacts.Add(new(
                "approval",
                "Independent approval",
                $"Typed Approved lifecycle evidence {approved.ReferenceId}; actor {approved.AttestedBy}; attested {approved.AttestedAtUtc.ToString("u", CultureInfo.InvariantCulture)}.",
                assetHref,
                FinancialRecordExplorerTone.Success));
        }

        var postedProof = SelectPostedJournalProof(enrichment);
        if (postedProof?.Journal is not { })
        {
            return;
        }

        impacts.Add(new(
            "journal",
            "Posted Journal",
            BuildPostedJournalDetail(postedProof),
            BuildSecurityJournalHref(enrichment),
            FinancialRecordExplorerTone.Success));
    }

    private static void AppendInstrumentJournalProofUsedIn(
        List<FinancialRecordExplorerRelationshipDto> relationships,
        WorkstationSecurityReference reference,
        SecurityInstrumentEnrichment enrichment)
    {
        if (enrichment.InstrumentJournalProofs.Count == 0)
        {
            return;
        }

        relationships.Add(new(
            "accounting-projection-proof",
            "Accounting projection proof",
            $"{enrichment.InstrumentJournalProofs.Count.ToString(CultureInfo.InvariantCulture)} typed role/position projection chain{Plural(enrichment.InstrumentJournalProofs.Count)} can be reconstructed by ledger book and source event.",
            BuildAssetOperationsHref(reference.SecurityId),
            FinancialRecordExplorerTone.Info));

        if (SelectPostedJournalProof(enrichment) is { Journal: not null } postedProof)
        {
            relationships.Add(new(
                "instrument-journal-proof",
                "Instrument-to-posted-journal proof",
                BuildPostedJournalDetail(postedProof),
                BuildSecurityJournalHref(enrichment),
                FinancialRecordExplorerTone.Success));
        }
    }

    private static void AppendInstrumentJournalProofGraph(
        List<FinancialRecordExplorerGraphNodeDto> nodes,
        List<FinancialRecordExplorerGraphEdgeDto> edges,
        FinancialRecordExplorerRowDto row,
        FinancialRecordExplorerRelationshipDto? journal)
    {
        var factorEvidence = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "factor-evidence");
        if (factorEvidence is null)
        {
            return;
        }

        var rolePosition = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "instrument-role-position");
        var economicProjection = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "economic-projection");
        var factorEvidenceNodeId = AddRelationshipGraphNode(nodes, row.RecordId, factorEvidence, "factor-evidence");
        var rolePositionNodeId = AddRelationshipGraphNode(nodes, row.RecordId, rolePosition, "instrument-role-position");
        var economicProjectionNodeId = AddRelationshipGraphNode(nodes, row.RecordId, economicProjection, "economic-projection");
        AddGraphEdge(edges, factorEvidenceNodeId, rolePositionNodeId, "supports", factorEvidence.Tone);
        AddGraphEdge(edges, rolePositionNodeId, economicProjectionNodeId, "projects", rolePosition?.Tone ?? FinancialRecordExplorerTone.Info);

        var postingCandidate = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "posting-candidate");
        var approval = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "approval");
        var previousNodeId = economicProjectionNodeId;
        if (postingCandidate is not null)
        {
            var postingCandidateNodeId = AddRelationshipGraphNode(nodes, row.RecordId, postingCandidate, "posting-candidate");
            AddGraphEdge(edges, previousNodeId, postingCandidateNodeId, "proposes", economicProjection?.Tone ?? FinancialRecordExplorerTone.Info);
            previousNodeId = postingCandidateNodeId;
        }

        if (approval is not null)
        {
            var approvalNodeId = AddRelationshipGraphNode(nodes, row.RecordId, approval, "approval");
            AddGraphEdge(edges, previousNodeId, approvalNodeId, "authorizes", approval.Tone);
            previousNodeId = approvalNodeId;
        }

        if (journal is null)
        {
            return;
        }

        var immutableJournalNodeId = AddRelationshipGraphNode(nodes, row.RecordId, journal, "journal");
        AddGraphEdge(edges, previousNodeId, immutableJournalNodeId, "posts", journal.Tone);
    }

    private static string BuildSecurityJournalHref(SecurityInstrumentEnrichment enrichment)
    {
        var journalEntryId = enrichment.InstrumentJournalProofs
            .Select(static proof => proof.Journal?.Entry.JournalEntryId)
            .FirstOrDefault(static id => id.HasValue);
        if (journalEntryId.HasValue)
        {
            return UiApiRoutes.WithQuery(
                UiApiRoutes.LedgerManualJournalEntryWorkbench,
                $"ledgerEntryId={Uri.EscapeDataString(journalEntryId.Value.ToString("D"))}");
        }

        return string.Empty;
    }

    private static string BuildAssetOperationsHref(Guid securityId)
        => UiApiRoutes.WithParam(UiApiRoutes.WorkstationAssetOperations, "securityId", securityId.ToString("D"));

    private sealed record InstrumentJournalProof(
        BookPositionDto Position,
        InstrumentRoleDto? Role,
        PositionEconomicStateDto? State,
        ProjectionLineageDto Lineage,
        AssetAccountingEventSpineDto? Spine,
        LedgerJournalEntryRecord? Journal);

    private static InstrumentJournalProof? SelectPostedJournalProof(SecurityInstrumentEnrichment enrichment)
        => enrichment.InstrumentJournalProofs.FirstOrDefault(static proof => proof.Journal is not null);

    private static InstrumentJournalProof? SelectDisplayedInstrumentJournalProof(SecurityInstrumentEnrichment enrichment)
        => SelectPostedJournalProof(enrichment) ?? enrichment.InstrumentJournalProofs.FirstOrDefault();

    private static bool IsAuthoritativeProjectionLineage(BookPositionDto position, ProjectionLineageDto lineage)
        => position.PositionId != Guid.Empty &&
           position.SecurityId != Guid.Empty &&
           lineage.ProjectionRunId != Guid.Empty &&
           HasText(lineage.ModelKey) &&
           HasText(lineage.ModelVersion) &&
           HasText(lineage.EngineVersion) &&
           HasText(lineage.Scenario) &&
           lineage.BookPositionId == position.PositionId &&
           lineage.TriggerEvent.BookPositionId == position.PositionId &&
           lineage.TriggerEvent.SecurityId == position.SecurityId &&
           lineage.TriggerEvent.EventId != Guid.Empty &&
           lineage.TriggerEvent.EventVersion > 0 &&
           HasText(lineage.TriggerEvent.EventType) &&
           HasText(lineage.TriggerEvent.SourceDomain) &&
           HasText(lineage.TriggerEvent.SourceEntityId) &&
           HasText(lineage.TriggerEvent.SourceContentHash);

    private static bool IsAuthoritativeProjectionScope(
        BookPositionDto position,
        InstrumentRoleDto? role,
        PositionEconomicStateDto? state,
        ProjectionLineageDto lineage)
    {
        if (position.BookContext.LedgerBookId == Guid.Empty ||
            position.BookContext.PeriodId is not { } periodId ||
            periodId == Guid.Empty ||
            !HasText(position.BookContext.BaseCurrency) ||
            role is null ||
            role.RoleId != position.RoleId ||
            role.SecurityId != position.SecurityId ||
            state is null ||
            state.PositionId != position.PositionId ||
            !string.Equals(state.Currency, position.BookContext.BaseCurrency, StringComparison.OrdinalIgnoreCase) ||
            state.SourceEvent is null ||
            !IsSameEconomicEvent(state.SourceEvent, lineage.TriggerEvent) ||
            state.ProjectionLineage is not null && !IsSameProjectionLineage(state.ProjectionLineage, lineage))
        {
            return false;
        }

        var dimensions = position.BookContext.Dimensions;
        return dimensions is not null &&
               dimensions.InstrumentId == position.SecurityId &&
               dimensions.PositionId == position.PositionId &&
               Guid.TryParse(dimensions.BookId, out var bookId) &&
               bookId == position.BookContext.LedgerBookId &&
               string.Equals(dimensions.FundId, position.BookContext.FundProfileId, StringComparison.Ordinal);
    }

    private static bool IsMatchingAssetAccountingSpine(
        BookPositionDto position,
        ProjectionLineageDto lineage,
        AssetAccountingEventSpineDto spine)
    {
        if (!AssetAccountingEventSpineValidator.IsValid(spine) ||
            spine.EventId != lineage.TriggerEvent.EventId ||
            spine.EventVersion != lineage.TriggerEvent.EventVersion ||
            spine.EffectiveDate != lineage.TriggerEvent.EffectiveDate ||
            spine.Scope.SecurityId != position.SecurityId ||
            spine.Scope.BookPositionId != position.PositionId ||
            spine.Scope.ExpectedBookPositionVersion != position.Version ||
            spine.Scope.LedgerBookId != position.BookContext.LedgerBookId ||
            position.BookContext.PeriodId is not { } periodId ||
            spine.Scope.PeriodId != periodId ||
            spine.Scope.AccountingBasis != position.BookContext.AccountingBasis ||
            !string.Equals(spine.Scope.FundProfileId, position.BookContext.FundProfileId, StringComparison.Ordinal) ||
            !string.Equals(spine.Currency, position.BookContext.BaseCurrency, StringComparison.OrdinalIgnoreCase) ||
            !IsSameEconomicEvent(spine.EconomicEvent, lineage.TriggerEvent) ||
            !IsSameProjectionLineage(spine.ProjectionLineage, lineage) ||
            spine.ProjectedEffect is not { } projected ||
            projected.ProjectionRunId != lineage.ProjectionRunId ||
            !spine.Stages.Any(static stage => stage.Stage == AssetAccountingLifecycleStageDto.Projected))
        {
            return false;
        }

        var dimensions = spine.Scope.Dimensions;
        return dimensions is not null &&
               dimensions.InstrumentId == position.SecurityId &&
               dimensions.PositionId == position.PositionId &&
               Guid.TryParse(dimensions.BookId, out var dimensionBookId) &&
               dimensionBookId == position.BookContext.LedgerBookId &&
               string.Equals(dimensions.FundId, position.BookContext.FundProfileId, StringComparison.Ordinal);
    }

    private static bool IsMatchingDurableJournal(
        BookPositionDto position,
        ProjectionLineageDto lineage,
        AssetAccountingEventSpineDto spine,
        LedgerJournalEntryRecord journal)
    {
        if (journal.Entry.JournalEntryId == Guid.Empty ||
            journal.AggregateId != position.BookContext.LedgerBookId ||
            journal.SourceEventId != lineage.TriggerEvent.EventId ||
            journal.PeriodId == Guid.Empty ||
            position.BookContext.PeriodId is not { } expectedPeriodId ||
            expectedPeriodId == Guid.Empty ||
            journal.PeriodId != expectedPeriodId ||
            journal.CreatedAt == default ||
            journal.AccountingBasis != position.BookContext.AccountingBasis ||
            !string.Equals(journal.AccountingPolicyId, position.BookContext.AccountingPolicyId, StringComparison.Ordinal) ||
            !string.Equals(journal.AccountingPolicyVersion, position.BookContext.AccountingPolicyVersion, StringComparison.Ordinal) ||
            journal.Entry.Metadata.SecurityId != position.SecurityId ||
            !Guid.TryParse(journal.Entry.Metadata.LedgerBook, out var metadataLedgerBookId) ||
            metadataLedgerBookId != position.BookContext.LedgerBookId ||
            journal.Entry.Metadata.EffectiveDate != lineage.TriggerEvent.EffectiveDate ||
            !HasMatchingEconomicEventTags(journal, position, lineage) ||
            !HasMatchingProjectionTags(journal, lineage) ||
            journal.Entry.Lines.Count == 0 ||
            !journal.Entry.IsBalanced ||
            !IsMatchingPostedImpact(spine, journal))
        {
            return false;
        }

        return journal.Entry.Lines.All(line =>
            line.EntryId != Guid.Empty &&
            line.Dimensions?.InstrumentId == position.SecurityId &&
            line.Dimensions.PositionId == position.PositionId &&
            Guid.TryParse(line.Dimensions.BookId, out var lineBookId) &&
            lineBookId == position.BookContext.LedgerBookId &&
            line.Currency is not null &&
               string.Equals(line.Currency.FunctionalCurrency, position.BookContext.BaseCurrency, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsMatchingPostedImpact(
        AssetAccountingEventSpineDto spine,
        LedgerJournalEntryRecord journal)
    {
        if (spine.PostedJournalImpact is not { } impact ||
            !PostedJournalImpactValidator.IsValid(impact) ||
            !TryGetAuthoritativeStage(spine, AssetAccountingLifecycleStageDto.Posted, out _) ||
            impact.JournalEntryId != journal.Entry.JournalEntryId ||
            impact.LedgerBookId != journal.AggregateId ||
            impact.PeriodId != journal.PeriodId ||
            impact.AccountingBasis != journal.AccountingBasis ||
            impact.PostedAtUtc != journal.CreatedAt ||
            !string.Equals(impact.Currency, spine.Currency, StringComparison.OrdinalIgnoreCase) ||
            impact.TotalDebits != journal.Entry.Lines.Sum(static line => line.Debit) ||
            impact.TotalCredits != journal.Entry.Lines.Sum(static line => line.Credit) ||
            impact.Lines.Count != journal.Entry.Lines.Count ||
            impact.Lines.Select(static line => line.LineId).Distinct().Count() != impact.Lines.Count)
        {
            return false;
        }

        return journal.Entry.Lines.All(line =>
        {
            var impactLine = impact.Lines.SingleOrDefault(candidate => candidate.LineId == line.EntryId);
            return impactLine is not null &&
                   string.Equals(impactLine.AccountId, line.Account.ToString(), StringComparison.Ordinal) &&
                   impactLine.Debit == line.Debit &&
                   impactLine.Credit == line.Credit &&
                   string.Equals(impactLine.Currency, line.Currency?.FunctionalCurrency, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static bool HasMatchingEconomicEventTags(
        LedgerJournalEntryRecord journal,
        BookPositionDto position,
        ProjectionLineageDto lineage)
        => JournalTagGuidEquals(journal, "sourceEventId", lineage.TriggerEvent.EventId) &&
           string.Equals(JournalTag(journal, "sourceEventType"), lineage.TriggerEvent.EventType, StringComparison.Ordinal) &&
           long.TryParse(JournalTag(journal, "sourceEventVersion"), NumberStyles.None, CultureInfo.InvariantCulture, out var eventVersion) &&
           eventVersion == lineage.TriggerEvent.EventVersion &&
           string.Equals(JournalTag(journal, "sourceEventDomain"), lineage.TriggerEvent.SourceDomain, StringComparison.Ordinal) &&
           string.Equals(JournalTag(journal, "sourceEventEntityId"), lineage.TriggerEvent.SourceEntityId, StringComparison.Ordinal) &&
           string.Equals(JournalTag(journal, "sourceEventContentHash"), lineage.TriggerEvent.SourceContentHash, StringComparison.Ordinal) &&
           JournalTagGuidEquals(journal, "securityId", position.SecurityId) &&
           JournalTagGuidEquals(journal, "bookPositionId", position.PositionId);

    private static bool HasMatchingProjectionTags(
        LedgerJournalEntryRecord journal,
        ProjectionLineageDto lineage)
        => JournalTagGuidEquals(journal, "projectionRunId", lineage.ProjectionRunId) &&
           JournalTagOptionalGuidEquals(journal, "projectionEventId", lineage.ProjectionEventId) &&
           string.Equals(JournalTag(journal, "projectionModelKey"), lineage.ModelKey, StringComparison.Ordinal) &&
           string.Equals(JournalTag(journal, "projectionModelVersion"), lineage.ModelVersion, StringComparison.Ordinal) &&
           string.Equals(JournalTag(journal, "projectionEngineVersion"), lineage.EngineVersion, StringComparison.Ordinal) &&
           string.Equals(JournalTag(journal, "projectionScenario"), lineage.Scenario, StringComparison.Ordinal);

    private static bool JournalTagGuidEquals(LedgerJournalEntryRecord journal, string key, Guid expected)
        => Guid.TryParse(JournalTag(journal, key), out var actual) && actual == expected;

    private static bool JournalTagOptionalGuidEquals(LedgerJournalEntryRecord journal, string key, Guid? expected)
        => expected.HasValue
            ? JournalTagGuidEquals(journal, key, expected.Value)
            : !HasText(JournalTag(journal, key));

    private static bool IsSameEconomicEvent(EconomicEventReferenceDto left, EconomicEventReferenceDto right)
        => left.EventId == right.EventId &&
           left.EventVersion == right.EventVersion &&
           left.EffectiveDate == right.EffectiveDate &&
           left.OccurredAtUtc == right.OccurredAtUtc &&
           left.SecurityId == right.SecurityId &&
           left.BookPositionId == right.BookPositionId &&
           string.Equals(left.EventType, right.EventType, StringComparison.Ordinal) &&
           string.Equals(left.SourceDomain, right.SourceDomain, StringComparison.Ordinal) &&
           string.Equals(left.SourceEntityId, right.SourceEntityId, StringComparison.Ordinal) &&
           string.Equals(left.SourceContentHash, right.SourceContentHash, StringComparison.Ordinal) &&
           left.CorrelationId == right.CorrelationId &&
           left.CausationId == right.CausationId &&
           left.EvidenceLinks.SequenceEqual(right.EvidenceLinks, StringComparer.Ordinal);

    private static bool IsSameProjectionLineage(ProjectionLineageDto left, ProjectionLineageDto right)
        => left.ProjectionRunId == right.ProjectionRunId &&
           left.ProjectionEventId == right.ProjectionEventId &&
           left.BookPositionId == right.BookPositionId &&
           string.Equals(left.ModelKey, right.ModelKey, StringComparison.Ordinal) &&
           string.Equals(left.ModelVersion, right.ModelVersion, StringComparison.Ordinal) &&
           string.Equals(left.EngineVersion, right.EngineVersion, StringComparison.Ordinal) &&
           string.Equals(left.Scenario, right.Scenario, StringComparison.Ordinal) &&
           left.ProjectionAsOfDate == right.ProjectionAsOfDate &&
           left.GeneratedAtUtc == right.GeneratedAtUtc &&
           string.Equals(left.SourceDomain, right.SourceDomain, StringComparison.Ordinal) &&
           string.Equals(left.SourceEntityId, right.SourceEntityId, StringComparison.Ordinal) &&
           string.Equals(left.TermsVersion, right.TermsVersion, StringComparison.Ordinal) &&
           string.Equals(left.TermsHash, right.TermsHash, StringComparison.Ordinal) &&
           left.SupersededRunId == right.SupersededRunId &&
           left.EvidenceLinks.SequenceEqual(right.EvidenceLinks, StringComparer.Ordinal) &&
           IsSameEconomicEvent(left.TriggerEvent, right.TriggerEvent);

    private static bool TryGetAuthoritativeStage(
        InstrumentJournalProof proof,
        AssetAccountingLifecycleStageDto stage,
        out AssetAccountingStageEvidenceDto evidence)
        => TryGetAuthoritativeStage(proof.Spine, stage, out evidence);

    private static bool TryGetAuthoritativeStage(
        AssetAccountingEventSpineDto? spine,
        AssetAccountingLifecycleStageDto stage,
        out AssetAccountingStageEvidenceDto evidence)
    {
        evidence = spine?.Stages.SingleOrDefault(candidate => candidate.Stage == stage)!;
        if (spine is null ||
            evidence is null ||
            !HasText(evidence.ReferenceId) ||
            !HasText(evidence.AttestedBy) ||
            evidence.AttestedAtUtc == default ||
            evidence.Evidence.Count == 0 ||
            evidence.Evidence.Any(item =>
                !RetainedEvidenceIdentityValidator.IsComplete(item) ||
                RequiresSourceEvidence(stage) && !spine.RetainedEvidence.Contains(item)))
        {
            return false;
        }

        return stage switch
        {
            AssetAccountingLifecycleStageDto.Drafted =>
                spine.Stages.Any(static candidate => candidate.Stage == AssetAccountingLifecycleStageDto.Projected),
            AssetAccountingLifecycleStageDto.Approved =>
                spine.Stages.Any(static candidate => candidate.Stage == AssetAccountingLifecycleStageDto.Drafted),
            AssetAccountingLifecycleStageDto.Posted =>
                spine.Stages.Any(static candidate => candidate.Stage == AssetAccountingLifecycleStageDto.Approved),
            _ => true
        };
    }

    private static bool RequiresSourceEvidence(AssetAccountingLifecycleStageDto stage)
        => stage is AssetAccountingLifecycleStageDto.Expected or
            AssetAccountingLifecycleStageDto.Projected or
            AssetAccountingLifecycleStageDto.Drafted;

    private static string ResolveJournalCurrency(InstrumentJournalProof proof)
        => proof.Journal?.Entry.Lines
               .Select(static line => line.Currency?.FunctionalCurrency)
               .FirstOrDefault(HasText)
           ?? proof.Position.BookContext.BaseCurrency.Trim().ToUpperInvariant();

    private static string BuildPostedJournalDetail(InstrumentJournalProof proof)
    {
        var journal = proof.Journal!;
        var currency = ResolveJournalCurrency(proof);
        var totalDebits = journal.Entry.Lines.Sum(static line => line.Debit);
        var totalCredits = journal.Entry.Lines.Sum(static line => line.Credit);
        return $"Journal {journal.Entry.JournalEntryId:D}; ledger book {proof.Position.BookContext.LedgerBookId:D}; period {journal.PeriodId:D}; state Posted; currency {currency}; debits {FormatAmountCurrency(totalDebits, currency)}; credits {FormatAmountCurrency(totalCredits, currency)}; posted {journal.CreatedAt.ToString("u", CultureInfo.InvariantCulture)}; balanced {journal.Entry.IsBalanced}; lines {BuildJournalLineDetail(journal, currency)}";
    }

    private static string BuildJournalLineDetail(LedgerJournalEntryRecord journal, string currency)
        => string.Join(
            "; ",
            journal.Entry.Lines.Select(line =>
                $"{line.EntryId:D} {line.Account}: debit {FormatAmountCurrency(line.Debit, currency)}, credit {FormatAmountCurrency(line.Credit, currency)}"));

    private static string? JournalTag(LedgerJournalEntryRecord? journal, string key)
        => journal?.Entry.Metadata.Tags is { } tags && tags.TryGetValue(key, out var value)
            ? value
            : null;
}
