using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Execution.Sdk;
using Meridian.Execution.Serialization;
using Meridian.Execution.Services;
using Xunit;

namespace Meridian.Tests.Execution;

public sealed class BrokerExecutionFillIdentityTests
{
    private static readonly DateTimeOffset ExecutedAt = new(2026, 11, 25, 15, 15, 30, TimeSpan.Zero);

    [Fact]
    public void FillWithoutExecutionId_KeepsTheRetainedContentIdentity()
    {
        var fill = Fill();

        // Re-derived independently: any drift here would orphan every retained fill record.
        PaperSessionFillRecord.ComputeCanonicalFillId(fill).Should().Be(LegacyFillId(fill));
        PaperSessionFillRecord.ComputeCanonicalFillId(fill with { ExecutionId = "  " })
            .Should().Be(LegacyFillId(fill), "a blank execution id is not a broker identity");
    }

    [Fact]
    public void FillWithoutExecutionId_SerializesExactlyAsBefore()
    {
        var json = JsonSerializer.Serialize(Fill(), ExecutionJsonContext.Default.ExecutionReport);

        json.Should().NotContain(nameof(ExecutionReport.ExecutionId),
            "omitting the null field keeps retained content hashes stable");
    }

    [Fact]
    public void SameBrokerExecution_ResolvesToOneFill_WhateverMeridianCalledTheOrder()
    {
        var beforeRestart = Fill() with { ExecutionId = "0000e0d5.6576f4a2.01.01" };
        var afterRestart = beforeRestart with
        {
            OrderId = "11001",
            ClientOrderId = null,
            Timestamp = ExecutedAt.AddSeconds(3),
            FillPrice = 100.25m
        };

        PaperSessionFillRecord.ComputeCanonicalFillId(afterRestart)
            .Should().Be(PaperSessionFillRecord.ComputeCanonicalFillId(beforeRestart));
        PaperSessionFillRecord.ComputeCanonicalHash(afterRestart)
            .Should().NotBe(PaperSessionFillRecord.ComputeCanonicalHash(beforeRestart),
                "different content under the same identity fails closed instead of posting twice");
    }

    [Fact]
    public void DistinctBrokerExecutions_WithIdenticalEconomics_StayDistinctFills()
    {
        var first = Fill() with { ExecutionId = "0000e0d5.6576f4a2.01.01" };
        var second = Fill() with { ExecutionId = "0000e0d5.6576f4a3.01.01" };

        var firstId = PaperSessionFillRecord.ComputeCanonicalFillId(first);
        var secondId = PaperSessionFillRecord.ComputeCanonicalFillId(second);

        firstId.Should().NotBe(secondId);
        firstId.Should().NotBe(LegacyFillId(Fill()));
    }

    [Fact]
    public void FillRecordWithExecutionId_RoundTripsAndValidates()
    {
        var fill = Fill() with { ExecutionId = "0000e0d5.6576f4a2.01.01" };
        var record = PaperSessionFillRecord.CreateCanonical(fill, ExecutedAt);

        var json = JsonSerializer.Serialize(record.Fill, ExecutionJsonContext.Default.ExecutionReport);
        var replayed = JsonSerializer.Deserialize(json, ExecutionJsonContext.Default.ExecutionReport)!;

        replayed.ExecutionId.Should().Be(fill.ExecutionId);
        record.Validate().FillId.Should().Be(PaperSessionFillRecord.ComputeCanonicalFillId(replayed));
    }

    private static ExecutionReport Fill() => new()
    {
        OrderId = "order-1",
        ClientOrderId = "client-1",
        GatewayOrderId = "11001",
        ReportType = ExecutionReportType.PartialFill,
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        OrderStatus = OrderStatus.PartiallyFilled,
        OrderQuantity = 10m,
        FilledQuantity = 2m,
        FillPrice = 100m,
        Timestamp = ExecutedAt
    };

    // The original content-derived identity, written out independently of production code.
    private static Guid LegacyFillId(ExecutionReport fill)
    {
        static string Encode(string? value) => value is null ? "-" : Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        static string Number(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);
        var identity = string.Join("|",
            Encode(fill.OrderId), Encode(fill.ClientOrderId), Encode(fill.GatewayOrderId), Encode(fill.Symbol),
            ((int)fill.Side).ToString(CultureInfo.InvariantCulture), Number(fill.FilledQuantity),
            fill.FillPrice is { } price ? Number(price) : "-", Number(fill.Commission ?? 0m),
            fill.Timestamp.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture));
        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(identity)).AsSpan(0, 16));
    }
}
