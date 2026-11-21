using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Etl;
using Meridian.Storage.Etl;
using Xunit;

namespace Meridian.Tests.Storage.Etl;

/// <summary>
/// Concurrent appends to the retained ETL audit and reject logs must not interleave or tear lines.
/// </summary>
public sealed class EtlLogAppendConcurrencyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"meridian-etl-append-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task AuditStore_ConcurrentWrites_ProduceOneWholeJsonLinePerEvent()
    {
        var store = new EtlAuditStore(_root);
        // Long messages make interleaving likely if appends are not serialized.
        var payload = new string('x', 8_192);

        await Task.WhenAll(Enumerable.Range(0, 200).Select(i => Task.Run(() =>
            store.WriteEventAsync("job-1", new EtlAuditEvent { Stage = $"stage-{i}", Message = payload }))));

        var lines = await File.ReadAllLinesAsync(store.GetAuditPath("job-1", "events.jsonl"));
        lines.Should().HaveCount(200);
        var stages = lines.Select(line => JsonSerializer.Deserialize<EtlAuditEvent>(line)!.Stage).ToArray();
        stages.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task RejectSink_ConcurrentAppends_ProduceOneWholeJsonLinePerRecord()
    {
        var sink = new EtlRejectSink(_root);
        var payload = new string('y', 8_192);

        await Task.WhenAll(Enumerable.Range(0, 200).Select(i => Task.Run(() =>
            sink.AppendAsync("job-2", new EtlRejectRecord
            {
                SourceFileName = "source.csv",
                RecordIndex = i,
                RejectCode = "BAD_ROW",
                RejectMessage = payload,
                RawLine = payload,
            }))));

        var lines = await File.ReadAllLinesAsync(Path.Combine(_root, "_etl", "rejects", "job-2", "rejects.jsonl"));
        lines.Should().HaveCount(200);
        lines.Select(line => JsonSerializer.Deserialize<EtlRejectRecord>(line)!.RecordIndex)
            .Should().BeEquivalentTo(Enumerable.Range(0, 200).Select(i => (long)i));
    }
}
