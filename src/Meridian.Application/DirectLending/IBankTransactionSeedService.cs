using Meridian.Contracts.DirectLending;

namespace Meridian.Application.DirectLending;

/// <summary>
/// Provides bank transaction seeding for development, demo, and integration-test scenarios.
/// This is intentionally separate from <see cref="IDirectLendingService"/> so that seeding
/// infrastructure is not coupled to the core direct-lending domain service.
/// </summary>
public interface IBankTransactionSeedService
{
    /// <summary>
    /// Seed representative bank transactions for one or more loans.
    /// Useful for development, demos, and integration test setup.
    /// </summary>
    Task<BankTransactionSeedResultDto> SeedBankTransactionsAsync(BankTransactionSeedRequest request, CancellationToken ct = default);
}
