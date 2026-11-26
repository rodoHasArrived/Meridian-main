using System.Threading.Channels;
using Meridian.Contracts.Configuration;
using Meridian.Core.Config;
using Serilog;

namespace Meridian.Application.UI;

/// <summary>Collector-owned application and acknowledgement of saves from either process.</summary>
internal sealed class CollectorConfigurationRuntime : IConfigurationRuntimeApplier, IAsyncDisposable
{
    private readonly ConfigStore _store;
    private readonly ConfigurationRevisionJournal _journal;
    private readonly IConfigurationRuntimeApplier _applier;
    private readonly ILogger _log;
    private readonly Guid _instanceId = Guid.NewGuid();
    private readonly FileStream? _activeLease;
    private readonly IDisposable _attachment;
    private readonly CancellationTokenSource _shutdown;
    private readonly Channel<bool> _reloads = Channel.CreateBounded<bool>(1);
    private readonly Task _worker;
    private readonly ConfigurationRevision? _startupRevision;
    private readonly CancellationToken _lifetime;
    private readonly SemaphoreSlim _runtimeGate = new(1, 1);
    private AppConfig _observed;

    internal CollectorConfigurationRuntime(
        ConfigStore store, IConfigurationRuntimeApplier applier, AppConfig startup, ILogger log, CancellationToken lifetime)
    {
        _store = store;
        _journal = new ConfigurationRevisionJournal(store.ConfigPath);
        _applier = applier;
        _observed = startup;
        _log = log;
        _shutdown = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        _lifetime = _shutdown.Token;
        _startupRevision = _journal.ReadCurrent()?.Revision;
        try
        {
            _activeLease = _journal.AcquireCollectorLease(_instanceId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning("Collector configuration acknowledgement is unavailable ({ErrorType})", ex.GetType().Name);
        }

        _attachment = ConfigurationRuntimeAppliers.Attach(store.ConfigPath, this);
        _worker = WatchAsync(_lifetime);
    }

    public string Name => _applier.Name;

    public bool Handles(string path) => _applier.Handles(path);

    /// <remarks>Called with the configuration write lease held by the saving process.</remarks>
    public async Task<IReadOnlyList<ConfigurationApplyEntry>> ApplyAsync(
        AppConfig previous, AppConfig next, IReadOnlyList<string> changedPaths, CancellationToken ct)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime);
        return await ApplyWithEvidenceAsync(next, changedPaths, _journal.ReadCurrent(), operation.Token).ConfigureAwait(false);
    }

    internal void SignalReload() => _reloads.Writer.TryWrite(true);

    private async Task WatchAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var _ in _reloads.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    using var lease = await _store.AcquireWriteLeaseAsync(ct).ConfigureAwait(false);
                    var revision = _journal.ReadCurrent(out var json);
                    if (revision?.Acknowledgement?.CollectorInstanceId == _instanceId)
                        continue;

                    var next = json is null ? _store.LoadRequired() : _store.LoadRequired(json);
                    var changed = ConfigurationChangeTracker.CollectorChanges(_observed, next)
                        .Select(change => change.Path).Where(Handles).ToArray();
                    // Starting with a document is not evidence of a live change. A save made
                    // during startup still applies if the collector built from an earlier document.
                    if (revision?.Revision == _startupRevision && changed.Length == 0)
                        continue;

                    // A legacy or externally edited file still reloads, without certifying a save.
                    var paths = revision is null ? changed : changed.Concat(revision.CollectorPaths).Distinct().ToArray();
                    await ApplyWithEvidenceAsync(next, paths, revision, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.Warning("Collector could not inspect the latest configuration revision ({ErrorType})",
                        ex.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private async Task<IReadOnlyList<ConfigurationApplyEntry>> ApplyWithEvidenceAsync(
        AppConfig next, IReadOnlyList<string> changedPaths, ConfigurationRevisionRecord? revision, CancellationToken ct)
    {
        var paths = changedPaths.Where(Handles).ToArray();
        if (paths.Length == 0)
            return [];

        await _runtimeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            IReadOnlyList<ConfigurationApplyEntry> entries;
            try
            {
                entries = await _applier.ApplyAsync(_observed, next, paths, ct).ConfigureAwait(false);
                _observed = next;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                entries = paths.Select(path => new ConfigurationApplyEntry(path, ConfigurationApplyKind.Deferred,
                    "The collector stopped waiting for application.")).ToArray();
            }
            catch (Exception ex)
            {
                _log.Warning("Collector could not apply the saved configuration revision ({ErrorType})", ex.GetType().Name);
                entries = paths.Select(path => new ConfigurationApplyEntry(path, ConfigurationApplyKind.Failed,
                    "The collector reported a runtime application failure.")).ToArray();
            }

            if (_activeLease is not null && revision is not null)
                await _journal.RecordAcknowledgementAsync(revision, _instanceId, entries).ConfigureAwait(false);

            return entries;
        }
        finally
        {
            _runtimeGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _attachment.Dispose();
        _shutdown.Cancel();
        await _worker.ConfigureAwait(false);
        // Drain an in-process save that acquired this applier before it detached.
        await _runtimeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            _activeLease?.Dispose();
            _journal.ReleaseCollectorLease(_instanceId);
            _shutdown.Dispose();
        }
        finally
        {
            _runtimeGate.Release();
        }
    }
}
