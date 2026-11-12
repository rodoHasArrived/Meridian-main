using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Application.Subscriptions.Services;
using Meridian.Application.UI;
using Meridian.Core.Subscriptions.Models;
using Meridian.Contracts.SecurityMaster;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Ledger;
using Meridian.Storage.Archival;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>Reserves one opening per account/book, then queues its draft through the shared workbench.</summary>
public sealed class PortfolioOpeningBalanceQueue(
    AutomatedJournalDraftIntakeService intake,
    ConfigStore configuration,
    ILedgerJournalStore store,
    ISecurityMasterQueryService? securities = null,
    IFundAccountService? accounts = null) : IPortfolioOpeningBalanceQueue
{
    public async Task<Guid> QueueAsync(PortfolioImportRun run, CancellationToken ct = default)
    {
        var draft = PortfolioOpeningBalanceProjector.Build(run);
        var scope = run.Request.Scope;
        var accountAuthority = accounts ?? throw new InvalidOperationException("Opening balances require the retained financial account authority.");
        var account = await accountAuthority.GetAccountAsync(scope.FinancialAccountId, ct);
        if (account is null || !account.IsActive || account.EntityId != scope.EntityId ||
            account.FundId?.ToString("D") != scope.FundProfileId)
            throw new InvalidOperationException("The opening entity and fund must match the retained active financial account.");
        if (draft.Lines.Any(line => line.account.Symbol is not null))
        {
            var resolver = securities ?? throw new InvalidOperationException("Opening instruments require an authoritative Security Master resolver.");
            var resolvedLines = new List<(LedgerAccount account, decimal debit, decimal credit, LedgerLineDimensionSet? dimensions)>();
            foreach (var line in draft.Lines)
            {
                if (line.account.Symbol is not { } symbol)
                { resolvedLines.Add(line); continue; }
                var security = await resolver.GetByIdentifierAsync(SecurityIdentifierKind.Ticker, symbol, null, ct)
                    ?? throw new InvalidOperationException($"Opening instrument {symbol} is not resolved in Security Master.");
                if (security.Status != SecurityStatusDto.Active || !string.Equals(security.Currency, run.Snapshot!.Currency, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Opening instrument {symbol} requires active same-currency Security Master evidence.");
                resolvedLines.Add((line.account, line.debit, line.credit,
                    (line.dimensions ?? new LedgerLineDimensionSet()) with { InstrumentId = security.SecurityId }));
            }
            draft = draft with { Lines = resolvedLines };
        }
        var book = await store.GetLedgerBookAsync(scope.LedgerBookId, ct)
            ?? throw new InvalidOperationException("The opening ledger book is unavailable.");
        var period = await store.GetPeriodAsync(scope.PeriodId, ct)
            ?? throw new InvalidOperationException("The opening accounting period is unavailable.");
        if (book.FundStructureNodeId != scope.FinancialAccountId || book.FundProfileId != scope.FundProfileId ||
            period.LedgerBookId != book.LedgerBookId || period.StartDate > scope.EffectiveDate || period.EndDate < scope.EffectiveDate ||
            !string.Equals(book.BaseCurrency, run.Snapshot!.Currency, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The opening book, account, currency and accounting period must agree.");
        var directory = Path.Combine(configuration.GetDataRoot(), "portfolio-imports", "opening-reservations");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{scope.LedgerBookId:N}-{scope.FinancialAccountId:N}.json");
        await using var lease = await AcquireLeaseAsync(path + ".lock", ct);
        OpeningReservation reservation;
        if (File.Exists(path))
        {
            reservation = JsonSerializer.Deserialize(await File.ReadAllTextAsync(path, ct),
                PortfolioOpeningQueueJsonContext.Default.OpeningReservation)
                ?? throw new InvalidDataException("Opening reservation is unreadable.");
            if (reservation.RunId != run.Request.RunId || reservation.SnapshotHash != run.SnapshotHash ||
                reservation.CommandHash != run.CommandHash)
                throw new InvalidOperationException("This account/book already has another opening source; correct it through reversal and rebook.");
            if (reservation.JournalEntryId is { } retainedId)
                return retainedId;
        }
        else
        {
            if (period.Status != "Open")
                throw new InvalidOperationException("An opening draft requires an open accounting period.");
            var history = await store.QueryAsync(new LedgerJournalEntryQuery(LedgerBookId: scope.LedgerBookId), ct);
            if (history.Any(record => record.Entry.Lines.Any(line =>
                    string.Equals(line.Account.FinancialAccountId, scope.FinancialAccountId.ToString("D"), StringComparison.OrdinalIgnoreCase)
                    || line.Account.FinancialAccountId is null)))
                throw new InvalidOperationException("This account/book already has journal activity; use a governed adjustment rather than a second opening.");
            reservation = new(run.Request.RunId, run.CommandHash, run.SnapshotHash!, null);
            await SaveAsync(path, reservation, ct);
        }
        var result = await intake.IntakeDraftsAsync(new AutomatedJournalPreparedDraftIntakeRequest(
            scope.FundProfileId, run.Snapshot!.Currency, [draft], scope.Actor,
            scope.LedgerBookId, scope.PeriodId.ToString("D"), EntityId: scope.EntityId!.Value.ToString("D"), TenantId: scope.TenantId, CompanyId: scope.CompanyId,
            BatchCorrelationId: run.Request.RunId.ToString("D"), AccountingBasis: book.AccountingBasis), ct);
        var id = result.Created.SingleOrDefault()?.JournalEntryId ?? result.Skipped.SingleOrDefault()?.JournalEntryId
            ?? throw new InvalidOperationException("The opening draft was not retained.");
        if (result.Skipped.Any(skip => skip.Disposition is AutomatedJournalDraftIntakeDisposition.ExistingDraftRejected
                or AutomatedJournalDraftIntakeDisposition.ExistingDraftTerminal or AutomatedJournalDraftIntakeDisposition.ProjectionFailed))
            throw new InvalidOperationException("The retained opening draft requires an explicit correction before retry.");
        await SaveAsync(path, reservation with { JournalEntryId = id }, ct);
        return id;
    }

    private static Task SaveAsync(string path, OpeningReservation reservation, CancellationToken ct)
        => AtomicFileWriter.WriteAsync(path, JsonSerializer.Serialize(reservation, PortfolioOpeningQueueJsonContext.Default.OpeningReservation), ct);

    private static async Task<FileStream> AcquireLeaseAsync(string path, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (DateTimeOffset.UtcNow < deadline) { await Task.Delay(50, ct); }
        }
    }
}

internal sealed record OpeningReservation(Guid RunId, string CommandHash, string SnapshotHash, Guid? JournalEntryId);

[JsonSerializable(typeof(OpeningReservation))]
internal partial class PortfolioOpeningQueueJsonContext : JsonSerializerContext;
