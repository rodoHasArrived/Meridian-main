using System.Globalization;
using Meridian.FinancialOperations.FundAdministration;

namespace Meridian.ProcessTestHelper;

internal static partial class Program
{
    private static async Task<int> PinRecurringObligationAsync(IReadOnlyList<string> args)
    {
        RequireArgumentCount(args, 8);
        var date = DateOnly.ParseExact(args[3], "yyyy-MM-dd", CultureInfo.InvariantCulture);
        RecurringJournalIdentity.ValidateEconomicObligationKeys([args[4]]);
        await File.WriteAllTextAsync(args[5], Environment.ProcessId.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
        await WaitForFileAsync(args[6], TimeSpan.FromSeconds(60)).ConfigureAwait(false);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await using var session = await new FileRecurringJournalStore(args[1]).OpenSessionAsync(timeout.Token).ConfigureAwait(false);
        var claim = await session.ClaimAsync(args[2], date, 1, 1,
            new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), timeout.Token).ConfigureAwait(false);
        string outcome;
        try
        {
            await session.PinEconomicObligationsAsync(claim.OccurrenceKey, [args[4]], timeout.Token).ConfigureAwait(false);
            outcome = "owned";
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("already owned", StringComparison.Ordinal))
        {
            outcome = "overlap";
        }
        await File.WriteAllTextAsync(args[7], outcome, timeout.Token).ConfigureAwait(false);
        return 0;
    }
}
