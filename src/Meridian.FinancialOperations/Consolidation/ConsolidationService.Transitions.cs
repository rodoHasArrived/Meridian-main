using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Consolidation;

public sealed partial class ConsolidationService
{
    public async Task<ConsolidationCalculation> CalculateAsync(ConsolidationRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (transitions is null) return await CalculateUnroutedAsync(request, ct).ConfigureAwait(false);
        var book = await journals.GetLedgerBookAsync(request.EliminationBookId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The elimination book is unavailable.");
        var history = await transitions.ListAsync(request.OrganizationId, request.OwnershipRootId, ct).ConfigureAwait(false);
        var entering = history.SingleOrDefault(x => x.Request.NewEliminationBookId == request.EliminationBookId);
        if (entering is not null && request.AsOf < entering.Request.CutoverDate)
            throw new InvalidOperationException("The new reporting book is unavailable before its approved cutover date; use the predecessor's historical book.");
        if (history.Any(x => x.Request.PreviousEliminationBookId == request.EliminationBookId && request.AsOf >= x.Request.CutoverDate))
            throw new InvalidOperationException("This reporting book is historical from its approved cutover date; use the approved successor book.");
        if (history.Count > 0)
        {
            var effectiveRoute = history.Where(x => x.Request.CutoverDate <= request.AsOf).MaxBy(x => x.RootVersion);
            var expectedBook = effectiveRoute?.Request.NewEliminationBookId
                ?? history.MinBy(x => x.RootVersion)!.Request.PreviousEliminationBookId;
            if (request.EliminationBookId != expectedBook)
                throw new InvalidOperationException("Use the approved reporting book for this date; an unopened third book cannot bypass an approved route.");
        }
        if (entering is null)
        {
            foreach (var other in await journals.ListLedgerBooksAsync(book.FundProfileId, request.OwnershipRootId,
                FundStructureNodeKindDto.Fund, ct).ConfigureAwait(false))
            {
                if (other.LedgerBookId == book.LedgerBookId || other.AccountingBasis != AccountingBasisKindDto.Primary) continue;
                var retired = history.Any(x => x.Request.PreviousEliminationBookId == other.LedgerBookId && request.AsOf >= x.Request.CutoverDate);
                var notYetEffective = history.Any(x => x.Request.NewEliminationBookId == other.LedgerBookId && request.AsOf < x.Request.CutoverDate);
                if (retired || notYetEffective) continue;
                if ((await journals.QueryAsync(new LedgerJournalEntryQuery(LedgerBookId: other.LedgerBookId), ct).ConfigureAwait(false))
                    .Any(record => record.Entry.Metadata.Tags?.ContainsKey(EvidenceTag) == true ||
                        record.Entry.Metadata.IdempotencyKey?.StartsWith("consolidation:", StringComparison.OrdinalIgnoreCase) == true))
                    throw new InvalidOperationException("An established reporting book already exists for this root. Approve an explicit perimeter transition before using a replacement book.");
            }
        }
        var formerMembers = entering is null ? null : history.Where(x => x.RootVersion <= entering.RootVersion)
            .SelectMany(x => x.PreviousEntityIds).ToHashSet(StringComparer.Ordinal);
        var calculation = await CalculateUnroutedAsync(request, ct, formerMembers).ConfigureAwait(false);
        var members = calculation.Perimeter.Entities.Select(x => x.EntityId.ToString("D")).Order(StringComparer.Ordinal).ToArray();
        var expectedMembers = entering?.NewEvidence.EntityIds ?? calculation.Posted.OrderByDescending(EffectiveDate)
            .ThenByDescending(x => x.GlobalSequence).Select(x => ReadEvidence(x.Entry.Metadata.Tags?.GetValueOrDefault(EvidenceTag))?.EntityIds)
            .FirstOrDefault(x => x is not null);
        if (expectedMembers is not null && !members.SequenceEqual(expectedMembers.Order(StringComparer.Ordinal)))
            throw new InvalidOperationException("The reporting membership changed. Prepare a reviewed cutover into a new dedicated elimination book.");
        if (entering is null) return calculation;
        var lineage = history.Where(x => x.RootVersion <= entering.RootVersion).OrderBy(x => x.RootVersion)
            .Select(x => new ConsolidationTransitionLineageDto(x.TransitionId, x.ContentHash, x.Request.CutoverDate, x.Request.PreviousEliminationBookId)).ToArray();
        var evidence = calculation.Evidence with
        {
            TransitionLineage = lineage,
            SourceFingerprint = Hash(new { Original = calculation.Evidence.SourceFingerprint, TransitionLineage = lineage })
        };
        return calculation with { Evidence = evidence };
    }

    /// <summary>Only the reviewed transition service may inspect an unopened target before approval.</summary>
    internal Task<ConsolidationCalculation> CalculateForTransitionAsync(ConsolidationRequestDto request, CancellationToken ct,
        IReadOnlySet<string>? formerMembers = null)
        => CalculateUnroutedAsync(request, ct, formerMembers);

    internal static bool IsDedicatedConsolidationBook(LedgerBookRecord book)
        => book.FundStructureNodeKind == FundStructureNodeKindDto.Fund && book.AccountingBasis == AccountingBasisKindDto.Primary &&
            (book.AccountingPolicyId, book.AccountingPolicyVersion) is ("consolidation-v1", "w10-v1") or
                ("consolidation-v2", "w10-v2") or ("consolidation-v3", "w10-v3") or ("consolidation-v4", "w10-v4");
}
