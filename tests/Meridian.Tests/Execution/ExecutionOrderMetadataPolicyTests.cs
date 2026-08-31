using FluentAssertions;
using Meridian.Execution.Sdk;
using Xunit;

namespace Meridian.Tests.Execution;

/// <summary>
/// Guards the server-owned order-metadata boundary. The failure mode under guard: a
/// client smuggles broker-account, asset-class, or execution-control override keys into
/// order metadata and steers routing decisions that only the server may make.
/// </summary>
public sealed class ExecutionOrderMetadataPolicyTests
{
    public static TheoryData<string> ClientRejectedKeys => new(
        "broker_account_id",
        "brokerAccountId",
        "account_id",
        "accountId",
        "alpaca:broker_account_id",
        "asset_class",
        "assetClass",
        "alpaca:asset_class",
        "manualOverrideId",
        "manual_override_id",
        "executionControlOverrideId",
        "execution_control_override_id",
        "liveReadinessEvidenceReference",
        "live_readiness_evidence_reference",
        "livePromotionAuditReference",
        "live_promotion_audit_reference",
        "riskSubmitter");

    [Fact]
    public void ContainsClientRejectedServerOwnedKey_NullMetadata_ReturnsFalse()
    {
        ExecutionOrderMetadataPolicy.ContainsClientRejectedServerOwnedKey(null).Should().BeFalse();
    }

    [Fact]
    public void ContainsClientRejectedServerOwnedKey_BenignMetadata_ReturnsFalse()
    {
        var metadata = new Dictionary<string, string>
        {
            ["strategy_run_id"] = "run-2026-07-06-01",
            ["desk"] = "options-income"
        };

        ExecutionOrderMetadataPolicy.ContainsClientRejectedServerOwnedKey(metadata).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(ClientRejectedKeys))]
    public void ContainsClientRejectedServerOwnedKey_EachServerOwnedKey_IsDetected(string rejectedKey)
    {
        var metadata = new Dictionary<string, string>
        {
            ["strategy_run_id"] = "run-2026-07-06-01",
            [rejectedKey] = "client-supplied-value"
        };

        ExecutionOrderMetadataPolicy.ContainsClientRejectedServerOwnedKey(metadata)
            .Should().BeTrue($"'{rejectedKey}' is server-owned and must be rejected when a client supplies it");
    }

    [Theory]
    [MemberData(nameof(ClientRejectedKeys))]
    public void ContainsClientRejectedServerOwnedKey_DetectsKeysRegardlessOfCasingAndComparer(string rejectedKey)
    {
        // JSON-deserialized request bags use a case-sensitive comparer, while downstream merges
        // read the bag ordinal-ignore-case — a differently-cased key must still be rejected here.
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["strategy_run_id"] = "run-2026-07-06-01",
            [rejectedKey.ToUpperInvariant()] = "client-supplied-value"
        };

        ExecutionOrderMetadataPolicy.ContainsClientRejectedServerOwnedKey(metadata)
            .Should().BeTrue($"'{rejectedKey}' is server-owned and must be rejected regardless of the casing a client uses");
    }

    [Fact]
    public void RemoveClientRejectedServerOwnedKeys_NullMetadata_ReturnsNull()
    {
        ExecutionOrderMetadataPolicy.RemoveClientRejectedServerOwnedKeys(null).Should().BeNull();
    }

    [Fact]
    public void RemoveClientRejectedServerOwnedKeys_NoServerOwnedKeys_ReturnsSameInstance()
    {
        var metadata = new Dictionary<string, string> { ["desk"] = "options-income" };

        var sanitized = ExecutionOrderMetadataPolicy.RemoveClientRejectedServerOwnedKeys(metadata);

        sanitized.Should().BeSameAs(metadata, "clean metadata must not be copied");
    }

    [Fact]
    public void RemoveClientRejectedServerOwnedKeys_StripsServerOwnedKeysAndKeepsTheRest()
    {
        var metadata = new Dictionary<string, string>
        {
            ["broker_account_id"] = "acct-hijack",
            ["assetClass"] = "us_option",
            ["strategy_run_id"] = "run-2026-07-06-01"
        };

        var sanitized = ExecutionOrderMetadataPolicy.RemoveClientRejectedServerOwnedKeys(metadata);

        sanitized.Should().NotBeSameAs(metadata);
        sanitized.Should().ContainKey("strategy_run_id");
        sanitized.Should().NotContainKey("broker_account_id");
        sanitized.Should().NotContainKey("assetClass");
        metadata.Should().ContainKey("broker_account_id", "the caller's dictionary must not be mutated");
    }

    [Fact]
    public void RemoveClientRejectedServerOwnedKeys_RemovesKeysCaseInsensitively()
    {
        // The input is deliberately case-sensitive: removal must be case-insensitive by
        // the policy's own doing, not because of the caller's dictionary comparer.
        var metadata = new Dictionary<string, string>
        {
            ["BROKER_ACCOUNT_ID"] = "acct-hijack"
        };

        var sanitized = ExecutionOrderMetadataPolicy.RemoveClientRejectedServerOwnedKeys(metadata);

        sanitized.Should().BeEmpty("removal compares keys case-insensitively");
    }

    [Fact]
    public void RemoveServerOwnedRoutingKeys_NullRequest_Throws()
    {
        var act = () => ExecutionOrderMetadataPolicy.RemoveServerOwnedRoutingKeys(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RemoveServerOwnedRoutingKeys_NoMetadata_ReturnsSameRequest()
    {
        var request = CreateOrderRequest(metadata: null);

        var sanitized = ExecutionOrderMetadataPolicy.RemoveServerOwnedRoutingKeys(request);

        sanitized.Should().BeSameAs(request);
    }

    [Fact]
    public void RemoveServerOwnedRoutingKeys_StripsRoutingAndOverrideKeys()
    {
        var request = CreateOrderRequest(new Dictionary<string, string>
        {
            ["broker_account_id"] = "acct-hijack",
            ["executionControlOverrideId"] = "override-42",
            ["live_promotion_audit_reference"] = "audit-99",
            ["strategy_run_id"] = "run-2026-07-06-01"
        });

        var sanitized = ExecutionOrderMetadataPolicy.RemoveServerOwnedRoutingKeys(request);

        sanitized.Should().NotBeSameAs(request);
        sanitized.Metadata.Should().ContainKey("strategy_run_id");
        sanitized.Metadata.Should().NotContainKeys(
            "broker_account_id", "executionControlOverrideId", "live_promotion_audit_reference");
        sanitized.Symbol.Should().Be(request.Symbol, "sanitizing metadata must not alter order economics");
        sanitized.Quantity.Should().Be(request.Quantity);
    }

    [Fact]
    public void RemoveServerOwnedRoutingKeys_PreservesAssetClassKeys()
    {
        // Asset-class keys are rejected at the client boundary but tolerated on the
        // server-internal sanitization path; this pins that deliberate difference.
        var request = CreateOrderRequest(new Dictionary<string, string>
        {
            ["asset_class"] = "us_option"
        });

        var sanitized = ExecutionOrderMetadataPolicy.RemoveServerOwnedRoutingKeys(request);

        sanitized.Should().BeSameAs(request);
        sanitized.Metadata.Should().ContainKey("asset_class");
    }

    private static OrderRequest CreateOrderRequest(IReadOnlyDictionary<string, string>? metadata) => new()
    {
        Symbol = "SPY",
        Side = OrderSide.Buy,
        Type = OrderType.Limit,
        Quantity = 100m,
        LimitPrice = 512.35m,
        Metadata = metadata
    };
}
