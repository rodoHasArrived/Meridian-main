using Meridian.Contracts.Workstation;

namespace Meridian.Strategies.Interfaces;

/// <summary>
/// Durable repository for Strategy Builder design documents.
/// </summary>
public interface IStrategyDesignRepository
{
    /// <summary>Saves a new design document version.</summary>
    Task SaveAsync(StrategyDesignDocument document, CancellationToken ct = default);

    /// <summary>Loads the latest version of a design document, or <c>null</c> when missing.</summary>
    Task<StrategyDesignDocument?> GetAsync(string documentId, CancellationToken ct = default);

    /// <summary>Returns the latest draft summary for every stored document.</summary>
    Task<IReadOnlyList<StrategyDesignDraftSummary>> ListDraftsAsync(CancellationToken ct = default);
}
