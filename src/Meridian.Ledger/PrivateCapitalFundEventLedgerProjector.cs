namespace Meridian.Ledger;

/// <summary>
/// Approval state reconstructed from journal-entry metadata attached to a private-capital posting.
/// </summary>
public enum PrivateCapitalFundEventApprovalState
{
    Missing,
    Submitted,
    Approved,
    Rejected,
    Posted,
}

/// <summary>
/// Severity for private-capital event reconstruction issues.
/// </summary>
public enum PrivateCapitalFundEventIssueSeverity
{
    Info,
    Warning,
    Critical,
}

/// <summary>
/// Ledger-backed projection of private-capital fund events, capital-account movements,
/// retained evidence, approval state, and report outputs.
/// </summary>
public sealed record PrivateCapitalFundEventLedgerProjection(
    IReadOnlyList<PrivateCapitalFundEventLedgerEvent> Events)
{
    public int EventCount => Events.Count;

    public int PostingReadyCount => Events.Count(static item => item.IsPostingReady);

    public int ReportReadyCount => Events.Count(static item => item.IsReportReady);
}

public sealed record PrivateCapitalFundEventLedgerEvent(
    string FundEventId,
    string FundEventType,
    DateOnly? EffectiveDate,
    string? CapitalAccountId,
    string? InvestorId,
    string? PaymentIntentId,
    string? SettlementReference,
    string? IdempotencyKey,
    DateTimeOffset FirstPostedAt,
    DateTimeOffset LastPostedAt,
    PrivateCapitalFundEventApprovalState ApprovalState,
    string? ApprovalId,
    string? ApprovedBy,
    IReadOnlyList<Guid> JournalEntryIds,
    IReadOnlyList<Guid> LedgerEntryIds,
    IReadOnlyList<PrivateCapitalFundEventLedgerImpact> LedgerImpacts,
    IReadOnlyList<PrivateCapitalFundEventCapitalAccountImpact> CapitalAccountImpacts,
    IReadOnlyList<string> EvidenceLinks,
    IReadOnlyList<PrivateCapitalFundEventReportOutput> ReportOutputs,
    IReadOnlyList<PrivateCapitalFundEventIssue> Issues)
{
    public bool HasCriticalIssues => Issues.Any(static item => item.Severity == PrivateCapitalFundEventIssueSeverity.Critical);

    public bool HasEvidence => EvidenceLinks.Count > 0;

    public bool IsApprovalComplete => ApprovalState is PrivateCapitalFundEventApprovalState.Approved or PrivateCapitalFundEventApprovalState.Posted;

    public bool IsPostingReady =>
        !HasCriticalIssues &&
        HasEvidence &&
        IsApprovalComplete &&
        LedgerImpacts.Count > 0 &&
        LedgerImpacts.All(static item => item.IsBalanced) &&
        CapitalAccountImpacts.Count > 0;

    public bool IsReportReady => IsPostingReady && ReportOutputs.Any(static item => item.IsPublished && item.EvidenceLinks.Count > 0);
}

public sealed record PrivateCapitalFundEventLedgerImpact(
    string LedgerImpactId,
    Guid JournalEntryId,
    DateTimeOffset PostedAt,
    string Description,
    decimal TotalDebits,
    decimal TotalCredits,
    decimal Imbalance,
    bool IsBalanced,
    IReadOnlyList<PrivateCapitalFundEventLedgerLine> Lines);

public sealed record PrivateCapitalFundEventLedgerLine(
    Guid LedgerEntryId,
    string AccountName,
    LedgerAccountType AccountType,
    string? Symbol,
    string? FinancialAccountId,
    decimal Debit,
    decimal Credit,
    decimal NetNormalBalanceImpact);

public sealed record PrivateCapitalFundEventCapitalAccountImpact(
    string CapitalAccountId,
    string? InvestorId,
    string AccountName,
    string? FinancialAccountId,
    decimal TotalDebits,
    decimal TotalCredits,
    decimal NetCapitalAccountImpact,
    IReadOnlyList<Guid> LedgerEntryIds);

