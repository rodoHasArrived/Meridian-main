using Meridian.Contracts.Ledger;

namespace Meridian.FinancialOperations.Intercompany;

/// <summary>
/// Pure transitions of an intercompany confirmation close policy (ADR-032). A policy blocks nothing until someone other
/// than its proposer approves it; disabling it restores advisory-only confirmation for the book.
/// </summary>
public static class IntercompanyConfirmationClosePolicyRules
{
    public const int MaximumReasonLength = 500;

    /// <summary>Confirmation finding codes a policy may make blocking, without the <c>intercompany.confirmation.</c> prefix.</summary>
    public static readonly IReadOnlyList<string> BlockableCodes =
        ["not-proposed", "awaiting-confirmation", "disputed", "balances-changed", "resolved-awaiting-proposal"];

    public static IntercompanyConfirmationClosePolicyDto Propose(string tenantId, string? companyId, string actor,
        ProposeIntercompanyConfirmationClosePolicyRequest request, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RequestId == Guid.Empty)
            throw new ArgumentException("A proposal request identity is required.");
        if (request.LedgerBookId == Guid.Empty)
            throw new ArgumentException("A ledger book is required.");
        var reason = Text(request.Reason, "A reason");
        return new(Guid.NewGuid(), tenantId, companyId, request.LedgerBookId, Codes(request.BlockingCodes), 1,
            IntercompanyConfirmationClosePolicyStatusDto.Proposed, null,
            [new(Guid.NewGuid(), request.RequestId, IntercompanyConfirmationClosePolicyActionDto.Propose, actor, at, reason)]);
    }

    public static IntercompanyConfirmationClosePolicyDto Decide(IntercompanyConfirmationClosePolicyDto current, string actor,
        DecideIntercompanyConfirmationClosePolicyRequest request, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(request);
        if (request.RequestId == Guid.Empty)
            throw new ArgumentException("A decision request identity is required.");
        if (current.Version != request.ExpectedVersion)
            throw new InvalidOperationException("The close policy changed since it was read. Reload it before deciding.");
        var proposer = current.Events.First(item => item.Action == IntercompanyConfirmationClosePolicyActionDto.Propose).Actor;
        IntercompanyConfirmationClosePolicyDto Next(IntercompanyConfirmationClosePolicyStatusDto status, string? reason, DateOnly? activeFrom = null) => current with
        {
            Version = current.Version + 1,
            Status = status,
            ActiveFrom = activeFrom ?? current.ActiveFrom,
            Events = [.. current.Events, new(Guid.NewGuid(), request.RequestId, request.Action, actor, at, reason)]
        };
        return (request.Action, current.Status) switch
        {
            (IntercompanyConfirmationClosePolicyActionDto.Approve, IntercompanyConfirmationClosePolicyStatusDto.Proposed)
                => string.Equals(actor, proposer, StringComparison.OrdinalIgnoreCase)
                    ? throw new InvalidOperationException("A close policy is approved by someone other than the person who proposed it.")
                    : Next(IntercompanyConfirmationClosePolicyStatusDto.Active, null, DateOnly.FromDateTime(at.UtcDateTime)),
            (IntercompanyConfirmationClosePolicyActionDto.Reject, IntercompanyConfirmationClosePolicyStatusDto.Proposed)
                => Next(IntercompanyConfirmationClosePolicyStatusDto.Rejected, Text(request.Reason, "A rejection reason")),
            (IntercompanyConfirmationClosePolicyActionDto.Disable, IntercompanyConfirmationClosePolicyStatusDto.Active)
                => Next(IntercompanyConfirmationClosePolicyStatusDto.Disabled, Text(request.Reason, "A reason for disabling")),
            _ => throw new InvalidOperationException($"A {current.Status} close policy cannot be {request.Action switch
            {
                IntercompanyConfirmationClosePolicyActionDto.Approve => "approved",
                IntercompanyConfirmationClosePolicyActionDto.Reject => "rejected",
                IntercompanyConfirmationClosePolicyActionDto.Disable => "disabled",
                _ => "changed that way"
            }}.")
        };
    }

    /// <summary>True when this request already produced a step of the policy; a reused identity for another step conflicts.</summary>
    public static bool IsReplay(IntercompanyConfirmationClosePolicyDto? current, Guid requestId, IntercompanyConfirmationClosePolicyActionDto action)
    {
        if (current?.Events.FirstOrDefault(item => item.RequestId == requestId) is not { } step)
            return false;
        return step.Action == action ? true
            : throw new InvalidOperationException("This request identity already recorded a different close-policy step.");
    }

    /// <summary>
    /// The contributor result for one active policy over the confirmation findings at the close period end. Findings use the
    /// confirmation service's raw codes; any unreadable pair makes the decision incomplete instead of being ignored.
    /// </summary>
    public static (string Status, IReadOnlyList<string> Records, string Message) Evaluate(IntercompanyConfirmationClosePolicyDto policy,
        IReadOnlyList<IntercompanyBalanceConfirmationFindingDto> findings, DateOnly periodEnd)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(findings);
        var unavailable = findings.Where(item => Code(item.Code) == "unavailable").ToArray();
        if (unavailable.Length > 0)
            return ("Unavailable", unavailable.Select(item => item.AccountingArrangementId.ToString("D")).ToArray(),
                $"Close policy {policy.PolicyId:D} v{policy.Version} needs confirmation status at {periodEnd:yyyy-MM-dd}, but balances for "
                + $"{unavailable.Length} accounting profile(s) cannot be read.");
        var blocking = findings.Where(item => policy.BlockingCodes.Contains(Code(item.Code), StringComparer.Ordinal)).ToArray();
        if (blocking.Length == 0)
            return ("Ready", [], $"Close policy {policy.PolicyId:D} v{policy.Version}: no blocking confirmation finding at {periodEnd:yyyy-MM-dd}.");
        return ("Blocked", blocking.Select(item => item.ConfirmationId?.ToString("D") ?? item.AccountingArrangementId.ToString("D")).Distinct().ToArray(),
            $"Close policy {policy.PolicyId:D} v{policy.Version} blocks close at {periodEnd:yyyy-MM-dd}: "
            + string.Join("; ", blocking.Select(item => $"intercompany.confirmation.{Code(item.Code)} for accounting profile {item.AccountingArrangementId:D}")) + ".");
    }

    /// <summary>Maps a confirmation service finding code to the published <c>intercompany.confirmation.*</c> suffix.</summary>
    public static string Code(string code) => code switch
    {
        "NotProposed" => "not-proposed",
        "AwaitingConfirmation" => "awaiting-confirmation",
        "Disputed" => "disputed",
        "BalancesChanged" => "balances-changed",
        "ResolvedAwaitingProposal" => "resolved-awaiting-proposal",
        _ => "unavailable"
    };

    private static IReadOnlyList<string> Codes(IReadOnlyList<string>? codes)
    {
        var normalized = (codes ?? []).Select(code => code?.Trim().ToLowerInvariant() ?? string.Empty).Distinct(StringComparer.Ordinal).ToArray();
        if (normalized.Length == 0)
            throw new ArgumentException("Name at least one confirmation finding that blocks close.");
        if (normalized.FirstOrDefault(code => !BlockableCodes.Contains(code, StringComparer.Ordinal)) is { } unknown)
            throw new ArgumentException($"'{unknown}' is not a confirmation finding a close policy can block. Use: {string.Join(", ", BlockableCodes)}.");
        return BlockableCodes.Where(normalized.Contains).ToArray();
    }

    private static string Text(string? value, string label)
    {
        var text = value?.Trim() ?? string.Empty;
        return text.Length is > 0 and <= MaximumReasonLength
            ? text : throw new ArgumentException($"{label} of 1 to {MaximumReasonLength} characters is required.");
    }
}
