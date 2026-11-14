using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Application.UI;
using Meridian.Contracts.Integrity;
using Meridian.Core.Subscriptions.Models;
using Meridian.Storage.Archival;

namespace Meridian.Application.Subscriptions.Services;

/// <summary>Queues the retained complete source population through the ordinary human approval lifecycle.</summary>
public interface IPortfolioOpeningBalanceQueue
{
    Task<Guid> QueueAsync(PortfolioImportRun run, CancellationToken ct = default);
}

/// <summary>
/// Retains a broker snapshot before any effects. A retry resumes that exact population rather than
/// refetching a changed account. Copy-on-write checkpoints and a process-independent file lease
/// cover response loss between a side effect and its receipt.
/// </summary>
public sealed class PortfolioImportRunService(
    ConfigStore configuration,
    PortfolioImportService importer,
    WatchlistService watchlists,
    IPortfolioOpeningBalanceQueue? openingBalances = null)
{
    public async Task<PortfolioImportRun?> GetAsync(string tenantId, string companyId, Guid runId, CancellationToken ct = default)
    {
        var path = RunPath(tenantId, companyId, runId);
        if (!File.Exists(path))
            return null;
        var run = await ReadAsync(path, ct);
        if (run.Request.Scope.TenantId != tenantId || run.Request.Scope.CompanyId != companyId || run.Request.RunId != runId)
            throw new InvalidDataException("The retained import belongs to another scope.");
        return run;
    }

    public async Task<PortfolioImportRun> RunAsync(PortfolioImportRunRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Scope);
        ArgumentNullException.ThrowIfNull(request.Options);
        if (request.RunId == Guid.Empty || request.Scope.FinancialAccountId == Guid.Empty ||
            request.Scope.LedgerBookId == Guid.Empty || request.Scope.PeriodId == Guid.Empty || request.Scope.EffectiveDate == default)
            throw new ArgumentException("An import requires explicit run, account, ledger book, period and effective-date identities.");
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Scope.Actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Scope.FundProfileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Options.BrokerAccountId);
        if (request.PrepareOpeningBalances && request.Scope.EntityId.GetValueOrDefault() == Guid.Empty)
            throw new ArgumentException("Opening balances require the retained financial account's entity identity.");
        if (request.Broker is not ("alpaca" or "ib" or "interactivebrokers"))
            throw new ArgumentException("A durable account import requires a supported broker source.");
        var path = RunPath(request.Scope.TenantId, request.Scope.CompanyId, request.RunId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var lease = await AcquireLeaseAsync(path + ".lock", ct);
        var hash = Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(request, PortfolioImportRunJsonContext.Default.PortfolioImportRunRequest));
        var run = File.Exists(path) ? await ReadAsync(path, ct) : new PortfolioImportRun(
            request, hash, PortfolioImportRunStatus.Running, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        if (run.CommandHash != hash)
            throw new InvalidOperationException("This import run already retains a different command or accounting scope.");
        if (run.Status == PortfolioImportRunStatus.Succeeded)
            return run;
        await SaveAsync(path, run with { Status = PortfolioImportRunStatus.Running, Error = null }, ct);
        try
        {
            if (run.Snapshot is null)
            {
                var snapshot = await importer.GetPortfolioSummaryAsync(request.Broker, request.Options.BrokerAccountId, ct)
                    ?? throw new InvalidOperationException("The selected broker account is unavailable.");
                var existing = (configuration.Load().Symbols ?? []).Select(s => s.Symbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var plan = PortfolioImportService.SelectPositions(snapshot, request.Options)
                    .Where(p => !request.Options.SkipExisting || !existing.Contains(p.Symbol))
                    .Select(p => p.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                run = run with { Snapshot = snapshot, SnapshotHash = SnapshotHash(snapshot), PlannedSymbols = plan };
                await SaveAsync(path, run, ct);
            }
            if (run.Subscriptions is null)
            {
                var plan = run.PlannedSymbols ?? throw new InvalidDataException("The retained import subscription plan is missing.");
                var plannedSnapshot = run.Snapshot with { Positions = run.Snapshot.Positions.Where(p => plan.Contains(p.Symbol, StringComparer.OrdinalIgnoreCase)).ToArray() };
                var result = await importer.ProcessPortfolioAsync(plannedSnapshot, request.Options with { CreateWatchlist = false, SkipExisting = false }, ct);
                if (!result.Success || result.FailedCount != 0)
                    throw new InvalidOperationException("Subscription import was incomplete.");
                run = run with { Subscriptions = result };
                await SaveAsync(path, run, ct);
            }
            if (request.Options.CreateWatchlist && run.WatchlistId is null)
            {
                var watchlist = await watchlists.CreateWatchlistAsync(new CreateWatchlistRequest(
                    Name: request.Options.WatchlistName ?? $"{request.Broker} import {request.RunId:D}",
                    Symbols: run.PlannedSymbols?.ToArray() ?? throw new InvalidDataException("The retained import plan is missing.")),
                    "import_" + Sha256Digest.ComputeUtf8(path)[..24], ct);
                run = run with { WatchlistId = watchlist.Id };
                await SaveAsync(path, run, ct);
            }
            if (request.PrepareOpeningBalances && run.OpeningJournalEntryId is null)
            {
                var queue = openingBalances ?? throw new InvalidOperationException("The governed opening-balance draft queue is unavailable.");
                var id = await queue.QueueAsync(run, ct);
                if (id == Guid.Empty)
                    throw new InvalidOperationException("Opening-balance intake returned no retained draft identity.");
                run = run with { OpeningJournalEntryId = id };
                await SaveAsync(path, run, ct);
            }
            run = run with { Status = PortfolioImportRunStatus.Succeeded, Error = null, UpdatedAtUtc = DateTimeOffset.UtcNow };
            await SaveAsync(path, run, ct);
            return run;
        }
        catch (Exception ex)
        {
            run = run with
            {
                Status = run.Snapshot is null ? PortfolioImportRunStatus.Failed : PortfolioImportRunStatus.Partial,
                Error = ex is OperationCanceledException ? "Import cancelled; resume the same run." : ex.Message,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            await SaveAsync(path, run, CancellationToken.None);
            if (ex is OperationCanceledException)
                throw;
            return run;
        }
    }

    public static string SnapshotHash(PortfolioSummary snapshot)
        => Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(snapshot, PortfolioImportRunJsonContext.Default.PortfolioSummary));

    private static async Task<PortfolioImportRun> ReadAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var run = await JsonSerializer.DeserializeAsync(stream, PortfolioImportRunJsonContext.Default.PortfolioImportRun, ct)
            ?? throw new InvalidDataException("The retained import is unreadable.");
        var commandHash = Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(run.Request, PortfolioImportRunJsonContext.Default.PortfolioImportRunRequest));
        if (run.CommandHash != commandHash || (run.Snapshot is not null && run.SnapshotHash != SnapshotHash(run.Snapshot)))
            throw new InvalidDataException("The retained import command or broker snapshot failed its content-hash check.");
        return run;
    }

    private static Task SaveAsync(string path, PortfolioImportRun run, CancellationToken ct)
        => AtomicFileWriter.WriteAsync(path, JsonSerializer.Serialize(run, PortfolioImportRunJsonContext.Default.PortfolioImportRun), ct);

    private string RunPath(string tenant, string company, Guid runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenant);
        ArgumentException.ThrowIfNullOrWhiteSpace(company);
        return Path.Combine(configuration.GetDataRoot(), "portfolio-imports", Sha256Digest.ComputeUtf8(tenant + "\n" + company), runId.ToString("N") + ".json");
    }

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

[JsonSerializable(typeof(PortfolioImportRun))]
[JsonSerializable(typeof(PortfolioImportRunRequest))]
[JsonSerializable(typeof(PortfolioSummary))]
internal partial class PortfolioImportRunJsonContext : JsonSerializerContext;