public sealed record PrivateCapitalFundEventReportOutput(
    string ReportOutputId,
    string ReportId,
    LedgerReportPackLifecycleStatus Status,
    DateTimeOffset AsOf,
    string SignatureHash,
    IReadOnlyList<string> ArtifactNames,
    IReadOnlyList<string> EvidenceLinks,
    bool IsPublished);

public sealed record PrivateCapitalFundEventIssue(
    string Code,
    PrivateCapitalFundEventIssueSeverity Severity,
    string Message);

/// <summary>
/// Reconstructs the private-capital fund-event ledger from posted journal entries.
/// </summary>
public static class PrivateCapitalFundEventLedgerProjector
{
    private const decimal BalanceTolerance = 0.000001m;

    private sealed record CapitalAccountImpactLine(
        string CapitalAccountId,
        string? InvestorId,
        LedgerEntry Line);

    public static PrivateCapitalFundEventLedgerProjection Project(
        IReadOnlyLedger ledger,
        IReadOnlyList<LedgerFinancialReportPack>? reportPacks = null,
        LedgerQuery? query = null)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        var entries = (query is null ? ledger.Journal : ledger.GetJournalEntries(query))
            .Where(static entry => HasPrivateCapitalContext(entry.Metadata))
            .OrderBy(static entry => entry.Timestamp)
            .ThenBy(static entry => entry.JournalEntryId)
            .ToArray();

        var events = entries
            .GroupBy(BuildEventGroupKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => BuildEvent(group.OrderBy(static entry => entry.Timestamp).ThenBy(static entry => entry.JournalEntryId).ToArray(), reportPacks ?? []))
            .OrderBy(static item => item.FirstPostedAt)
            .ThenBy(static item => item.FundEventId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new PrivateCapitalFundEventLedgerProjection(events);
    }

    private static PrivateCapitalFundEventLedgerEvent BuildEvent(
        IReadOnlyList<JournalEntry> entries,
        IReadOnlyList<LedgerFinancialReportPack> reportPacks)
    {
        var fundEventId = FirstText(entries, static metadata => metadata.FundEventId);
        var fundEventType = FirstText(entries, static metadata => metadata.FundEventType) ??
            FirstText(entries, static metadata => metadata.ActivityType);
        var capitalAccountId = FirstText(entries, static metadata => metadata.CapitalAccountId);
        var investorId = FirstText(entries, static metadata => metadata.InvestorId);
        var journalEntryIds = entries.Select(static entry => entry.JournalEntryId).Distinct().ToArray();
        var ledgerEntryIds = entries
            .SelectMany(static entry => entry.Lines)
            .Select(static line => line.EntryId)
            .Distinct()
            .ToArray();
        var evidenceLinks = ExtractEvidenceLinks(entries);
        var ledgerImpacts = entries.Select(BuildLedgerImpact).ToArray();
        var capitalAccountImpacts = BuildCapitalAccountImpacts(entries, capitalAccountId, investorId);
        var reportOutputs = BuildReportOutputs(fundEventId, journalEntryIds, ledgerEntryIds, reportPacks);
        var issues = BuildIssues(entries, fundEventId, fundEventType, capitalAccountId, evidenceLinks, ledgerImpacts, capitalAccountImpacts);

        return new PrivateCapitalFundEventLedgerEvent(
            fundEventId ?? $"journal-entry:{journalEntryIds[0]:D}",
            fundEventType ?? "Unclassified",
            entries.Select(static entry => entry.Metadata.EffectiveDate).FirstOrDefault(static value => value is not null),
            capitalAccountId,
            investorId,
            FirstText(entries, static metadata => metadata.PaymentIntentId),
            FirstText(entries, static metadata => metadata.SettlementReference),
            FirstText(entries, static metadata => metadata.IdempotencyKey),
            entries[0].Timestamp,
            entries[^1].Timestamp,
            ResolveApprovalState(entries),
            FirstTagText(entries, "automatedJournalApprovalId") ?? FirstTagText(entries, "approvalId"),
            FirstTagText(entries, "approvedBy"),
            journalEntryIds,
            ledgerEntryIds,
            ledgerImpacts,
            capitalAccountImpacts,
            evidenceLinks,
            reportOutputs,
            issues);
    }

