using System.Text.RegularExpressions;

namespace Meridian.FinancialOperations.FundAdministration;

/// <summary>
/// One node in a reusable onboarding template. Code and name are templated with <c>{parameter}</c>
/// placeholders resolved at apply time, so a single template can stamp out the standard
/// organization → entity → portfolio → account → book skeleton for many funds.
/// </summary>
public sealed record OnboardingTemplateNode(
    string NodeType,
    string Key,
    string CodeTemplate,
    string NameTemplate,
    string? ParentKey = null,
    string? BaseCurrency = null,
    IReadOnlyDictionary<string, string>? Attributes = null)
{
    /// <summary>Well-known node types administered by FundStudio-style controls.</summary>
    public static class Types
    {
        public const string Organization = "Organization";
        public const string Entity = "Entity";
        public const string Portfolio = "Portfolio";
        public const string Account = "Account";
        public const string Book = "Book";
    }

    public IReadOnlyDictionary<string, string> Attributes { get; init; } =
        Attributes ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>A resolved onboarding node: concrete code/name after placeholder substitution.</summary>
public sealed record OnboardingPlanNode(
    string NodeType,
    string Key,
    string Code,
    string Name,
    string? ParentKey,
    string? BaseCurrency,
    IReadOnlyDictionary<string, string> Attributes);

/// <summary>The concrete structure produced by applying an <see cref="OnboardingTemplate"/>.</summary>
public sealed record OnboardingPlan(
    string TemplateId,
    IReadOnlyList<OnboardingPlanNode> Nodes,
    IReadOnlyDictionary<string, string> Parameters);

/// <summary>
/// A named, reusable onboarding blueprint. Applying it with a set of parameters yields a concrete
/// <see cref="OnboardingPlan"/> — a normalized list of organization/entity/portfolio/account/book
/// nodes ready to be created through the fund-structure services — without re-keying the skeleton for
/// each new fund.
/// </summary>
public sealed record OnboardingTemplate
{
    private static readonly Regex PlaceholderPattern = new(@"\{([A-Za-z0-9_]+)\}", RegexOptions.Compiled);

    public OnboardingTemplate(
        string templateId,
        string name,
        string description,
        IReadOnlyList<OnboardingTemplateNode> nodes,
        string createdBy,
        DateTimeOffset createdAtUtc,
        IReadOnlyList<string>? requiredParameters = null)
    {
        if (string.IsNullOrWhiteSpace(templateId))
            throw new ArgumentException("Onboarding template identifier must not be null or whitespace.", nameof(templateId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Onboarding template name must not be null or whitespace.", nameof(name));
        ArgumentNullException.ThrowIfNull(nodes);
        if (nodes.Count == 0)
            throw new ArgumentException("An onboarding template must define at least one node.", nameof(nodes));
        if (string.IsNullOrWhiteSpace(createdBy))
            throw new ArgumentException("Onboarding template must record its author.", nameof(createdBy));

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
        {
            ArgumentNullException.ThrowIfNull(node);
            if (string.IsNullOrWhiteSpace(node.NodeType))
                throw new ArgumentException("Every onboarding node must declare a node type.", nameof(nodes));
            if (string.IsNullOrWhiteSpace(node.Key))
                throw new ArgumentException("Every onboarding node must declare a unique key.", nameof(nodes));
            if (!keys.Add(node.Key.Trim()))
                throw new ArgumentException($"Onboarding node key '{node.Key}' is duplicated.", nameof(nodes));
        }

        foreach (var node in nodes)
        {
            if (node.ParentKey is { } parentKey && !string.IsNullOrWhiteSpace(parentKey) && !keys.Contains(parentKey.Trim()))
                throw new ArgumentException($"Onboarding node '{node.Key}' references unknown parent '{parentKey}'.", nameof(nodes));
        }

        TemplateId = templateId.Trim();
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? string.Empty : description.Trim();
        Nodes = nodes.ToArray();
        CreatedBy = createdBy.Trim();
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        RequiredParameters = (requiredParameters ?? InferParameters(nodes))
            .Where(static parameter => !string.IsNullOrWhiteSpace(parameter))
            .Select(static parameter => parameter.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string TemplateId { get; }

    public string Name { get; }

    public string Description { get; }

    public IReadOnlyList<OnboardingTemplateNode> Nodes { get; }

    public string CreatedBy { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>Parameter names that must be supplied to <see cref="Apply"/>.</summary>
    public IReadOnlyList<string> RequiredParameters { get; }

    /// <summary>Resolves this template's placeholders against <paramref name="parameters"/>.</summary>
    /// <exception cref="ArgumentException">Thrown when a required parameter is missing.</exception>
    public OnboardingPlan Apply(IReadOnlyDictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var normalized = new Dictionary<string, string>(parameters, StringComparer.OrdinalIgnoreCase);

        var missing = RequiredParameters.Where(parameter => !normalized.ContainsKey(parameter)).ToArray();
        if (missing.Length > 0)
        {
            throw new ArgumentException(
                $"Onboarding template '{TemplateId}' is missing required parameter(s): {string.Join(", ", missing)}.",
                nameof(parameters));
        }

        var planNodes = Nodes
            .Select(node => new OnboardingPlanNode(
                node.NodeType.Trim(),
                node.Key.Trim(),
                Resolve(node.CodeTemplate, normalized),
                Resolve(node.NameTemplate, normalized),
                string.IsNullOrWhiteSpace(node.ParentKey) ? null : node.ParentKey.Trim(),
                string.IsNullOrWhiteSpace(node.BaseCurrency) ? null : Resolve(node.BaseCurrency, normalized),
                node.Attributes.ToDictionary(
                    pair => pair.Key,
                    pair => Resolve(pair.Value, normalized),
                    StringComparer.OrdinalIgnoreCase)))
            .ToArray();

        return new OnboardingPlan(TemplateId, planNodes, normalized);
    }

    private static string Resolve(string? template, IReadOnlyDictionary<string, string> parameters)
    {
        if (string.IsNullOrEmpty(template))
            return string.Empty;

        return PlaceholderPattern.Replace(template, match =>
        {
            var key = match.Groups[1].Value;
            if (parameters.TryGetValue(key, out var value))
                return value;

            throw new ArgumentException($"Onboarding placeholder '{{{key}}}' has no supplied value.", nameof(parameters));
        });
    }

    private static IReadOnlyList<string> InferParameters(IReadOnlyList<OnboardingTemplateNode> nodes)
    {
        var parameters = new List<string>();
        foreach (var node in nodes)
        {
            CollectPlaceholders(node.CodeTemplate, parameters);
            CollectPlaceholders(node.NameTemplate, parameters);
            CollectPlaceholders(node.BaseCurrency, parameters);
            foreach (var value in node.Attributes.Values)
                CollectPlaceholders(value, parameters);
        }

        return parameters;
    }

    private static void CollectPlaceholders(string? template, List<string> sink)
    {
        if (string.IsNullOrEmpty(template))
            return;

        foreach (Match match in PlaceholderPattern.Matches(template))
            sink.Add(match.Groups[1].Value);
    }
}
