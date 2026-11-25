using System.Text.Json;
using System.Text.Json.Nodes;
using System.Globalization;
using System.Numerics;
using Meridian.Application.Composition;
using Meridian.Reporting;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Admin-owned host configuration seam. Empty configuration provides no policy; never falls back
/// to browser defaults. Hosts may replace this DI interface with a durable policy registry.
/// </summary>
public sealed class ConfiguredReportingReviewMaterialityPolicyProvider(CompositionConfiguration configuration)
    : IReportingReviewMaterialityPolicyProvider
{
    public const string ConfigurationKey = "MERIDIAN_REPORTING_REVIEW_POLICIES";

    private static string NormalizeThreshold(string input)
    {
        var text = input.Trim();
        if (text.Length is 0 or > 1024)
            throw new JsonException("A bounded decimal threshold string is required.");
        var negative = text[0] == '-';
        var unsigned = text[0] is '+' or '-' ? text[1..] : text;
        var parts = unsigned.Split('.');
        if (parts.Length is < 1 or > 2 || parts.Any(part => part.Length == 0 || part.Any(c => !char.IsAsciiDigit(c)))
            || !decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed))
            throw new JsonException("The materiality threshold is not a representable decimal.");
        var coefficient = BigInteger.Parse(string.Concat(parts), CultureInfo.InvariantCulture) * (negative ? -1 : 1);
        var scale = parts.Length == 2 ? parts[1].Length : 0;
        var bits = decimal.GetBits(parsed);
        var parsedCoefficient = ((BigInteger)(uint)bits[0] | (BigInteger)(uint)bits[1] << 32 | (BigInteger)(uint)bits[2] << 64)
            * ((bits[3] & int.MinValue) != 0 ? -1 : 1);
        var parsedScale = (bits[3] >> 16) & 255;
        if (coefficient * BigInteger.Pow(10, parsedScale) != parsedCoefficient * BigInteger.Pow(10, scale))
            throw new JsonException("The materiality threshold would round approved precision.");
        return ReportingIncomeDecimalJsonConverter.Format(parsed);
    }

    public ValueTask<ReportingReviewMaterialityPolicy?> ResolveAsync(ReportingOperationalScope scope,
        string templateId, string templateVersion, string currency, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var json = configuration[ConfigurationKey];
        if (string.IsNullOrWhiteSpace(json))
            return ValueTask.FromResult<ReportingReviewMaterialityPolicy?>(null);
        IReadOnlyList<ReportingReviewMaterialityPolicy> policies;
        try
        {
            var entries = JsonNode.Parse(json) as JsonArray ?? throw new JsonException("A policy array is required.");
            foreach (var entry in entries)
            {
                if (entry is not JsonObject policy || policy["metrics"] is not JsonArray metrics)
                    throw new JsonException("Explicit metric policies are required.");
                foreach (var entryMetric in metrics)
                {
                    if (entryMetric is not JsonObject metric)
                        throw new JsonException("Each metric policy must be an object.");
                    foreach (var name in new[] { "absoluteVariance", "relativeVariancePercent" })
                    {
                        if (!metric.TryGetPropertyValue(name, out var threshold) || threshold is null)
                            continue;
                        if (threshold is not JsonValue value || !value.TryGetValue<string>(out var text))
                            throw new JsonException("Materiality thresholds require decimal strings or null; JSON numbers may round approved precision.");
                        metric[name] = NormalizeThreshold(text);
                    }
                }
            }
            policies = JsonSerializer.Deserialize(entries.ToJsonString(), ReportingRunReviewJsonContext.Default.MaterialityPolicies)
                ?? throw new JsonException("A policy array is required.");
        }
        catch (JsonException exception) { throw new InvalidDataException("Reporting review policy configuration is invalid.", exception); }
        foreach (var policy in policies)
            ReportingReviewMateriality.ValidatePolicy(policy);
        var matches = policies.Where(p => p.Scope == scope && p.TemplateId == templateId
            && p.TemplateVersion == templateVersion && p.Currency == currency).ToArray();
        if (matches.Length > 1)
            throw new InvalidDataException("Multiple reporting review policies match the same scope, template revision and currency.");
        return ValueTask.FromResult(matches.SingleOrDefault());
    }
}
