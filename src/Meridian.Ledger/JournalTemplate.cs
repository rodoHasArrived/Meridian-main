using Meridian.Contracts.Ledger;

namespace Meridian.Ledger;

/// <summary>Which side of the ledger a <see cref="JournalTemplateLine"/> posts to.</summary>
public enum JournalTemplateSide
{
    /// <summary>The resolved amount is posted as a debit.</summary>
    Debit,

    /// <summary>The resolved amount is posted as a credit.</summary>
    Credit,
}

/// <summary>
/// One line of a reusable <see cref="JournalTemplate"/>. The posting amount is resolved at
/// instantiation time either from a named parameter (<see cref="AmountParameter"/>) or from a
/// <see cref="FixedAmount"/>, then scaled by <see cref="Factor"/>. This lets a single template
/// (for example a management-fee accrual) be reposted every period with a different fee amount.
/// </summary>
public sealed record JournalTemplateLine(
    LedgerAccount Account,
    JournalTemplateSide Side,
    string? AmountParameter = null,
    decimal? FixedAmount = null,
    decimal Factor = 1m,
    LedgerLineDimensionSet? Dimensions = null,
    string? Memo = null)
{
    /// <summary>
    /// Resolves this line's non-negative posting amount from the supplied parameter values.
    /// </summary>
    /// <exception cref="LedgerValidationException">
    /// Thrown when the referenced parameter is missing, or the resolved amount is negative.
    /// </exception>
    public decimal Resolve(IReadOnlyDictionary<string, decimal> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        decimal baseAmount;
        if (!string.IsNullOrWhiteSpace(AmountParameter))
        {
            if (!parameters.TryGetValue(AmountParameter.Trim(), out baseAmount))
            {
                throw new LedgerValidationException(
                    $"Journal template line for account '{Account}' references parameter '{AmountParameter}', which was not supplied.");
            }
        }
        else if (FixedAmount is { } fixedAmount)
        {
            baseAmount = fixedAmount;
        }
        else
        {
            throw new LedgerValidationException(
                $"Journal template line for account '{Account}' must define either an amount parameter or a fixed amount.");
        }

        var amount = baseAmount * Factor;
        if (amount < 0m)
        {
            throw new LedgerValidationException(
                $"Journal template line for account '{Account}' resolved to a negative amount ({amount}); use the opposite side instead.");
        }

        return amount;
    }
}

