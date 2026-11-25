using System.Globalization;
using Meridian.Contracts.Integrity;
using System.Text;
using Meridian.Contracts.Ledger;

namespace Meridian.FinancialOperations.Intercompany;

/// <summary>
/// Pure transitions for a reciprocal intercompany balance confirmation. Every step is checked against the exact
/// retained version and balance digest. Nothing here reads a store, posts a journal or blocks close.
/// </summary>
public static class IntercompanyBalanceConfirmationRules
{
    /// <summary>
    /// The digest both sides confirm: each side's book, currency, signed balances, retained journal identities and
    /// balance schedule hash. A change to any posted amount or contributing journal changes the digest.
    /// </summary>
    public static string Digest(Guid arrangementId, DateOnly asOf, IntercompanyBalanceConfirmationSideDto one, IntercompanyBalanceConfirmationSideDto two)
    {
        var text = new StringBuilder()
            .Append("intercompany-balance-confirmation-v1|").Append(arrangementId.ToString("D")).Append('|')
            .Append(asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        foreach (var side in new[] { one, two })
            text.Append('|').Append(side.EntityId.ToString("D")).Append(':').Append(side.LedgerBookId.ToString("D")).Append(':')
                .Append(side.TransactionCurrency).Append(':').Append(side.TransactionSignedBalance.ToString(CultureInfo.InvariantCulture)).Append(':')
                .Append(side.FunctionalCurrency).Append(':').Append(side.FunctionalSignedBalance.ToString(CultureInfo.InvariantCulture)).Append(':')
                .Append(string.Join(',', side.JournalEntryIds.Order().Select(id => id.ToString("D"))));
        return Sha256Digest.ComputeUtf8(text.ToString());
    }

    /// <summary>
    /// Pins both sides' current balances. A prior record for the same profile and date may be superseded only when its
    /// balances moved, or after a dispute was resolved; an unresolved dispute or a matching full confirmation stands.
    /// </summary>
    public static IntercompanyBalanceConfirmationDto Propose(IntercompanyBalanceConfirmationDto? current, Guid confirmationId, Guid requestId,
        string tenantId, string? companyId, Guid arrangementId, Guid agreementId, DateOnly asOf,
        IntercompanyBalanceConfirmationSideDto one, IntercompanyBalanceConfirmationSideDto two, string actor, DateTimeOffset at,
        string? partyOneCompanyId = null, string? partyTwoCompanyId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (one.TransactionCurrency != two.TransactionCurrency)
            throw new InvalidOperationException("Both sides must hold the agreement's balance in the same transaction currency.");
        var digest = Digest(arrangementId, asOf, one, two);
        if (current is not null)
        {
            if (current.Status == IntercompanyBalanceConfirmationStatusDto.Disputed)
                throw new InvalidOperationException("The balances are disputed. Resolve the dispute with a reason before proposing again.");
            if (current.BalanceDigest == digest && current.Status != IntercompanyBalanceConfirmationStatusDto.Resolved)
                throw new InvalidOperationException("These posted balances are already proposed for this date; confirm or dispute the retained proposal.");
        }
        var proposal = new IntercompanyBalanceConfirmationEventDto(Guid.NewGuid(), requestId, IntercompanyBalanceConfirmationActionDto.Propose, null, actor, at, digest, null);
        // The proposing company owns the record for its lifetime; a later proposal by the other side's company keeps it.
        return new(current?.ConfirmationId ?? confirmationId, tenantId, current is null ? companyId : current.CompanyId, arrangementId,
            agreementId, asOf, (current?.Version ?? 0) + 1, IntercompanyBalanceConfirmationStatusDto.Proposed, one, two,
            one.TransactionSignedBalance + two.TransactionSignedBalance, digest, [.. current?.Events ?? [], proposal])
        {
            PartyOneCompanyId = partyOneCompanyId,
            PartyTwoCompanyId = partyTwoCompanyId
        };
    }

    /// <summary>Applies a confirmation, dispute or resolution to the exact retained version.</summary>
    public static IntercompanyBalanceConfirmationDto Apply(IntercompanyBalanceConfirmationDto current,
        IntercompanyBalanceConfirmationActionRequest request, string actor, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (request.ExpectedVersion != current.Version)
            throw new InvalidOperationException("The confirmation changed since it was shown. Reload it before acting.");
        if (!string.Equals(request.ExpectedBalanceDigest, current.BalanceDigest, StringComparison.Ordinal))
            throw new InvalidOperationException("The pinned balances differ from the ones shown. Reload the confirmation before acting.");
        var reason = request.Reason?.Trim();
        var step = request.Action switch
        {
            IntercompanyBalanceConfirmationActionDto.Confirm => Confirm(current, request.Party, actor),
            IntercompanyBalanceConfirmationActionDto.Dispute => Dispute(current, request.Party, reason),
            IntercompanyBalanceConfirmationActionDto.Resolve => Resolve(current, request.Party, actor, reason),
            _ => throw new InvalidOperationException("Proposals are made from the posted balances, not applied to a retained version.")
        };
        var recorded = new IntercompanyBalanceConfirmationEventDto(Guid.NewGuid(), request.RequestId, request.Action,
            RecordedParty(current, request), actor, at, current.BalanceDigest,
            string.IsNullOrEmpty(reason) ? null : reason);
        return current with { Version = current.Version + 1, Status = step, Events = [.. current.Events, recorded] };
    }

    private static IntercompanyBalanceConfirmationStatusDto Confirm(IntercompanyBalanceConfirmationDto current,
        IntercompanyBalanceConfirmationPartyDto? party, string actor)
    {
        if (party is not { } side)
            throw new InvalidOperationException("State which side is confirming its balance.");
        if (current.NetTransactionBalance != 0m)
            throw new InvalidOperationException("The two sides' posted balances do not net to zero. Dispute them or correct the books, then propose again.");
        var other = side == IntercompanyBalanceConfirmationPartyDto.PartyOne
            ? IntercompanyBalanceConfirmationStatusDto.ConfirmedByPartyTwo : IntercompanyBalanceConfirmationStatusDto.ConfirmedByPartyOne;
        var own = side == IntercompanyBalanceConfirmationPartyDto.PartyOne
            ? IntercompanyBalanceConfirmationStatusDto.ConfirmedByPartyOne : IntercompanyBalanceConfirmationStatusDto.ConfirmedByPartyTwo;
        if (current.Status == IntercompanyBalanceConfirmationStatusDto.Proposed)
            return own;
        if (current.Status == other)
        {
            // Reciprocal confirmation means two independent sign-offs, one per entity.
            if (SinceProposal(current).Any(item => item.Action == IntercompanyBalanceConfirmationActionDto.Confirm
                && string.Equals(item.Actor, actor, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("The same person cannot confirm both sides of a reciprocal balance.");
            return IntercompanyBalanceConfirmationStatusDto.Confirmed;
        }
        throw new InvalidOperationException(current.Status == own
            ? "This side has already confirmed the pinned balances."
            : $"A {current.Status} confirmation cannot be confirmed. Propose the current balances again.");
    }

    private static IntercompanyBalanceConfirmationStatusDto Dispute(IntercompanyBalanceConfirmationDto current,
        IntercompanyBalanceConfirmationPartyDto? party, string? reason)
    {
        if (party is null)
            throw new InvalidOperationException("State which side disputes the balance.");
        if (string.IsNullOrEmpty(reason))
            throw new InvalidOperationException("A dispute needs a reason.");
        return current.Status is IntercompanyBalanceConfirmationStatusDto.Proposed or IntercompanyBalanceConfirmationStatusDto.ConfirmedByPartyOne
            or IntercompanyBalanceConfirmationStatusDto.ConfirmedByPartyTwo
            ? IntercompanyBalanceConfirmationStatusDto.Disputed
            : throw new InvalidOperationException($"A {current.Status} confirmation cannot be disputed. Propose the current balances again.");
    }

    /// <summary>
    /// The party a step is recorded against. A same-company resolution settles both sides at once and names none; a
    /// cross-company resolution is recorded per side, because neither company may settle the other's position.
    /// </summary>
    public static IntercompanyBalanceConfirmationPartyDto? RecordedParty(IntercompanyBalanceConfirmationDto current,
        IntercompanyBalanceConfirmationActionRequest request)
        => request.Action == IntercompanyBalanceConfirmationActionDto.Resolve && !current.IsCrossCompany ? null : request.Party;

    private static IntercompanyBalanceConfirmationStatusDto Resolve(IntercompanyBalanceConfirmationDto current,
        IntercompanyBalanceConfirmationPartyDto? party, string actor, string? reason)
    {
        if (current.Status != IntercompanyBalanceConfirmationStatusDto.Disputed)
            throw new InvalidOperationException("Only a disputed confirmation can be resolved.");
        if (string.IsNullOrEmpty(reason))
            throw new InvalidOperationException("A resolution needs a reason that records how the dispute was settled.");
        var disputeIndex = current.Events.ToList().FindLastIndex(item => item.Action == IntercompanyBalanceConfirmationActionDto.Dispute);
        var dispute = current.Events[disputeIndex];
        if (string.Equals(dispute.Actor, actor, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The person who raised the dispute cannot also resolve it.");
        if (!current.IsCrossCompany)
            return IntercompanyBalanceConfirmationStatusDto.Resolved;
        // Across companies each side records its own resolution; the dispute stands until both have.
        if (party is not { } side)
            throw new InvalidOperationException("State which side is recording its resolution of a cross-company dispute.");
        var resolved = current.Events.Skip(disputeIndex + 1)
            .Where(item => item.Action == IntercompanyBalanceConfirmationActionDto.Resolve).Select(item => item.Party).ToHashSet();
        if (resolved.Contains(side))
            throw new InvalidOperationException("This side has already recorded its resolution of the dispute.");
        return resolved.Count == 0 ? IntercompanyBalanceConfirmationStatusDto.Disputed : IntercompanyBalanceConfirmationStatusDto.Resolved;
    }

    /// <summary>The sides that have recorded a resolution of the current cross-company dispute.</summary>
    public static IReadOnlyList<IntercompanyBalanceConfirmationPartyDto> PartiesThatResolved(IntercompanyBalanceConfirmationDto current)
    {
        if (current.Status != IntercompanyBalanceConfirmationStatusDto.Disputed || !current.IsCrossCompany)
            return [];
        var disputeIndex = current.Events.ToList().FindLastIndex(item => item.Action == IntercompanyBalanceConfirmationActionDto.Dispute);
        return current.Events.Skip(disputeIndex + 1)
            .Where(item => item.Action == IntercompanyBalanceConfirmationActionDto.Resolve && item.Party is not null)
            .Select(item => item.Party!.Value).Distinct().ToArray();
    }

    private static IEnumerable<IntercompanyBalanceConfirmationEventDto> SinceProposal(IntercompanyBalanceConfirmationDto current)
    {
        var start = current.Events.ToList().FindLastIndex(item => item.Action == IntercompanyBalanceConfirmationActionDto.Propose);
        return current.Events.Skip(Math.Max(start, 0));
    }

    /// <summary>
    /// The advisory finding for one approved profile at the date, or null when both sides confirmed balances that have
    /// not moved since. <paramref name="currentDigest"/> is the digest of today's posted balances, when known.
    /// </summary>
    public static IntercompanyBalanceConfirmationFindingDto? Finding(Guid arrangementId, IntercompanyBalanceConfirmationDto? current, string? currentDigest)
    {
        if (current is null)
            return new("NotProposed", "No reciprocal balance confirmation has been proposed for this date.", arrangementId, null);
        if (currentDigest is not null && current.Status != IntercompanyBalanceConfirmationStatusDto.Resolved
            && !string.Equals(currentDigest, current.BalanceDigest, StringComparison.Ordinal))
            return new("BalancesChanged", "Posted balances changed after they were pinned. Propose the current balances again.", arrangementId, current.ConfirmationId);
        return current.Status switch
        {
            IntercompanyBalanceConfirmationStatusDto.Confirmed => null,
            IntercompanyBalanceConfirmationStatusDto.Disputed => new("Disputed", PartiesThatResolved(current) is [var resolvedBy]
                ? $"A side disputes the pinned balances; {(resolvedBy == IntercompanyBalanceConfirmationPartyDto.PartyOne ? "party one" : "party two")} recorded its resolution and the other side has not."
                : "A side disputes the pinned balances.", arrangementId, current.ConfirmationId),
            IntercompanyBalanceConfirmationStatusDto.Resolved => new("ResolvedAwaitingProposal",
                "The dispute was resolved; propose the corrected balances for confirmation.", arrangementId, current.ConfirmationId),
            _ => new("AwaitingConfirmation", current.Status switch
            {
                IntercompanyBalanceConfirmationStatusDto.ConfirmedByPartyOne => "Party one confirmed; party two has not.",
                IntercompanyBalanceConfirmationStatusDto.ConfirmedByPartyTwo => "Party two confirmed; party one has not.",
                _ => "Neither side has confirmed the pinned balances."
            }, arrangementId, current.ConfirmationId)
        };
    }
}
