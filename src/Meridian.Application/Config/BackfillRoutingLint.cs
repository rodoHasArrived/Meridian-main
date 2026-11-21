using Meridian.Core.Config;

namespace Meridian.Application.Config;

/// <summary>Pure configuration lint. Does not read secrets or claim vendor coverage or runtime vault availability.</summary>
public static class BackfillRoutingLint
{
    public static IReadOnlyList<string> Evaluate(AppConfig config)
    {
        var providers = config.Backfill?.Providers;
        if (providers?.Synthetic?.Enabled != true)
            return [];
        var real = BackfillProviderCatalog.Providers.Where(p => p.Id != "openfigi")
            .Select(p => (Provider: p, Options: BackfillProviderCatalog.Read(providers, p.Id)))
            .Where(p => p.Options.Enabled).ToArray();
        var warnings = new List<string>();
        if (real.Length == 0)
        {
            warnings.Add("Backfill routing: only FABRICATED Synthetic bars are enabled. These are sample data, not observed market evidence.");
        }
        else
        {
            warnings.Add("Backfill routing: Synthetic is enabled and can serve FABRICATED bars if real vendors are unavailable. Preview routing before using results as research evidence.");
            if (real.Any(p => providers.Synthetic.Priority <= p.Options.Priority))
                warnings.Add("Backfill routing: Numeric Synthetic priority would outrank an enabled real vendor; the observed-vendor guard keeps real vendors first. Preview fallback routing; Source/Sample provenance must remain intact.");
        }
        if (providers.Tiingo is { Enabled: true } && string.IsNullOrWhiteSpace(providers.Tiingo.ApiToken))
            warnings.Add("Backfill routing: Tiingo has no resolved token in configuration; verify its retained runtime credentials. Synthetic may serve when no real vendor is available.");
        return warnings;
    }
}