/// <summary>
/// A named, reusable double-entry posting template. Registering a template once lets fund
/// administrators re-post the same balanced structure — accruals, fee bookings, standard reclasses —
/// with per-instance amounts and dimensional scope, instead of re-keying journal lines each period.
/// </summary>
/// <remarks>
/// This is the ledger-domain posting engine. It complements the contract-layer
/// <c>Meridian.Contracts.Ledger.JournalEntryTemplateDto</c> (the operator/API editing shape) by
/// turning a template plus runtime parameters into balanced ledger lines ready to post.
/// </remarks>
public sealed record JournalTemplate
{
    public JournalTemplate(
        string templateId,
        string name,
        string description,
        IReadOnlyList<JournalTemplateLine> lines,
        IReadOnlyList<string>? requiredParameters = null,
        string? ledgerBook = null)
    {
        if (string.IsNullOrWhiteSpace(templateId))
            throw new ArgumentException("Journal template identifier must not be null or whitespace.", nameof(templateId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Journal template name must not be null or whitespace.", nameof(name));
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0)
            throw new ArgumentException("A journal template must define at least one line.", nameof(lines));

        TemplateId = templateId.Trim();
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? string.Empty : description.Trim();
        Lines = lines.ToArray();
        RequiredParameters = (requiredParameters ?? InferParameters(lines))
            .Where(static parameter => !string.IsNullOrWhiteSpace(parameter))
            .Select(static parameter => parameter.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        LedgerBook = string.IsNullOrWhiteSpace(ledgerBook) ? null : ledgerBook.Trim();
    }

    public string TemplateId { get; }

    public string Name { get; }

    public string Description { get; }

    public IReadOnlyList<JournalTemplateLine> Lines { get; }

    /// <summary>Parameter names that must be supplied to <see cref="Instantiate"/>.</summary>
    public IReadOnlyList<string> RequiredParameters { get; }

    /// <summary>Optional target ledger book this template posts to.</summary>
    public string? LedgerBook { get; }

    /// <summary>
    /// Resolves this template against the supplied parameters and effective timestamp, producing a
    /// balanced <see cref="JournalTemplateInstance"/> ready to post.
    /// </summary>
    /// <exception cref="LedgerValidationException">
    /// Thrown when a required parameter is missing or the resolved lines do not balance.
    /// </exception>
    public JournalTemplateInstance Instantiate(JournalTemplateInstantiation instantiation)
    {
        ArgumentNullException.ThrowIfNull(instantiation);

        // Normalize the supplied parameters once (trimmed keys, case-insensitive) so lookups are
        // robust regardless of the comparer or key spacing the caller's dictionary used.
        var parameters = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in instantiation.Parameters)
        {
            if (!string.IsNullOrWhiteSpace(key))
                parameters[key.Trim()] = value;
        }

        var missing = RequiredParameters
            .Where(parameter => !parameters.ContainsKey(parameter))
            .ToArray();
        if (missing.Length > 0)
        {
            throw new LedgerValidationException(
                $"Journal template '{TemplateId}' is missing required parameter(s): {string.Join(", ", missing)}.");
        }

        var description = string.IsNullOrWhiteSpace(instantiation.DescriptionOverride)
            ? Name
            : instantiation.DescriptionOverride.Trim();

        var resolvedLines = new List<(LedgerAccount account, decimal debit, decimal credit, LedgerLineDimensionSet? dimensions)>();
        foreach (var line in Lines)
        {
            var amount = line.Resolve(parameters);
            if (amount == 0m)
                continue;

            var dimensions = line.Dimensions ?? instantiation.Dimensions;
            resolvedLines.Add(line.Side == JournalTemplateSide.Debit
                ? (line.Account, amount, 0m, dimensions)
                : (line.Account, 0m, amount, dimensions));
        }

        if (resolvedLines.Count == 0)
        {
            throw new LedgerValidationException(
                $"Journal template '{TemplateId}' resolved to no non-zero lines for the supplied parameters.");
        }

        var totalDebits = resolvedLines.Sum(static line => line.debit);
        var totalCredits = resolvedLines.Sum(static line => line.credit);
        if (Math.Abs(totalDebits - totalCredits) > LedgerToleranceConstants.Balance)
        {
            throw new LedgerValidationException(
                $"Journal template '{TemplateId}' resolved to an unbalanced journal (debits {totalDebits}, credits {totalCredits}).");
        }

        var metadata = (instantiation.Metadata ?? new JournalEntryMetadata()) with { LedgerBook = instantiation.Metadata?.LedgerBook ?? LedgerBook };

        return new JournalTemplateInstance(
            TemplateId,
            instantiation.Timestamp.ToUniversalTime(),
            description,
            resolvedLines,
            metadata.Normalize());
    }

    private static IReadOnlyList<string> InferParameters(IReadOnlyList<JournalTemplateLine> lines)
        => lines
            .Where(static line => !string.IsNullOrWhiteSpace(line.AmountParameter))
            .Select(static line => line.AmountParameter!)
            .ToArray();
}

/// <summary>
/// Inputs for turning a <see cref="JournalTemplate"/> into a concrete, postable journal.
/// </summary>
public sealed record JournalTemplateInstantiation(
    DateTimeOffset Timestamp,
    IReadOnlyDictionary<string, decimal> Parameters,
    LedgerLineDimensionSet? Dimensions = null,
    JournalEntryMetadata? Metadata = null,
    string? DescriptionOverride = null)
{
    public IReadOnlyDictionary<string, decimal> Parameters { get; init; } =
        Parameters ?? new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// A balanced, template-derived journal ready to post to a <see cref="Ledger"/>.
/// </summary>
public sealed record JournalTemplateInstance(
    string TemplateId,
    DateTimeOffset Timestamp,
    string Description,
    IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit, LedgerLineDimensionSet? dimensions)> Lines,
    JournalEntryMetadata Metadata)
{
    public decimal TotalDebits => Lines.Sum(static line => line.debit);

    public decimal TotalCredits => Lines.Sum(static line => line.credit);

    public bool IsBalanced => Math.Abs(TotalDebits - TotalCredits) <= LedgerToleranceConstants.Balance;

    /// <summary>Materializes this instance into a validated, balanced <see cref="JournalEntry"/>.</summary>
    public JournalEntry ToJournalEntry()
    {
        var journalId = Guid.NewGuid();
        var entries = Lines
            .Select(line => new LedgerEntry(
                Guid.NewGuid(),
                journalId,
                Timestamp,
                line.account,
                line.debit,
                line.credit,
                Description,
                line.dimensions))
            .ToList();

        return new JournalEntry(journalId, Timestamp, Description, entries, Metadata);
    }
}
