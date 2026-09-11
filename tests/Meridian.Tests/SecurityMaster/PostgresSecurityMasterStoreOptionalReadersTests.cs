using System.Text.Json;
using FluentAssertions;
using Meridian.Storage.SecurityMaster;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The hand-written projection path's numeric JSON readers must check the value kind before
/// reading, as their string/bool/object/date siblings and the registry path's <c>DecodeTerm</c>
/// already do. <c>JsonElement.TryGetDecimal</c> / <c>TryGetInt32</c> throw on a non-number
/// element — including the JSON <c>null</c> the canonical F# serializer writes for every optional
/// common term the record does not carry — and these readers feed the core <c>securities</c>
/// upsert (<c>lot_size</c>, <c>tick_size</c>) and every per-class projection writer, so a throw
/// here aborts the projection transaction for an ordinary record.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PostgresSecurityMasterStoreOptionalReadersTests
{
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    [Theory]
    [InlineData("""{"lotSize":null}""")]
    [InlineData("""{"lotSize":"100"}""")]
    [InlineData("""{"lotSize":true}""")]
    [InlineData("""{"lotSize":{}}""")]
    [InlineData("""{"lotSize":[100]}""")]
    [InlineData("""{}""")]
    public void GetOptionalDecimal_NonNumberOrAbsent_ReadsAsAbsentInsteadOfThrowing(string json)
        => PostgresSecurityMasterStore.GetOptionalDecimal(Json(json), "lotSize").Should().BeNull();

    [Fact]
    public void GetOptionalDecimal_Number_ReadsTheValue()
        => PostgresSecurityMasterStore.GetOptionalDecimal(Json("""{"tickSize":0.0625}"""), "tickSize").Should().Be(0.0625m);

    [Theory]
    [InlineData("""{"settlementCycleDays":null}""")]
    [InlineData("""{"settlementCycleDays":"2"}""")]
    [InlineData("""{"settlementCycleDays":false}""")]
    [InlineData("""{"settlementCycleDays":{}}""")]
    [InlineData("""{}""")]
    public void GetOptionalInt_NonNumberOrAbsent_ReadsAsAbsentInsteadOfThrowing(string json)
        => PostgresSecurityMasterStore.GetOptionalInt(Json(json), "settlementCycleDays").Should().BeNull();

    [Fact]
    public void GetOptionalInt_Number_ReadsTheValue()
        => PostgresSecurityMasterStore.GetOptionalInt(Json("""{"settlementCycleDays":2}"""), "settlementCycleDays").Should().Be(2);

    [Fact]
    public void GetOptionalInt_NonIntegralNumber_ReadsAsAbsent()
        => PostgresSecurityMasterStore.GetOptionalInt(Json("""{"settlementCycleDays":2.5}"""), "settlementCycleDays").Should().BeNull();
}
