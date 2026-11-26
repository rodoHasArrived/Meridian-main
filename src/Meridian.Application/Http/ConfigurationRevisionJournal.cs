using System.Text;
using System.Text.Json;
using Meridian.Application.Composition.Startup;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.Integrity;
using Meridian.Core.IO;
using Meridian.Core.Logging;
using Meridian.Storage.Archival;

namespace Meridian.Application.UI;

/// <summary>Retains the current save and collector evidence beside the shared configuration file.</summary>
/// <remarks>All writes are serialized by ConfigStore's cross-process write lease. Readers validate
/// the exact file before using a receipt. A prepared or mismatched record carries no activation evidence.</remarks>
internal sealed class ConfigurationRevisionJournal(string configPath)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string _journalPath = configPath + ".runtime-revision.json";

    internal bool Exists => File.Exists(_journalPath);

    internal static bool IsCollectorPath(string path) =>
        path.Equals("dataSources", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("dataSources.", StringComparison.OrdinalIgnoreCase);

    internal async Task<ConfigurationRevisionRecord> PrepareAsync(
        string json, IReadOnlyList<ConfigurationFieldChange> changes, CancellationToken ct)
    {
        var previous = ReadCurrent();
        var confirmed = ActiveAcknowledgement(previous)?.Entries.Where(entry => HasTakenEffect(entry.Kind))
            .Select(entry => entry.Path).ToHashSet(StringComparer.Ordinal);

        var paths = (previous?.CollectorPaths ?? []).Where(path => confirmed?.Contains(path) != true)
            .Concat(changes.Select(change => change.Path))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var proposed = new ConfigurationRevisionRecord(
            new ConfigurationRevision(Guid.NewGuid(), Sha256Digest.ComputeUtf8(json)),
            changes,
            paths,
            CommittedAtUtc: null,
            FileWriteTimeUtc: null,
            Acknowledgement: null);
        await WriteAsync(proposed, ct).ConfigureAwait(false);
        return proposed;
    }

    internal async Task<ConfigurationRevisionRecord?> ConfirmSaveAsync(ConfigurationRevisionRecord proposed)
    {
        try
        {
            using var document = OpenDocument(configPath);
            if (Sha256Digest.Compute(document) != proposed.Revision.ContentHash)
                return null;

            var committed = proposed with
            {
                CommittedAtUtc = DateTimeOffset.UtcNow,
                FileWriteTimeUtc = File.GetLastWriteTimeUtc(document.SafeFileHandle)
            };
            // The document has already committed. HTTP cancellation must not interrupt recording it.
            await WriteAsync(committed, CancellationToken.None).ConfigureAwait(false);
            return committed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LoggingSetup.ForContext<ConfigurationRevisionJournal>().Warning(
                "Configuration was saved but its revision receipt could not be retained ({ErrorType})",
                ex.GetType().Name);
            return null;
        }
    }

    internal ConfigurationRevisionRecord? ReadCurrent() => ReadCurrentCore(readJson: false, out _);

    internal ConfigurationRevisionRecord? ReadCurrent(out string? json) => ReadCurrentCore(readJson: true, out json);

    private ConfigurationRevisionRecord? ReadCurrentCore(bool readJson, out string? json)
    {
        json = null;
        try
        {
            using var journal = OpenDocument(_journalPath);
            var record = JsonSerializer.Deserialize<ConfigurationRevisionRecord>(journal, Json);
            if (record is not
                {
                    CommittedAtUtc: not null, FileWriteTimeUtc: { } stamp,
                    Revision.Id: var revisionId, Changes: not null, CollectorPaths: not null
                } || revisionId == Guid.Empty)
                return null;

            using var document = OpenDocument(configPath);
            if (stamp != File.GetLastWriteTimeUtc(document.SafeFileHandle))
                return null;
            if (!readJson)
                return record.Revision.ContentHash == Sha256Digest.Compute(document) ? record : null;

            using var content = new MemoryStream();
            document.CopyTo(content);
            var bytes = content.GetBuffer().AsSpan(0, checked((int)content.Length));
            if (record.Revision.ContentHash != Sha256Digest.Compute(bytes))
                return null;
            json = Encoding.UTF8.GetString(bytes);
            return record;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    internal async Task RecordAcknowledgementAsync(
        ConfigurationRevisionRecord record, Guid collectorId, IReadOnlyList<ConfigurationApplyEntry> entries)
    {
        // The caller still holds the configuration lease. Recheck the document in case an
        // external editor wrote without taking the lease during the runtime operation.
        if (ReadCurrent()?.Revision != record.Revision)
            return;

        var paths = record.CollectorPaths.ToHashSet(StringComparer.Ordinal);
        var evidence = entries.Where(entry => paths.Contains(entry.Path))
            .DistinctBy(entry => entry.Path, StringComparer.Ordinal)
            .Select(entry => new ConfigurationApplyEntry(entry.Path, entry.Kind, FixedDetail(entry.Detail))).ToArray();
        var acknowledgement = new ConfigurationRuntimeAcknowledgement(
            record.Revision, collectorId, DateTimeOffset.UtcNow, evidence);
        try
        {
            await WriteAsync(record with { Acknowledgement = acknowledgement }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LoggingSetup.ForContext<ConfigurationRevisionJournal>().Warning(
                "Collector applied configuration but could not retain the acknowledgement ({ErrorType})",
                ex.GetType().Name);
        }
    }

    // Keep fixed operator explanations. Other runtime details may include configuration values.
    private static string? FixedDetail(string? detail)
        => detail is SingleSourceProviderConfigApplier.FailoverRestartMessage or SingleSourceProviderConfigApplier.EligibilityOnlyMessage
            ? detail : null;

    internal ConfigurationRuntimeAcknowledgement? ActiveAcknowledgement(ConfigurationRevisionRecord? record)
        => record?.Acknowledgement is { CollectorInstanceId: var id, Entries: not null } acknowledgement
            && acknowledgement.Revision == record.Revision
            && id != Guid.Empty
            && IsCollectorActive(id)
            ? acknowledgement : null;

    internal FileStream AcquireCollectorLease(Guid id) =>
        new(CollectorLeasePath(id), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, bufferSize: 1);

    internal void ReleaseCollectorLease(Guid id)
    {
        try
        { File.Delete(CollectorLeasePath(id)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private bool IsCollectorActive(Guid id)
    {
        try
        {
            using var unlocked = new FileStream(
                CollectorLeasePath(id), FileMode.Open, FileAccess.Read, FileShare.None, bufferSize: 1);
            return false;
        }
        catch (IOException ex) when (ExclusiveFileLock.IsContention(ex))
        {
            // The collector owns this unique file for its process lifetime. An abandoned file
            // after process termination can be opened and never proves a collector is active.
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private string CollectorLeasePath(Guid id) => configPath + $".runtime-collector.{id:N}.lock";

    internal static bool HasTakenEffect(ConfigurationApplyKind kind)
        => kind is ConfigurationApplyKind.AppliedLive or ConfigurationApplyKind.ProviderReconfigured
            or ConfigurationApplyKind.NoRuntimeEffect;

    private Task WriteAsync(ConfigurationRevisionRecord record, CancellationToken ct)
        => AtomicFileWriter.WriteAsync(_journalPath, JsonSerializer.Serialize(record, Json), ct);

    private static FileStream OpenDocument(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
}

internal sealed record ConfigurationRevisionRecord(
    ConfigurationRevision Revision,
    IReadOnlyList<ConfigurationFieldChange> Changes,
    IReadOnlyList<string> CollectorPaths,
    DateTimeOffset? CommittedAtUtc,
    DateTime? FileWriteTimeUtc,
    ConfigurationRuntimeAcknowledgement? Acknowledgement);
