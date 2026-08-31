using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Storage.Ledger;

namespace Meridian.Storage.AssetOperations;

/// <summary>
/// One immutable version of the Asset Accounting Event Spine together with the canonical payload
/// identity retained by the append store.
/// </summary>
public sealed record AssetAccountingEventProjectionRecord(
    AssetAccountingEventSpineDto Projection,
    string CanonicalFingerprint);

public sealed record AssetAccountingEventAppendResult(
    AssetAccountingEventSpineDto Projection,
    string CanonicalFingerprint,
    bool WasReplay);

/// <summary>
/// Append-only Asset Operations projection store. Source-event versions never change; lifecycle
/// progress appends a new spine version and remains rebuildable rather than becoming ledger truth.
/// </summary>
public interface IAssetAccountingEventProjectionStore
{
    Task<AssetAccountingEventProjectionRecord?> GetAsync(
        Guid eventId,
        long eventVersion,
        long spineVersion,
        CancellationToken ct = default);

    Task<AssetAccountingEventProjectionRecord?> GetLatestAsync(
        Guid eventId,
        long eventVersion,
        CancellationToken ct = default);

    Task<AssetAccountingEventAppendResult> AppendAsync(
        AssetAccountingEventSpineDto projection,
        long expectedSpineVersion,
        long expectedBookPositionVersion,
        CancellationToken ct = default);
}

