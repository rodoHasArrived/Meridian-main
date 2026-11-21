using Meridian.Core.Subscriptions.Models;

namespace Meridian.Infrastructure.Adapters.OpenFigi;

/// <summary>All distinct bond candidates. A single complete identity is required for automatic resolution.</summary>
public sealed record BondFigiLookupResult(string Identifier, string IdentifierType, IReadOnlyList<FigiMapping> Candidates)
{
    public FigiMapping? Resolved => Candidates.Count == 1 && !string.IsNullOrWhiteSpace(Candidates[0].Figi)
        && !string.IsNullOrWhiteSpace(Candidates[0].SecurityType) ? Candidates[0] : null;
    public bool IsAmbiguous => Candidates.Count > 1;
}
