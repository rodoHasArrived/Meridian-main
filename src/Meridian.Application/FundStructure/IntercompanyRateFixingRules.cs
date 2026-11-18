using System.Globalization;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;

namespace Meridian.Application.FundStructure;

/// <summary>
/// Pure transitions of one index fixing. A value sets accrual rates only once a second person approves it, and a
/// correction never replaces the approved value until it is itself approved. Nothing here reads a market feed.
/// </summary>
public static class IntercompanyRateFixingRules
{
    public const int MaximumTextLength = 500;

    /// <summary>Index references match ignoring case and surrounding space, as agreement terms are typed.</summary>
    public static string IndexKey(string? indexReference)
    {
        var key = indexReference?.Trim().ToUpperInvariant() ?? string.Empty;
        if (key.Length is 0 or > 100)
            throw new ArgumentException("An index reference of 1 to 100 characters is required.");
        return key;
    }

    /// <summary>One record per tenant, index and observation date.</summary>
    public static Guid FixingId(string tenantId, string indexReference, DateOnly fixingDate)
        => new(Sha256Digest.ComputeBytesUtf8(string.Join("|", "intercompany-rate-fixing-v1", tenantId, IndexKey(indexReference),
            fixingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))).AsSpan(0, 16));

    public static string ValueDigest(string indexKey, DateOnly fixingDate, decimal rate, string source, string justification)
        => Sha256Digest.ComputeUtf8(string.Join("|", "intercompany-rate-fixing-value-v1", indexKey,
            fixingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Canonical(rate), source, justification));

    public static IntercompanyRateFixingDto Propose(IntercompanyRateFixingDto? current, string tenantId, string actor,
        ProposeIntercompanyRateFixingRequest request, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = IndexKey(request.IndexReference);
        var source = Text(request.SourceReference, "A source reference (publisher, series and publication)");
        var justification = Text(request.Justification, "A justification");
        if (request.FixingDate == default)
            throw new ArgumentException("A fixing date is required.");
        if (request.RateFraction is < -1m or > 1m)
            throw new ArgumentException("A fixing is an annual decimal fraction between -1 and 1 (0.0525 means 5.25%).");
        if ((current?.Version ?? 0) != request.ExpectedVersion)
            throw new InvalidOperationException("The fixing changed since it was read. Reload it before proposing.");
        if (current?.Pending is not null)
            throw new InvalidOperationException("A proposed value already awaits review; approve or reject it first.");
        var rate = decimal.Parse(Canonical(request.RateFraction), NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture);
        var digest = ValueDigest(key, request.FixingDate, rate, source, justification);
        if (current?.Approved?.RateFraction == rate)
            throw new InvalidOperationException("The approved fixing already has this rate; nothing would change.");
        var value = new IntercompanyRateFixingValueDto(rate, source, justification, actor, at, digest);
        var step = new IntercompanyRateFixingEventDto(Guid.NewGuid(), request.RequestId, IntercompanyRateFixingActionDto.Propose, actor, at, digest);
        return current is null
            ? new(FixingId(tenantId, key, request.FixingDate), tenantId, key, request.FixingDate, 1, null, value, [step])
            : current with { Version = current.Version + 1, Pending = value, Events = [.. current.Events, step] };
    }

    public static IntercompanyRateFixingDto Decide(IntercompanyRateFixingDto current, string actor,
        DecideIntercompanyRateFixingRequest request, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(request);
        if (current.Version != request.ExpectedVersion || current.Pending is not { } pending
            || !Sha256Digest.FixedEquals(pending.ValueDigest, request.ExpectedValueDigest))
            throw new InvalidOperationException("The proposed fixing changed since it was reviewed. Reload it before deciding.");
        var step = new IntercompanyRateFixingEventDto(Guid.NewGuid(), request.RequestId, request.Action, actor, at, pending.ValueDigest);
        switch (request.Action)
        {
            case IntercompanyRateFixingActionDto.Approve:
                if (string.Equals(actor, pending.ProposedBy, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("A fixing is approved by someone other than the person who proposed it.");
                return current with { Version = current.Version + 1, Approved = pending, Pending = null, Events = [.. current.Events, step] };
            case IntercompanyRateFixingActionDto.Reject:
                var reason = Text(request.Reason, "A rejection reason");
                return current with { Version = current.Version + 1, Pending = null, Events = [.. current.Events, step with { Reason = reason }] };
            default:
                throw new ArgumentException("A decision approves or rejects the pending value.");
        }
    }

    /// <summary>True when this request already produced the current record; a reused identity for a different step conflicts.</summary>
    public static bool IsReplay(IntercompanyRateFixingDto? current, Guid requestId, IntercompanyRateFixingActionDto action)
    {
        if (current?.Events.FirstOrDefault(item => item.RequestId == requestId) is not { } step)
            return false;
        return step.Action == action
            ? true
            : throw new InvalidOperationException("This request identity already recorded a different fixing step.");
    }

    public static IntercompanyApprovedRateFixingDto? Approved(IntercompanyRateFixingDto fixing)
        => fixing.Approved is { } value
            ? new(fixing.FixingId, fixing.IndexReference, fixing.FixingDate, value.RateFraction, value.ValueDigest)
            : null;

    private static string Canonical(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);

    private static string Text(string? value, string label)
    {
        var text = value?.Trim() ?? string.Empty;
        return text.Length is > 0 and <= MaximumTextLength
            ? text
            : throw new ArgumentException($"{label} of 1 to {MaximumTextLength} characters is required.");
    }
}
