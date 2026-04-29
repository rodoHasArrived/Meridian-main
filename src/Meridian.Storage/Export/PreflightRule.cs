using System.Collections.ObjectModel;

namespace Meridian.Storage.Export;

/// <summary>
/// Severity level emitted by preflight rules.
/// </summary>
public enum PreflightSeverity : byte
{
    Info,
    Warning,
    Error
}

/// <summary>
/// A standardized issue emitted by a preflight rule.
/// </summary>
/// <param name="RuleId">Stable rule identifier.</param>
/// <param name="Code">Domain-specific issue code.</param>
/// <param name="Severity">Issue severity.</param>
/// <param name="Message">Human-readable issue message.</param>
/// <param name="Remediation">Optional remediation hint.</param>
/// <param name="Details">Optional structured details.</param>
public sealed record PreflightIssue(
    string RuleId,
    string Code,
    PreflightSeverity Severity,
    string Message,
    string? Remediation = null,
    IReadOnlyDictionary<string, object>? Details = null);

/// <summary>
/// Abstraction for composable preflight rule evaluation.
/// </summary>
/// <typeparam name="TContext">Evaluation context type.</typeparam>
public interface IPreflightRule<in TContext>
{
    /// <summary>Stable rule identifier.</summary>
    string Id { get; }

    /// <summary>Evaluates this rule against the provided context.</summary>
    PreflightIssue? Evaluate(TContext context);
}

/// <summary>
/// Reusable engine that evaluates an ordered rule set against one context.
/// </summary>
/// <typeparam name="TContext">Evaluation context type.</typeparam>
public sealed class PreflightEngine<TContext>
{
    private readonly IReadOnlyList<IPreflightRule<TContext>> _rules;

    public PreflightEngine(IEnumerable<IPreflightRule<TContext>> rules)
    {
        _rules = new ReadOnlyCollection<IPreflightRule<TContext>>(rules.ToList());
    }

    public IReadOnlyList<PreflightIssue> Evaluate(TContext context)
    {
        var issues = new List<PreflightIssue>();

        foreach (var rule in _rules)
        {
            var issue = rule.Evaluate(context);
            if (issue is not null)
                issues.Add(issue);
        }

        return issues;
    }
}
