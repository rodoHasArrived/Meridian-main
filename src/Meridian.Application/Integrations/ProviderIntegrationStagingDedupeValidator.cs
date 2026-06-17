using Meridian.Contracts.Integrations;

namespace Meridian.Application.Integrations;

internal sealed class ProviderIntegrationStagingDedupeValidator
{
    private readonly HashSet<string> acceptedDedupeKeys = new(StringComparer.Ordinal);

    public ValidationIssueDto? TryAccept(
        string dedupeKey,
        ProviderCapabilityKindDto capability,
        string? targetField)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dedupeKey);

        if (acceptedDedupeKeys.Add(dedupeKey))
        {
            return null;
        }

        return new ValidationIssueDto(
            "duplicate.dedupe-key",
            ProviderIntegrationIssueSeverityDto.Critical,
            $"Duplicate {capability} provider record identity '{dedupeKey}' was found in this run.",
            targetField,
            "Confirm the provider source id, mapping, or file contents before staging duplicate records.");
    }
}