internal static class AssetAccountingEventProjectionRules
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void ValidateAppend(
        AssetAccountingEventSpineDto projection,
        long expectedSpineVersion,
        long expectedBookPositionVersion)
    {
        ArgumentNullException.ThrowIfNull(projection);

        var contractIssues = AssetAccountingEventSpineValidator.Validate(projection);
        if (contractIssues.Count > 0)
        {
            throw new InvalidOperationException(
                $"Asset accounting projection violates the canonical lifecycle contract: {string.Join(" ", contractIssues)}");
        }

        if (projection.EventId == Guid.Empty || projection.EconomicEvent.EventId != projection.EventId)
        {
            throw new InvalidOperationException(
                "Asset accounting projections require one matching source economic-event identity.");
        }

        if (projection.EventVersion <= 0 ||
            projection.EconomicEvent.EventVersion != projection.EventVersion)
        {
            throw new InvalidOperationException(
                "Asset accounting projections require one matching positive source-event version.");
        }

        if (expectedSpineVersion < 0 || projection.SpineVersion != expectedSpineVersion + 1)
        {
            throw new InvalidOperationException(
                "Asset accounting projections must append the next consecutive spine version.");
        }

        if (expectedBookPositionVersion <= 0 ||
            projection.Scope.ExpectedBookPositionVersion != expectedBookPositionVersion)
        {
            throw new InvalidOperationException(
                "Asset accounting projections require the asserted positive book-position version.");
        }

        if (projection.Scope.SecurityId == Guid.Empty ||
            projection.Scope.ExpectedSecurityVersion <= 0 ||
            projection.Scope.BookPositionId == Guid.Empty ||
            projection.Scope.LedgerBookId == Guid.Empty ||
            projection.Scope.PeriodId == Guid.Empty ||
            string.IsNullOrWhiteSpace(projection.Scope.FundProfileId))
        {
            throw new InvalidOperationException(
                "Asset accounting projections require security, position, book, period, and owner scope.");
        }

        if (projection.EconomicEvent.SecurityId != projection.Scope.SecurityId ||
            projection.EconomicEvent.BookPositionId != projection.Scope.BookPositionId ||
            projection.EconomicEvent.EffectiveDate != projection.EffectiveDate ||
            !string.Equals(
                projection.EconomicEvent.EventType,
                AssetAccountingEventTypeNames.For(projection.EventKind),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Asset accounting projection event identity, type, date, security, and position must match its scope.");
        }

        if (projection.ProjectionLineage.ProjectionRunId == Guid.Empty ||
            projection.ProjectionLineage.BookPositionId != projection.Scope.BookPositionId ||
            projection.ProjectionLineage.TriggerEvent.EventId != projection.EventId ||
            projection.ProjectionLineage.TriggerEvent.EventVersion != projection.EventVersion)
        {
            throw new InvalidOperationException(
                "Asset accounting projections require complete lineage for the same source event and book position.");
        }

        if (projection.RetainedEvidence.Count == 0 ||
            projection.RetainedEvidence.Any(static evidence =>
                RetainedEvidenceIdentityValidator.Validate(evidence).Count > 0))
        {
            throw new InvalidOperationException(
                "Asset accounting projections require complete typed retained evidence.");
        }

        if (projection.Stages.Count == 0 ||
            projection.Stages.GroupBy(static stage => stage.Stage).Any(static group => group.Count() > 1) ||
            projection.Stages.Any(static stage =>
                stage.AttestedAtUtc == default ||
                stage.AttestedAtUtc.Offset != TimeSpan.Zero ||
                string.IsNullOrWhiteSpace(stage.AttestedBy) ||
                stage.Evidence.Any(evidence => RetainedEvidenceIdentityValidator.Validate(evidence).Count > 0)))
        {
            throw new InvalidOperationException(
                "Asset accounting projections require unique, UTC, evidence-complete lifecycle attestations.");
        }

        var projectedStage = projection.Stages.Any(static stage =>
            stage.Stage == AssetAccountingLifecycleStageDto.Projected);
        if (expectedSpineVersion == 0 &&
            (projection.Stages.Count != 1 ||
             projection.Stages[0].Stage != AssetAccountingLifecycleStageDto.Expected ||
             projection.ProjectedEffect is not null))
        {
            throw new InvalidOperationException(
                "The initial asset accounting spine append must retain only the Expected attestation; Projected is a separate append.");
        }

        if (projectedStage &&
            (projection.ProjectedEffect is null ||
             projection.ProjectedEffect.ProjectionRunId != projection.ProjectionLineage.ProjectionRunId ||
             !string.Equals(
                 projection.ProjectedEffect.ModelKey,
                 projection.ProjectionLineage.ModelKey,
                 StringComparison.Ordinal) ||
             !string.Equals(
                 projection.ProjectedEffect.ModelVersion,
                 projection.ProjectionLineage.ModelVersion,
                 StringComparison.Ordinal) ||
             projection.ProjectedEffect.ProjectionAsOfDate != projection.ProjectionLineage.ProjectionAsOfDate ||
             !string.Equals(projection.ProjectedEffect.Currency, projection.Currency, StringComparison.OrdinalIgnoreCase) ||
             !projection.ProjectedEffect.IsBalanced))
        {
            throw new InvalidOperationException(
                "Asset accounting projections require a balanced Projected attestation matching model lineage.");
        }

        if (!projectedStage && projection.ProjectedEffect is not null)
        {
            throw new InvalidOperationException(
                "Projected accounting effect cannot be retained before the separate Projected attestation append.");
        }

        var hasPostedStage = projection.Stages.Any(static stage =>
            stage.Stage == AssetAccountingLifecycleStageDto.Posted);
        if (hasPostedStage != (projection.PostedJournalImpact is not null))
        {
            throw new InvalidOperationException(
                "Posted lifecycle attestation and durable journal impact must be appended together.");
        }

        var requiresTaxLotBatch = projection.EventKind is
            AssetAccountingEventKindDto.Acquisition or AssetAccountingEventKindDto.Disposal;
        if (hasPostedStage && requiresTaxLotBatch != projection.TaxLotMutationBatchId.HasValue)
        {
            throw new InvalidOperationException(requiresTaxLotBatch
                ? "Posted acquisition and disposal projections require an authoritative tax-lot mutation batch."
                : "Posted non-lot asset accounting projections cannot carry a tax-lot mutation batch.");
        }
        if (!hasPostedStage && projection.TaxLotMutationBatchId.HasValue)
        {
            throw new InvalidOperationException(
                "A tax-lot mutation batch can only be introduced with the first Posted attestation.");
        }

        if (projection.PostedJournalImpact is not null)
        {
            if (!PostedJournalImpactValidator.IsValid(projection.PostedJournalImpact))
            {
                throw new InvalidOperationException(
                    "Posted journal impact requires a valid independently retained Posted attestation.");
            }
        }
    }

    public static void ValidateStreamContinuity(
        AssetAccountingEventSpineDto previous,
        AssetAccountingEventSpineDto next)
    {
        var isExpectedToProjectedTransition =
            previous.Stages[^1].Stage == AssetAccountingLifecycleStageDto.Expected &&
            next.Stages.Count == previous.Stages.Count + 1 &&
            next.Stages[^1].Stage == AssetAccountingLifecycleStageDto.Projected;
        var projectedEffectChanged = !PayloadEquals(previous.ProjectedEffect, next.ProjectedEffect);
        if (previous.EventId != next.EventId ||
            previous.EventVersion != next.EventVersion ||
            previous.EventKind != next.EventKind ||
            previous.EffectiveDate != next.EffectiveDate ||
            previous.EventAmount != next.EventAmount ||
            !string.Equals(previous.Currency, next.Currency, StringComparison.Ordinal) ||
            !PayloadEquals(previous.Scope, next.Scope) ||
            !PayloadEquals(previous.EconomicEvent, next.EconomicEvent) ||
            !PayloadEquals(previous.ProjectionLineage, next.ProjectionLineage) ||
            !PayloadEquals(previous.RetainedEvidence, next.RetainedEvidence) ||
            (projectedEffectChanged &&
             !(isExpectedToProjectedTransition &&
               previous.ProjectedEffect is null &&
               next.ProjectedEffect is not null)) ||
            !PayloadEquals(previous.Correction, next.Correction))
        {
            throw new InvalidOperationException(
                "Asset accounting lifecycle appends cannot rewrite the immutable source projection.");
        }

        var previouslyDrafted = previous.Stages.Any(static stage =>
            stage.Stage == AssetAccountingLifecycleStageDto.Drafted);
        var nextDrafted = next.Stages.Any(static stage =>
            stage.Stage == AssetAccountingLifecycleStageDto.Drafted);
        if (previouslyDrafted)
        {
            if (!nextDrafted ||
                !PayloadEquals(previous.DraftedCandidate, next.DraftedCandidate) ||
                !PayloadEquals(previous.DraftedCandidateResult, next.DraftedCandidateResult) ||
                !string.Equals(
                    previous.DraftedCandidateFingerprint,
                    next.DraftedCandidateFingerprint,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    previous.DraftedCandidateResultFingerprint,
                    next.DraftedCandidateResultFingerprint,
                    StringComparison.Ordinal) ||
                !PayloadEquals(previous.DraftedLotMutation, next.DraftedLotMutation) ||
                !string.Equals(
                    previous.DraftedLotMutationFingerprint,
                    next.DraftedLotMutationFingerprint,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Asset accounting lifecycle appends cannot remove or rewrite retained Drafted candidate authority.");
            }
        }
        else if (nextDrafted &&
                 previous.Stages[^1].Stage != AssetAccountingLifecycleStageDto.Projected)
        {
            throw new InvalidOperationException(
                "Drafted candidate authority may only be established by the Projected-to-Drafted transition.");
        }

        var previouslyPosted = previous.Stages.Any(static stage =>
            stage.Stage == AssetAccountingLifecycleStageDto.Posted);
        var nextPosted = next.Stages.Any(static stage =>
            stage.Stage == AssetAccountingLifecycleStageDto.Posted);
        if ((previous.PostedJournalImpact is not null &&
             !PayloadEquals(previous.PostedJournalImpact, next.PostedJournalImpact)) ||
            (previous.PostedJournalImpact is null &&
             next.PostedJournalImpact is not null &&
             (previouslyPosted || !nextPosted)))
        {
            throw new InvalidOperationException(
                "Asset accounting lifecycle appends cannot remove or rewrite durable posted journal impact.");
        }

        if ((previous.TaxLotMutationBatchId.HasValue &&
             previous.TaxLotMutationBatchId != next.TaxLotMutationBatchId) ||
            (!previous.TaxLotMutationBatchId.HasValue &&
             next.TaxLotMutationBatchId.HasValue &&
             (previouslyPosted || !nextPosted)))
        {
            throw new InvalidOperationException(
                "Asset accounting lifecycle appends may set a tax-lot mutation batch only with the first Posted attestation and cannot rewrite it later.");
        }

        var priorStages = previous.Stages.ToDictionary(static stage => stage.Stage);
        foreach (var prior in priorStages)
        {
            if (next.Stages.FirstOrDefault(stage => stage.Stage == prior.Key) is not { } retained ||
                !PayloadEquals(prior.Value, retained))
            {
                throw new InvalidOperationException(
                    "Asset accounting lifecycle appends cannot remove or rewrite prior attestations.");
            }
        }

        var appendedStages = next.Stages.Skip(previous.Stages.Count)
            .Select(static stage => stage.Stage)
            .ToArray();
        AssetAccountingLifecycleStageDto[] expectedStages = previous.Stages[^1].Stage switch
        {
            AssetAccountingLifecycleStageDto.Expected =>
                [AssetAccountingLifecycleStageDto.Projected],
            AssetAccountingLifecycleStageDto.Projected =>
                [AssetAccountingLifecycleStageDto.Drafted],
            AssetAccountingLifecycleStageDto.Drafted =>
                [AssetAccountingLifecycleStageDto.Approved],
            AssetAccountingLifecycleStageDto.Approved =>
                [AssetAccountingLifecycleStageDto.Posted],
            AssetAccountingLifecycleStageDto.Posted =>
                [AssetAccountingLifecycleStageDto.Reconciled],
            AssetAccountingLifecycleStageDto.Reconciled =>
                [AssetAccountingLifecycleStageDto.Reported],
            _ => []
        };
        if (expectedStages.Length == 0 || !appendedStages.SequenceEqual(expectedStages))
        {
            throw new InvalidOperationException(
                "Asset accounting lifecycle append must add exactly one next canonical stage in its own durable spine version.");
        }
    }

    public static string Fingerprint(AssetAccountingEventSpineDto projection)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(projection, JsonOptions);
        return Sha256Digest.Compute(payload);
    }

    public static async Task ValidateDurablePostedImpactAsync(
        AssetAccountingEventSpineDto projection,
        ILedgerJournalStore? journalStore,
        CancellationToken ct)
    {
        if (projection.PostedJournalImpact is not { } impact)
        {
            return;
        }

        if (journalStore is null)
        {
            throw new InvalidOperationException(
                "Posted asset accounting impact requires a configured durable journal authority.");
        }

        var records = await journalStore.QueryAsync(
                new LedgerJournalEntryQuery(
                    LedgerBookId: projection.Scope.LedgerBookId,
                    PeriodId: projection.Scope.PeriodId,
                    AggregateId: projection.Scope.LedgerBookId,
                    SourceEventId: projection.EventId),
                ct)
            .ConfigureAwait(false);
        var matches = records
            .Where(record => record.Entry.JournalEntryId == impact.JournalEntryId)
            .ToArray();
        if (matches.Length != 1 || !MatchesDurableImpact(projection, matches[0], impact))
        {
            throw new InvalidOperationException(
                "Posted asset accounting impact did not resolve to one exact immutable journal in the asserted book and period.");
        }

        var requiresTaxLotBatch = projection.EventKind is
            AssetAccountingEventKindDto.Acquisition or AssetAccountingEventKindDto.Disposal;
        if (!requiresTaxLotBatch)
        {
            if (projection.TaxLotMutationBatchId.HasValue)
            {
                throw new InvalidOperationException(
                    "Posted non-lot asset accounting projections cannot carry a tax-lot mutation batch.");
            }

            return;
        }

        if (projection.TaxLotMutationBatchId is not { } mutationBatchId)
        {
            throw new InvalidOperationException(
                "Posted acquisition and disposal projections require an authoritative tax-lot mutation batch.");
        }

        AtomicTaxLotJournalResult? batch;
        try
        {
            batch = await journalStore
                .GetAtomicTaxLotPostingAsync(mutationBatchId, ct)
                .ConfigureAwait(false);
        }
        catch (NotSupportedException ex)
        {
            throw new InvalidOperationException(
                "Posted acquisition and disposal projections require a durable store that supports atomic tax-lot authority reads.",
                ex);
        }

        var expectedMutationKind = projection.EventKind == AssetAccountingEventKindDto.Acquisition
            ? AtomicTaxLotMutationKind.Acquisition
            : AtomicTaxLotMutationKind.Disposal;
        if (batch is null ||
            batch.MutationBatchId != mutationBatchId ||
            batch.MutationKind != expectedMutationKind ||
            batch.Journal.Entry.JournalEntryId != impact.JournalEntryId ||
            batch.Journal.GlobalSequence != matches[0].GlobalSequence ||
            batch.Journal.AggregateId != projection.Scope.LedgerBookId ||
            batch.Journal.PeriodId != projection.Scope.PeriodId ||
            batch.Journal.SourceEventId != projection.EventId ||
            batch.Mutations.Count == 0 ||
            batch.Mutations.Any(mutation =>
                mutation.MutationBatchId != mutationBatchId ||
                mutation.MutationKind != expectedMutationKind ||
                mutation.JournalEntryId != impact.JournalEntryId ||
                mutation.SourceEventId != projection.EventId ||
                mutation.SecurityId != projection.Scope.SecurityId ||
                mutation.BookPositionId != projection.Scope.BookPositionId))
        {
            throw new InvalidOperationException(
                "Posted tax-lot mutation batch did not resolve to the exact durable journal, event, book, period, security, and position authority.");
        }

        var projectedEvidence = projection.RetainedEvidence
            .Concat(projection.Stages.SelectMany(static stage => stage.Evidence))
            .Distinct()
            .ToArray();
        var batchEvidence = batch.RetainedEvidence.Distinct().ToArray();
        if (batchEvidence.Length != projectedEvidence.Length ||
            batchEvidence.Any(evidence => !projectedEvidence.Contains(evidence)))
        {
            throw new InvalidOperationException(
                "Posted tax-lot mutation batch evidence does not exactly match the retained lifecycle evidence.");
        }
    }

    private static bool MatchesDurableImpact(
        AssetAccountingEventSpineDto projection,
        LedgerJournalEntryRecord record,
        PostedJournalImpactDto impact)
    {
        if (record.AggregateId != projection.Scope.LedgerBookId ||
            record.PeriodId != projection.Scope.PeriodId ||
            record.SourceEventId != projection.EventId ||
            record.AccountingBasis != projection.Scope.AccountingBasis ||
            record.CreatedAt != impact.PostedAtUtc ||
            impact.LedgerBookId != record.AggregateId ||
            impact.PeriodId != record.PeriodId ||
            impact.AccountingBasis != record.AccountingBasis ||
            impact.PostingStatus != JournalPostingStatusDto.Posted ||
            record.Entry.Lines.Count != impact.Lines.Count ||
            record.Entry.Lines.Sum(static line => line.Debit) != impact.TotalDebits ||
            record.Entry.Lines.Sum(static line => line.Credit) != impact.TotalCredits)
        {
            return false;
        }

        return record.Entry.Lines.All(line =>
        {
            var matchingImpact = impact.Lines.SingleOrDefault(candidate => candidate.LineId == line.EntryId);
            return matchingImpact is not null &&
                   string.Equals(matchingImpact.AccountId, line.Account.ToString(), StringComparison.Ordinal) &&
                   matchingImpact.Debit == line.Debit &&
                   matchingImpact.Credit == line.Credit &&
                   string.Equals(
                       matchingImpact.Currency,
                       line.Currency?.FunctionalCurrency,
                       StringComparison.OrdinalIgnoreCase) &&
                   DimensionsMatch(matchingImpact.Dimensions, line.Dimensions);
        });
    }

    private static bool DimensionsMatch(
        Meridian.Contracts.Ledger.LedgerDimensionSetDto? expected,
        Meridian.Ledger.LedgerLineDimensionSet? actual)
    {
        if (expected is null || actual is null)
        {
            return expected is null && actual is null;
        }

        return string.Equals(expected.FundId, actual.FundId, StringComparison.Ordinal) &&
               string.Equals(expected.EntityId, actual.EntityId, StringComparison.Ordinal) &&
               string.Equals(expected.SleeveId, actual.SleeveId, StringComparison.Ordinal) &&
               string.Equals(expected.StrategyId, actual.StrategyId, StringComparison.Ordinal) &&
               string.Equals(expected.InvestorId, actual.InvestorId, StringComparison.Ordinal) &&
               string.Equals(expected.CapitalAccountId, actual.CapitalAccountId, StringComparison.Ordinal) &&
               expected.InstrumentId == actual.InstrumentId &&
               expected.PositionId == actual.PositionId &&
               string.Equals(expected.TaxLotId, actual.TaxLotId, StringComparison.Ordinal) &&
               string.Equals(expected.CostCenterId, actual.CostCenterId, StringComparison.Ordinal) &&
               string.Equals(expected.CounterpartyId, actual.CounterpartyId, StringComparison.Ordinal) &&
               string.Equals(expected.OrganizationId, actual.OrganizationId, StringComparison.Ordinal) &&
               string.Equals(expected.PortfolioId, actual.PortfolioId, StringComparison.Ordinal) &&
               string.Equals(expected.BookId, actual.BookId, StringComparison.Ordinal) &&
               string.Equals(expected.AccountId, actual.AccountId, StringComparison.Ordinal) &&
               string.Equals(expected.CustomerId, actual.CustomerId, StringComparison.Ordinal) &&
               string.Equals(expected.VendorId, actual.VendorId, StringComparison.Ordinal) &&
               string.Equals(expected.ProjectId, actual.ProjectId, StringComparison.Ordinal) &&
               expected.ExternalGlDimensions.OrderBy(static item => item.Key, StringComparer.Ordinal)
                   .SequenceEqual(actual.ExternalGlDimensions.OrderBy(static item => item.Key, StringComparer.Ordinal));
    }

    public static T Clone<T>(T value)
        => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JsonOptions), JsonOptions)!;

    public static bool PayloadEquals<T>(T left, T right)
        => JsonElement.DeepEquals(
            JsonSerializer.SerializeToElement(left, JsonOptions),
            JsonSerializer.SerializeToElement(right, JsonOptions));
}
