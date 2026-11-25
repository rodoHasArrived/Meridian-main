using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Npgsql;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Review A4 / P4: one change record per economic event, written in the event's transaction, claimed
/// with SKIP LOCKED. A change whose transaction commits AFTER a later-sequenced one is still
/// delivered, which a <c>global_sequence &gt; cursor</c> reader would have skipped.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SecurityMasterChangeOutboxPostgresTests : IClassFixture<SecurityMasterDatabaseFixture>
{
    private readonly SecurityMasterDatabaseFixture _fixture;

    public SecurityMasterChangeOutboxPostgresTests(SecurityMasterDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SecurityMasterDatabaseFact]
    public async Task LateCommittingLowerSequence_IsStillDelivered()
    {
        var outbox = new PostgresSecurityMasterChangeOutbox(_fixture.Options);
        var consumer = $"consumer-{Guid.NewGuid():N}";
        var early = Guid.NewGuid();
        var late = Guid.NewGuid();

        // Transaction A takes the LOWER change sequence position first but commits last.
        await using var connectionA = new NpgsqlConnection(_fixture.Options.ConnectionString);
        await connectionA.OpenAsync();
        await using var transactionA = await connectionA.BeginTransactionAsync();
        await PostgresSecurityMasterChangeOutbox.InsertAsync(connectionA, transactionA, _fixture.Options.Schema, Envelope(late, 1), CancellationToken.None);

        await using (var connectionB = new NpgsqlConnection(_fixture.Options.ConnectionString))
        {
            await connectionB.OpenAsync();
            await using var transactionB = await connectionB.BeginTransactionAsync();
            await PostgresSecurityMasterChangeOutbox.InsertAsync(connectionB, transactionB, _fixture.Options.Schema, Envelope(early, 1), CancellationToken.None);
            await transactionB.CommitAsync();
        }

        var firstPass = await ClaimAllAsync(outbox, consumer);
        firstPass.Should().Contain(change => change.SecurityId == early);
        firstPass.Should().NotContain(change => change.SecurityId == late, "an uncommitted change is not yet visible");
        await outbox.MarkProcessedAsync(consumer, firstPass.Select(static change => change.ChangeId).ToArray());

        await transactionA.CommitAsync();

        var secondPass = await ClaimAllAsync(outbox, consumer);
        secondPass.Should().Contain(change => change.SecurityId == late,
            "the late-committing change is delivered once it commits, unlike a sequence cursor that already moved past it");
    }

    [SecurityMasterDatabaseFact]
    public async Task ConcurrentConsumers_NeverClaimTheSameChange()
    {
        var outbox = new PostgresSecurityMasterChangeOutbox(_fixture.Options);
        await using (var connection = new NpgsqlConnection(_fixture.Options.ConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            for (var i = 0; i < 20; i++)
            {
                await PostgresSecurityMasterChangeOutbox.InsertAsync(connection, transaction, _fixture.Options.Schema, Envelope(Guid.NewGuid(), 1), CancellationToken.None);
            }

            await transaction.CommitAsync();
        }

        var claims = await Task.WhenAll(Enumerable.Range(0, 4).Select(index =>
            outbox.ClaimAsync($"parallel-{index}-{Guid.NewGuid():N}", 50, TimeSpan.FromMinutes(5))));

        var claimed = claims.SelectMany(static batch => batch).Select(static change => change.ChangeId).ToArray();
        claimed.Should().OnlyHaveUniqueItems();
    }

    [SecurityMasterDatabaseFact]
    public async Task RestatementDecision_IsPersistedOnTheChangeRecord()
    {
        var outbox = new PostgresSecurityMasterChangeOutbox(_fixture.Options);
        var securityId = Guid.NewGuid();
        await using (var connection = new NpgsqlConnection(_fixture.Options.ConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await PostgresSecurityMasterChangeOutbox.InsertAsync(connection, transaction, _fixture.Options.Schema, Envelope(securityId, 2), CancellationToken.None);
            await transaction.CommitAsync();
        }

        (await outbox.RecordRestatementDecisionAsync(securityId, 2, """{"restatementRequired":true}""")).Should().BeTrue();

        var record = (await outbox.ListAsync(securityId)).Single();
        using var decision = JsonDocument.Parse(record.RestatementDecisionJson!);
        decision.RootElement.GetProperty("restatementRequired").GetBoolean().Should().BeTrue();
    }

    private static async Task<IReadOnlyList<SecurityMasterChangeRecord>> ClaimAllAsync(ISecurityMasterChangeOutbox outbox, string consumer)
        => await outbox.ClaimAsync(consumer, 10_000, TimeSpan.FromMinutes(5));

    private static SecurityMasterEventEnvelope Envelope(Guid securityId, long version)
        => new(
            null,
            securityId,
            version,
            "TermsAmended",
            DateTimeOffset.UtcNow,
            "outbox-test",
            null,
            null,
            JsonSerializer.SerializeToElement(new { }),
            JsonSerializer.SerializeToElement(new { }));
}
