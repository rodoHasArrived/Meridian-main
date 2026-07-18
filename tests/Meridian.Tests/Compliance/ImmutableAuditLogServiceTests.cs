using FluentAssertions;
using Meridian.Audit.Compliance;
using Xunit;

namespace Meridian.Tests.Compliance;

/// <summary>
/// Guards the tamper-evident audit trail behind privileged compliance actions. The
/// failure mode under guard: an audit event whose hash chain no longer proves ordering
/// and content, letting a payment release or override approval be altered or reordered
/// without detection.
/// </summary>
public sealed class ImmutableAuditLogServiceTests
{
    [Fact]
    public void VerifyIntegrity_EmptyLog_ReturnsTrue()
    {
        var audit = new ImmutableAuditLogService();

        audit.VerifyIntegrity().Should().BeTrue();
        audit.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void Append_FirstEvent_HasNoPreviousHash()
    {
        var audit = new ImmutableAuditLogService();

        var evt = audit.Append(CreateActor(), CreateRequest("payment-1"));

        evt.PreviousHash.Should().BeNull();
        evt.Hash.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Append_ChainsEachEventToThePreviousHash()
    {
        var audit = new ImmutableAuditLogService();
        var actor = CreateActor();

        var first = audit.Append(actor, CreateRequest("payment-1"));
        var second = audit.Append(actor, CreateRequest("payment-2"));
        var third = audit.Append(actor, CreateRequest("payment-3"));

        second.PreviousHash.Should().Be(first.Hash,
            "each event must chain to its predecessor so reordering is detectable");
        third.PreviousHash.Should().Be(second.Hash);
        audit.GetAll().Should().HaveCount(3).And.ContainInOrder(first, second, third);
    }

    [Fact]
    public void Append_ProducesIndependentlyVerifiableHash()
    {
        var audit = new ImmutableAuditLogService();

        var evt = audit.Append(CreateActor(), CreateRequest("payment-1"));

        AuditHash.Compute(evt with { Hash = string.Empty }).Should().Be(evt.Hash,
            "an external verifier must be able to recompute the stored hash from the event payload");
    }

    [Fact]
    public void Append_RecordsActorAndRequestProvenance()
    {
        var audit = new ImmutableAuditLogService();
        var actor = CreateActor();
        var request = CreateRequest("payment-1");

        var evt = audit.Append(actor, request);

        evt.ActorId.Should().Be(actor.ActorId);
        evt.SourceIp.Should().Be(actor.SourceIp);
        evt.DeviceId.Should().Be(actor.DeviceId);
        evt.Action.Should().Be(request.Action);
        evt.ObjectId.Should().Be(request.ObjectId);
        evt.CorrelationId.Should().Be(request.CorrelationId);
        evt.BeforeStateJson.Should().Be(request.BeforeStateJson);
        evt.AfterStateJson.Should().Be(request.AfterStateJson);
    }

    [Fact]
    public void AuditHash_TamperedPayload_ProducesDifferentHash()
    {
        var audit = new ImmutableAuditLogService();

        var evt = audit.Append(CreateActor(), CreateRequest("payment-1"));
        var tampered = evt with { AfterStateJson = "{\"status\":\"released\",\"amount\":9999999}" };

        AuditHash.Compute(tampered with { Hash = string.Empty }).Should().NotBe(evt.Hash,
            "any change to the recorded state must invalidate the stored hash");
    }

    [Fact]
    public void VerifyIntegrity_UntamperedChain_ReturnsTrue()
    {
        var audit = new ImmutableAuditLogService();
        var actor = CreateActor();

        audit.Append(actor, CreateRequest("payment-1"));
        audit.Append(actor, CreateRequest("payment-2"));

        audit.VerifyIntegrity().Should().BeTrue();
    }

    [Fact]
    public async Task Append_UnderConcurrentWriters_PreservesIntactHashChain()
    {
        var audit = new ImmutableAuditLogService();
        var actor = CreateActor();
        const int writers = 100;

        // Concurrent appends must be serialized: if two callers read the same predecessor
        // hash and both chain off it, the tamper-evident chain forks and VerifyIntegrity
        // silently fails from that point on.
        await Task.WhenAll(
            Enumerable.Range(0, writers).Select(i =>
                Task.Run(() => audit.Append(actor, CreateRequest($"payment-{i}")))));

        audit.GetAll().Should().HaveCount(writers, "every concurrent append must be recorded once");
        audit.VerifyIntegrity().Should().BeTrue("concurrent appends must not fork the hash chain");
    }

    [Fact]
    public void Append_WithDurablePath_RehydratesChainAfterRestart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mdc_compliance_audit_{Guid.NewGuid():N}.jsonl");
        try
        {
            var actor = CreateActor();

            // First "process": append to the durable log.
            var original = new ImmutableAuditLogService(path);
            var firstHash = original.Append(actor, CreateRequest("payment-1")).Hash;
            var secondHash = original.Append(actor, CreateRequest("payment-2")).Hash;

            // Second "process": a fresh instance over the same path must recover the full history
            // instead of starting empty (the in-memory-only implementation lost everything here).
            var reloaded = new ImmutableAuditLogService(path);

            reloaded.GetAll().Select(static e => e.Hash).Should()
                .ContainInOrder([firstHash, secondHash], "persisted audit events must survive a restart");
            reloaded.VerifyIntegrity().Should().BeTrue("the rehydrated hash chain must still verify");

            // A new append must continue the persisted chain, not fork from an empty log.
            var third = reloaded.Append(actor, CreateRequest("payment-3"));
            third.PreviousHash.Should().Be(secondHash,
                "an append after restart must chain onto the last persisted event");
            reloaded.VerifyIntegrity().Should().BeTrue();
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void VerifyIntegrity_AfterReloadWithCorruptPersistedRecord_ReturnsFalse()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mdc_compliance_audit_{Guid.NewGuid():N}.jsonl");
        try
        {
            var actor = CreateActor();
            var original = new ImmutableAuditLogService(path);
            original.Append(actor, CreateRequest("payment-1"));
            original.Append(actor, CreateRequest("payment-2"));

            // Corrupt the durable log with an unparseable trailing record.
            File.AppendAllText(path, "{ not valid json" + Environment.NewLine);

            // A fresh instance loads the surviving prefix but must not report the shortened,
            // still-internally-consistent chain as valid — the corruption has to surface.
            var reloaded = new ImmutableAuditLogService(path);

            reloaded.VerifyIntegrity().Should().BeFalse(
                "a tamper-evident log must report corruption of a persisted record, not silently drop it");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void VerifyIntegrity_AfterReloadWithNullPersistedRecord_ReturnsFalse()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mdc_compliance_audit_{Guid.NewGuid():N}.jsonl");
        try
        {
            var actor = CreateActor();
            var original = new ImmutableAuditLogService(path);
            original.Append(actor, CreateRequest("payment-1"));

            // A record replaced by the JSON literal `null` deserializes without error but is not a
            // valid event; the reload must still flag the log as corrupt rather than drop it silently.
            File.AppendAllText(path, "null" + Environment.NewLine);

            var reloaded = new ImmutableAuditLogService(path);

            reloaded.VerifyIntegrity().Should().BeFalse(
                "a persisted null record removes a real event and must fail the integrity check");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static ActorContext CreateActor() => new(
        ActorId: "treasury-ops-1",
        Roles: ["TreasuryOperator"],
        Team: "Treasury",
        SourceIp: "10.20.30.40",
        DeviceId: "workstation-7",
        MfaSatisfied: true);

    private static ComplianceActionRequest CreateRequest(string objectId) => new(
        Action: SensitiveAction.PaymentRelease,
        ObjectType: "Payment",
        ObjectId: objectId,
        BeforeStateJson: "{\"status\":\"pending\"}",
        AfterStateJson: "{\"status\":\"released\"}",
        CorrelationId: $"corr-{objectId}",
        EntityId: "fund-1",
        RequestedByActorId: "requester-1",
        AdditionalApproverIds: ["approver-2", "approver-3"]);
}