    private static PrivateCapitalFundEventLedgerImpact BuildLedgerImpact(JournalEntry entry)
    {
        var totalDebits = entry.Lines.Sum(static line => line.Debit);
        var totalCredits = entry.Lines.Sum(static line => line.Credit);
        var imbalance = totalDebits - totalCredits;

        return new PrivateCapitalFundEventLedgerImpact(
            $"journal-entry:{entry.JournalEntryId:D}",
            entry.JournalEntryId,
            entry.Timestamp,
            entry.Description,
            totalDebits,
            totalCredits,
            imbalance,
            Math.Abs(imbalance) <= BalanceTolerance,
            entry.Lines
                .OrderBy(static line => line.Account.AccountType)
                .ThenBy(static line => line.Account.Name, StringComparer.Ordinal)
                .Select(static line => new PrivateCapitalFundEventLedgerLine(
                    line.EntryId,
                    line.Account.Name,
                    line.Account.AccountType,
                    line.Account.Symbol,
                    line.Account.FinancialAccountId,
                    line.Debit,
                    line.Credit,
                    Ledger.CalculateNetBalance(line.Account.AccountType, line.Debit, line.Credit)))
                .ToArray());
    }

    private static IReadOnlyList<PrivateCapitalFundEventCapitalAccountImpact> BuildCapitalAccountImpacts(
        IReadOnlyList<JournalEntry> entries,
        string? capitalAccountId,
        string? investorId)
    {
        return entries
            .SelectMany(entry => entry.Lines
                .Where(static line => line.Account.AccountType == LedgerAccountType.Equity)
                .Select(line => BuildCapitalAccountImpactLine(entry.Metadata, line, capitalAccountId, investorId)))
            .Where(static line => line is not null)
            .Select(static line => line!)
            .GroupBy(
                static line => $"{line.CapitalAccountId}\u001f{line.InvestorId}\u001f{line.Line.Account.Name}\u001f{line.Line.Account.FinancialAccountId}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                var debits = group.Sum(static line => line.Line.Debit);
                var credits = group.Sum(static line => line.Line.Credit);
                return new PrivateCapitalFundEventCapitalAccountImpact(
                    first.CapitalAccountId,
                    first.InvestorId,
                    first.Line.Account.Name,
                    first.Line.Account.FinancialAccountId,
                    debits,
                    credits,
                    // Capital-account subledger lines are all equity; route the net through the
                    // shared posting kernel rather than reimplementing the credit-normal rule.
                    Ledger.CalculateNetBalance(LedgerAccountType.Equity, debits, credits),
                    group.Select(static line => line.Line.EntryId).Distinct().ToArray());
            })
            .OrderBy(static item => item.CapitalAccountId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.InvestorId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.AccountName, StringComparer.Ordinal)
            .ToArray();
    }

    private static CapitalAccountImpactLine? BuildCapitalAccountImpactLine(
        JournalEntryMetadata metadata,
        LedgerEntry line,
        string? fallbackCapitalAccountId,
        string? fallbackInvestorId)
    {
        var resolvedCapitalAccountId = NormalizeText(metadata.CapitalAccountId) ?? NormalizeText(fallbackCapitalAccountId);
        if (resolvedCapitalAccountId is null)
        {
            return null;
        }

        var resolvedInvestorId = NormalizeText(metadata.InvestorId) ?? NormalizeText(fallbackInvestorId);
        return new CapitalAccountImpactLine(resolvedCapitalAccountId, resolvedInvestorId, line);
    }

    private static IReadOnlyList<PrivateCapitalFundEventReportOutput> BuildReportOutputs(
        string? fundEventId,
        IReadOnlyList<Guid> journalEntryIds,
        IReadOnlyList<Guid> ledgerEntryIds,
        IReadOnlyList<LedgerFinancialReportPack> reportPacks)
    {
        if (reportPacks.Count == 0)
        {
            return [];
        }

        var journalIds = journalEntryIds.ToHashSet();
        var lineIds = ledgerEntryIds.ToHashSet();
        var eventKey = string.IsNullOrWhiteSpace(fundEventId) ? journalEntryIds[0].ToString("D") : fundEventId.Trim();

        return reportPacks
            .Select(pack =>
            {
                var linkedRows = pack.LineProvenance
                    .Where(row => row.LedgerJournalEntryIds.Any(journalIds.Contains) || row.LedgerEntryIds.Any(lineIds.Contains))
                    .ToArray();
                if (linkedRows.Length == 0)
                {
                    return null;
                }

                var evidence = linkedRows
                    .SelectMany(static row => row.EvidenceLinks)
                    .Concat(pack.LifecycleEvents.SelectMany(static item => item.EvidenceLinks))
                    .Concat(pack.Restatements.SelectMany(static item => item.EvidenceLinks))
                    .Where(static link => !string.IsNullOrWhiteSpace(link))
                    .Select(static link => link.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return new PrivateCapitalFundEventReportOutput(
                    $"{pack.Request.ReportId}:private-capital:{eventKey}",
                    pack.Request.ReportId,
                    pack.Status,
                    pack.Request.AsOf,
                    pack.Signature.PayloadChecksumSha256,
                    pack.Artifacts.Select(static artifact => artifact.Name).Order(StringComparer.Ordinal).ToArray(),
                    evidence,
                    pack.Status is LedgerReportPackLifecycleStatus.Published or LedgerReportPackLifecycleStatus.Restated);
            })
            .Where(static item => item is not null)
            .Select(static item => item!)
            .OrderBy(static item => item.AsOf)
            .ThenBy(static item => item.ReportId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<PrivateCapitalFundEventIssue> BuildIssues(
        IReadOnlyList<JournalEntry> entries,
        string? fundEventId,
        string? fundEventType,
        string? capitalAccountId,
        IReadOnlyList<string> evidenceLinks,
        IReadOnlyList<PrivateCapitalFundEventLedgerImpact> ledgerImpacts,
        IReadOnlyList<PrivateCapitalFundEventCapitalAccountImpact> capitalAccountImpacts)
    {
        var issues = new List<PrivateCapitalFundEventIssue>();

        if (string.IsNullOrWhiteSpace(fundEventId))
        {
            AddIssue(issues, "private-capital.fund-event-id-missing", PrivateCapitalFundEventIssueSeverity.Critical, "Posted private-capital journal metadata is missing a fund event id.");
        }

        if (string.IsNullOrWhiteSpace(fundEventType))
        {
            AddIssue(issues, "private-capital.fund-event-type-missing", PrivateCapitalFundEventIssueSeverity.Critical, "Posted private-capital journal metadata is missing a fund event type.");
        }

        if (string.IsNullOrWhiteSpace(capitalAccountId))
        {
            AddIssue(issues, "private-capital.capital-account-missing", PrivateCapitalFundEventIssueSeverity.Critical, "Posted private-capital journal metadata is missing a capital account id.");
        }

        AddMetadataConsistencyIssue(
            issues,
            entries.Select(static entry => entry.Metadata.FundEventType ?? entry.Metadata.ActivityType),
            "private-capital.fund-event-type-conflict",
            "Posted private-capital journals grouped into one fund event disagree on fund event type.");
        AddMetadataConsistencyIssue(
            issues,
            entries.Select(static entry => entry.Metadata.CapitalAccountId),
            "private-capital.capital-account-conflict",
            "Posted private-capital journals grouped into one fund event disagree on capital account id.");
        AddMetadataConsistencyIssue(
            issues,
            entries.Select(static entry => entry.Metadata.InvestorId),
            "private-capital.investor-conflict",
            "Posted private-capital journals grouped into one fund event disagree on investor id.");
        AddMetadataConsistencyIssue(
            issues,
            entries.Select(static entry => entry.Metadata.EffectiveDate?.ToString("yyyy-MM-dd")),
            "private-capital.effective-date-conflict",
            "Posted private-capital journals grouped into one fund event disagree on effective date.");
        AddMetadataConsistencyIssue(
            issues,
            entries.Select(ResolveApprovalStatusText),
            "private-capital.approval-state-conflict",
            "Posted private-capital journals grouped into one fund event disagree on approval state.");
        AddMetadataConsistencyIssue(
            issues,
            entries.Select(static entry => entry.Metadata.IdempotencyKey),
            "private-capital.idempotency-key-conflict",
            "Posted private-capital journals grouped into one fund event disagree on idempotency key.");
        AddMetadataConsistencyIssue(
            issues,
            entries.Select(static entry => entry.Metadata.PaymentIntentId),
            "private-capital.payment-intent-conflict",
            "Posted private-capital journals grouped into one fund event disagree on payment intent id.");
        AddMetadataConsistencyIssue(
            issues,
            entries.Select(static entry => entry.Metadata.SettlementReference),
            "private-capital.settlement-reference-conflict",
            "Posted private-capital journals grouped into one fund event disagree on settlement reference.");

        if (!entries.Any(static entry => entry.Metadata.EffectiveDate is not null))
        {
            AddIssue(issues, "private-capital.effective-date-missing", PrivateCapitalFundEventIssueSeverity.Critical, "Posted private-capital journal metadata is missing an effective date.");
        }

        if (!entries.Any(static entry => !string.IsNullOrWhiteSpace(entry.Metadata.IdempotencyKey)))
        {
            AddIssue(issues, "private-capital.idempotency-key-missing", PrivateCapitalFundEventIssueSeverity.Critical, "Posted private-capital journal metadata is missing an idempotency key.");
        }

        if (evidenceLinks.Count == 0)
        {
            AddIssue(issues, "private-capital.evidence-missing", PrivateCapitalFundEventIssueSeverity.Critical, "Posted private-capital event has no retained evidence links.");
        }

        if (ResolveApprovalState(entries) == PrivateCapitalFundEventApprovalState.Missing)
        {
            AddIssue(issues, "private-capital.approval-missing", PrivateCapitalFundEventIssueSeverity.Warning, "Posted private-capital event has no approval metadata.");
        }

        if (ledgerImpacts.Any(static item => !item.IsBalanced))
        {
            AddIssue(issues, "private-capital.ledger-impact-unbalanced", PrivateCapitalFundEventIssueSeverity.Critical, "At least one private-capital ledger impact is unbalanced.");
        }

        if (capitalAccountImpacts.Count == 0)
        {
            AddIssue(issues, "private-capital.capital-account-impact-missing", PrivateCapitalFundEventIssueSeverity.Critical, "Posted private-capital event has no capital-account subledger impact.");
        }

        return issues;
    }

    private static void AddMetadataConsistencyIssue(
        List<PrivateCapitalFundEventIssue> issues,
        IEnumerable<string?> values,
        string code,
        string message)
    {
        var distinctValues = values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();

        if (distinctValues.Length > 1)
        {
            AddIssue(issues, code, PrivateCapitalFundEventIssueSeverity.Critical, message);
        }
    }

    private static void AddIssue(
        List<PrivateCapitalFundEventIssue> issues,
        string code,
        PrivateCapitalFundEventIssueSeverity severity,
        string message)
        => issues.Add(new PrivateCapitalFundEventIssue(code, severity, message));

    private static bool HasPrivateCapitalContext(JournalEntryMetadata metadata) =>
        !string.IsNullOrWhiteSpace(metadata.FundEventId) ||
        !string.IsNullOrWhiteSpace(metadata.FundEventType) ||
        !string.IsNullOrWhiteSpace(metadata.CapitalAccountId) ||
        !string.IsNullOrWhiteSpace(metadata.InvestorId) ||
        !string.IsNullOrWhiteSpace(metadata.PaymentIntentId) ||
        !string.IsNullOrWhiteSpace(metadata.SettlementReference);

    private static string BuildEventGroupKey(JournalEntry entry) =>
        !string.IsNullOrWhiteSpace(entry.Metadata.FundEventId)
            ? entry.Metadata.FundEventId.Trim()
            : $"journal-entry:{entry.JournalEntryId:D}";

    private static IReadOnlyList<string> ExtractEvidenceLinks(IReadOnlyList<JournalEntry> entries) =>
        entries
            .SelectMany(static entry => entry.Metadata.EvidenceReferences.Select(static evidence => evidence.Uri))
            .Concat(entries.SelectMany(static entry => ExtractEvidenceLinks(entry.Metadata.Tags)))
            .Where(static link => !string.IsNullOrWhiteSpace(link))
            .Select(static link => link.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IEnumerable<string> ExtractEvidenceLinks(IReadOnlyDictionary<string, string>? tags)
    {
        if (tags is null || tags.Count == 0)
        {
            return [];
        }

        return tags
            .Where(static pair =>
                string.Equals(pair.Key, "evidenceLinks", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pair.Key, "approvalEvidenceLinks", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pair.Key, "sourceEvidenceLinks", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pair.Key, "settlementEvidenceLinks", StringComparison.OrdinalIgnoreCase))
            .SelectMany(static pair => pair.Value.Split(['|', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static PrivateCapitalFundEventApprovalState ResolveApprovalState(IReadOnlyList<JournalEntry> entries)
    {
        var status = entries.Select(ResolveApprovalStatusText).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
        if (Enum.TryParse<AutomatedJournalApprovalStatus>(status, ignoreCase: true, out var automatedStatus))
        {
            return automatedStatus switch
            {
                AutomatedJournalApprovalStatus.Submitted => PrivateCapitalFundEventApprovalState.Submitted,
                AutomatedJournalApprovalStatus.Approved => PrivateCapitalFundEventApprovalState.Approved,
                AutomatedJournalApprovalStatus.Rejected => PrivateCapitalFundEventApprovalState.Rejected,
                AutomatedJournalApprovalStatus.Posted => PrivateCapitalFundEventApprovalState.Posted,
                _ => PrivateCapitalFundEventApprovalState.Missing
            };
        }

        if (!string.IsNullOrWhiteSpace(FirstTagText(entries, "approvedBy")) ||
            !string.IsNullOrWhiteSpace(FirstTagText(entries, "automatedJournalApprovalId")) ||
            !string.IsNullOrWhiteSpace(FirstTagText(entries, "approvalId")))
        {
            return PrivateCapitalFundEventApprovalState.Approved;
        }

        return PrivateCapitalFundEventApprovalState.Missing;
    }

    private static string? ResolveApprovalStatusText(JournalEntry entry) =>
        TryGetTagText(entry, "automatedJournalStatus") ??
        TryGetTagText(entry, "approvalStatus") ??
        TryGetTagText(entry, "journalApprovalStatus");

    private static string? FirstText(IReadOnlyList<JournalEntry> entries, Func<JournalEntryMetadata, string?> selector) =>
        entries
            .Select(entry => selector(entry.Metadata))
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))
            ?.Trim();

    private static string? NormalizeText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? FirstTagText(IReadOnlyList<JournalEntry> entries, string key)
    {
        foreach (var entry in entries)
        {
            if (entry.Metadata.Tags is not null &&
                entry.Metadata.Tags.TryGetValue(key, out var value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static string? TryGetTagText(JournalEntry entry, string key)
        => entry.Metadata.Tags is not null &&
           entry.Metadata.Tags.TryGetValue(key, out var value) &&
           !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
}
