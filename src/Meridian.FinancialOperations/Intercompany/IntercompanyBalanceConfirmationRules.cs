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
        IntercompanyBalanceConfirmationSideDto one, IntercompanyBalanceConfirmationSideDto two, string actor, DateTimeOffset at)
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
        return new(current?.ConfirmationId ?? confirmationId, tenantId, companyId, arrangementId, agreementId, asOf, (current?.Version ?? 0) + 1,
            IntercompanyBalanceConfirmationStatusDto.Proposed, one, two, one.TransactionSignedBalance + two.TransactionSignedBalance, digest,
            [.. current?.Events ?? [], proposal]);
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
            IntercompanyBalanceConfirmationActionDto.Resolve => Resolve(current, actor, reason),
            _ => throw new InvalidOperationException("Proposals are made from the posted balances, not applied to a retained version.")
        };
        var recorded = new IntercompanyBalanceConfirmationEventDto(Guid.NewGuid(), request.RequestId, request.Action,
            request.Action == IntercompanyBalanceConfirmationActionDto.Resolve ? null : request.Party, actor, at, current.BalanceDigest,
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

    private static IntercompanyBalanceConfirmationStatusDto Resolve(IntercompanyBalanceConfirmationDto current, string actor, string? reason)
    {
        if (current.Status != IntercompanyBalanceConfirmationStatusDto.Disputed)
            throw new InvalidOperationException("Only a disputed confirmation can be resolved.");
        if (string.IsNullOrEmpty(reason))
            throw new InvalidOperationException("A resolution needs a reason that records how the dispute was settled.");
        var dispute = current.Events.Last(item => item.Action == IntercompanyBalanceConfirmationActionDto.Dispute);
        if (string.Equals(dispute.Actor, actor, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The person who raised the dispute cannot also resolve it.");
        return IntercompanyBalanceConfirmationStatusDto.Resolved;
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
            IntercompanyBalanceConfirmationStatusDto.Disputed => new("Disputed", "A side disputes the pinned balances.", arrangementId, current.ConfirmationId),
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
