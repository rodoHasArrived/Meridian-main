using FluentAssertions;
using Meridian.Audit.Compliance;
using Meridian.Tests.Infrastructure;

namespace Meridian.Tests.Compliance;

/// <summary>
/// Fail-closed behaviour of the durable compliance approval authority: unauthenticated actors,
/// corrupt or unbound snapshots, expiry, decision immutability, and defensive copies.
/// </summary>
public sealed class FileComplianceApprovalStoreTests : TempDirectoryTestBase
{
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("anonymous")]
    [InlineData("  Anonymous ")]
    [InlineData("ANONYMOUS")]
    public void CreateRequest_AnonymousRequester_IsUnauthorizedAndPersistsNothing(string actorId)
    {
        var path = StorePath();
        var store = new FileComplianceApprovalStore(path, new FixedClock(Now));

        var act = () => store.CreateRequest(Actor(actorId), Command());

        act.Should().Throw<UnauthorizedAccessException>();
        File.Exists(path).Should().BeFalse();
    }

    [Theory]
    [InlineData("", "payment-1", "corr-1")]
    [InlineData("Payment", " ", "corr-1")]
    [InlineData("Payment", "payment-1", "")]
    public void CreateRequest_UnboundObjectOrCorrelation_IsRejected(string objectType, string objectId, string correlationId)
    {
        var store = new FileComplianceApprovalStore(StorePath(), new FixedClock(Now));

        var act = () => store.CreateRequest(
            Actor("requester-1"),
            new ComplianceApprovalRequestCommand(SensitiveAction.PaymentRelease, objectType, objectId, correlationId));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CreateRequest_TrimsBindingFieldsAndAppliesConfiguredLifetime()
    {
        var store = new FileComplianceApprovalStore(StorePath(), new FixedClock(Now), TimeSpan.FromHours(2));

        var record = store.CreateRequest(
            Actor("  requester-1  "),
            new ComplianceApprovalRequestCommand(
                SensitiveAction.PaymentRelease,
                ObjectType: " Payment ",
                ObjectId: " payment-1 ",
                CorrelationId: " corr-1 ",
                EntityId: "   "));

        record.ApprovalRequestId.Should().StartWith("compliance-approval-");
        record.RequestedByActorId.Should().Be("requester-1");
        record.ObjectType.Should().Be("Payment");
        record.ObjectId.Should().Be("payment-1");
        record.CorrelationId.Should().Be("corr-1");
        record.EntityId.Should().BeNull("a blank entity id must not become a binding value");
        record.RequestedAtUtc.Should().Be(Now);
        record.ExpiresAtUtc.Should().Be(Now.AddHours(2));
        record.Decisions.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_NonPositiveLifetime_Throws(int seconds)
    {
        var act = () => new FileComplianceApprovalStore(StorePath(), new FixedClock(Now), TimeSpan.FromSeconds(seconds));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("null")]
    [InlineData("{}")]
    public void Constructor_CorruptOrUnboundSnapshot_BlocksStartupInsteadOfStartingEmpty(string json)
    {
        var path = StorePath();
        File.WriteAllText(path, json);

        var act = () => new FileComplianceApprovalStore(path, new FixedClock(Now));

        act.Should().Throw<InvalidDataException>();
        File.ReadAllText(path).Should().Be(json, "a corrupt snapshot must be preserved for investigation");
    }

    [Theory]
    [InlineData("approvalRequestId")]
    [InlineData("requestedByActorId")]
    [InlineData("objectType")]
    [InlineData("objectId")]
    public void Constructor_SnapshotRecordWithBlankBindingField_FailsClosed(string field)
    {
        var path = StorePath();
        var store = new FileComplianceApprovalStore(path, new FixedClock(Now));
        store.CreateRequest(Actor("requester-1"), Command());
        var json = File.ReadAllText(path);
        var tampered = System.Text.RegularExpressions.Regex.Replace(
            json,
            $"\"{field}\":\\s*\"[^\"]*\"",
            $"\"{field}\": \"  \"");
        tampered.Should().NotBe(json, "the persisted snapshot must contain the camel-cased field '{0}'", field);
        File.WriteAllText(path, tampered);

        var act = () => new FileComplianceApprovalStore(path, new FixedClock(Now));

        act.Should().Throw<InvalidDataException>().WithMessage("*unbound record*");
    }

    [Fact]
    public void Constructor_DuplicateRequestIdsInSnapshot_FailsClosed()
    {
        var path = StorePath();
        var store = new FileComplianceApprovalStore(path, new FixedClock(Now));
        var request = store.CreateRequest(Actor("requester-1"), Command());
        var json = File.ReadAllText(path);
        var start = json.IndexOf('[') + 1;
        var end = json.LastIndexOf(']');
        var element = json[start..end];
        File.WriteAllText(path, json[..start] + element + "," + element + json[end..]);

        var act = () => new FileComplianceApprovalStore(path, new FixedClock(Now));

        act.Should().Throw<InvalidDataException>().WithMessage("*corrupt*");
        request.ApprovalRequestId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void RecordDecision_UnknownRequest_ThrowsKeyNotFound()
    {
        var store = new FileComplianceApprovalStore(StorePath(), new FixedClock(Now));

        var act = () => store.RecordDecision("compliance-approval-missing", Actor("approver-2"), approved: true);

        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void RecordDecision_AnonymousApprover_IsUnauthorizedAndRecordsNothing()
    {
        var store = new FileComplianceApprovalStore(StorePath(), new FixedClock(Now));
        var request = store.CreateRequest(Actor("requester-1"), Command());

        var act = () => store.RecordDecision(request.ApprovalRequestId, Actor("anonymous"), approved: true);

        act.Should().Throw<UnauthorizedAccessException>();
        store.Resolve(request.ApprovalRequestId)!.Decisions.Should().BeEmpty();
    }

    [Fact]
    public void RecordDecision_AtOrAfterExpiry_IsRejected()
    {
        var clock = new FixedClock(Now);
        var store = new FileComplianceApprovalStore(StorePath(), clock, TimeSpan.FromMinutes(30));
        var request = store.CreateRequest(Actor("requester-1"), Command());
        clock.Set(request.ExpiresAtUtc);

        var act = () => store.RecordDecision(request.ApprovalRequestId, Actor("approver-2"), approved: true);

        act.Should().Throw<InvalidOperationException>().WithMessage("*expired*");
        store.Resolve(request.ApprovalRequestId)!.Decisions.Should().BeEmpty();
    }

    [Fact]
    public void RecordDecision_RequesterInDifferentCase_CannotSelfApprove()
    {
        var store = new FileComplianceApprovalStore(StorePath(), new FixedClock(Now));
        var request = store.CreateRequest(Actor("requester-1"), Command());

        var act = () => store.RecordDecision(request.ApprovalRequestId, Actor("REQUESTER-1"), approved: true);

        act.Should().Throw<InvalidOperationException>().WithMessage("*cannot approve their own*");
    }

    [Theory]
    [InlineData(" requester-1 ")]
    [InlineData("requester-1 ")]
    [InlineData("\trequester-1")]
    public void RecordDecision_RequesterPaddedWithWhitespace_CannotSelfApprove(string paddedRequester)
    {
        var store = new FileComplianceApprovalStore(StorePath(), new FixedClock(Now));
        var request = store.CreateRequest(Actor("requester-1"), Command());

        var act = () => store.RecordDecision(request.ApprovalRequestId, Actor(paddedRequester), approved: true);

        act.Should().Throw<InvalidOperationException>().WithMessage("*cannot approve their own*");
        store.Resolve(request.ApprovalRequestId)!.Decisions.Should().BeEmpty();
    }

    [Fact]
    public void RecordDecision_PaddedApproverFlippingVerdict_IsRejected()
    {
        var store = new FileComplianceApprovalStore(StorePath(), new FixedClock(Now));
        var request = store.CreateRequest(Actor("requester-1"), Command());
        store.RecordDecision(request.ApprovalRequestId, Actor("approver-2"), approved: true);

        var act = () => store.RecordDecision(request.ApprovalRequestId, Actor("approver-2 "), approved: false);

        act.Should().Throw<InvalidOperationException>().WithMessage("*cannot replace*");
        store.Resolve(request.ApprovalRequestId)!.Decisions.Should().ContainSingle()
            .Which.Approved.Should().BeTrue();
    }

    [Fact]
    public void RecordDecision_SameActorSameVerdictTwice_IsIdempotent()
    {
        var store = new FileComplianceApprovalStore(StorePath(), new FixedClock(Now));
        var request = store.CreateRequest(Actor("requester-1"), Command());
        var first = store.RecordDecision(request.ApprovalRequestId, Actor("approver-2"), approved: true);

        var second = store.RecordDecision(request.ApprovalRequestId, Actor("Approver-2"), approved: true);

        second.Decisions.Should().ContainSingle();
        second.Decisions[0].ApprovalId.Should().Be(first.Decisions[0].ApprovalId);
    }

    [Fact]
    public void RecordDecision_SameActorFlipsVerdict_IsRejectedAndOriginalDecisionSurvivesRestart()
    {
        var path = StorePath();
        var store = new FileComplianceApprovalStore(path, new FixedClock(Now));
        var request = store.CreateRequest(Actor("requester-1"), Command());
        store.RecordDecision(request.ApprovalRequestId, Actor("approver-2"), approved: true);

        var act = () => store.RecordDecision(request.ApprovalRequestId, Actor("approver-2"), approved: false);

        act.Should().Throw<InvalidOperationException>().WithMessage("*cannot replace*");
        var restarted = new FileComplianceApprovalStore(path, new FixedClock(Now));
        restarted.Resolve(request.ApprovalRequestId)!.Decisions.Should().ContainSingle()
            .Which.Approved.Should().BeTrue();
    }

    [Fact]
    public void RecordDecision_SameInstantDecisions_KeepAppendOrderAcrossRestart()
    {
        var path = StorePath();
        var store = new FileComplianceApprovalStore(path, new FixedClock(Now));
        var request = store.CreateRequest(Actor("requester-1"), Command());
        foreach (var approver in new[] { "approver-z", "approver-a", "approver-m" })
            store.RecordDecision(request.ApprovalRequestId, Actor(approver), approved: true);

        var restarted = new FileComplianceApprovalStore(path, new FixedClock(Now));

        restarted.Resolve(request.ApprovalRequestId)!.Decisions.Select(d => d.ApprovedByActorId)
            .Should().Equal("approver-z", "approver-a", "approver-m");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_BlankId_ReturnsNull(string? id)
    {
        var store = new FileComplianceApprovalStore(StorePath(), new FixedClock(Now));

        store.Resolve(id!).Should().BeNull();
    }

    [Fact]
    public void Resolve_TrimsLookupKeyAndReturnsDefensiveCopy()
    {
        var store = new FileComplianceApprovalStore(StorePath(), new FixedClock(Now));
        var request = store.CreateRequest(Actor("requester-1"), Command());
        store.RecordDecision(request.ApprovalRequestId, Actor("approver-2"), approved: true);

        var resolved = store.Resolve($"  {request.ApprovalRequestId}  ");
        var decisions = (ComplianceApprovalDecisionRecord[])resolved!.Decisions;
        decisions[0] = decisions[0] with { ApprovedByActorId = "tampered", Approved = false };

        store.Resolve(request.ApprovalRequestId)!.Decisions.Should().ContainSingle()
            .Which.ApprovedByActorId.Should().Be("approver-2");
    }

    private string StorePath() => Path.Combine(TestDataRoot, $"approvals-{Guid.NewGuid():N}.json");

    private static ComplianceApprovalRequestCommand Command()
        => new(SensitiveAction.PaymentRelease, "Payment", "payment-1", "corr-1", "fund-1");

    private static ActorContext Actor(string actorId)
        => new(actorId, ["Admin"], "Compliance", "127.0.0.1", "test-device", MfaSatisfied: true);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Set(DateTimeOffset value) => _now = value;
    }
}
