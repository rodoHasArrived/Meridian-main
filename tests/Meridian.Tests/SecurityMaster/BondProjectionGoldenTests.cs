using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Npgsql;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Golden rows for the four Bond projection tables (<c>bond_projection</c> and its lifecycle,
/// accrual-convention and issuer satellites).
/// <para>
/// The expectations are what the hand-written <c>UpsertBondProjectionTablesAsync</c> writer
/// persisted before Bond moved onto a <see cref="SecurityTermsProjectionRegistry"/> descriptor with
/// satellite tables: the maturity gate, the flat coupon and its legacy nested fallback, the derived
/// lifecycle state, common-terms accrual columns, the issuer row that exists only while the bond
/// names an issuer, wrong JSON kinds, and all four tables cleared on a gate miss or class change.
/// Any change to the registry path that would alter one of these rows fails here.
/// </para>
/// </summary>
[Trait("Category", "Integration")]
public sealed class BondProjectionGoldenTests : IClassFixture<SecurityMasterDatabaseFixture>
{
    private const string DisplayName = "Golden bond fixture";
    private const string Currency = "USD";
    private const long Version = 11;

    /// <summary>Replaced in expectations by the record's primary identifier.</summary>
    private const string Primary = "{PRIMARY}";

    private readonly SecurityMasterDatabaseFixture _fixture;

    public BondProjectionGoldenTests(SecurityMasterDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    /// <param name="Terms">Raw asset-specific-terms JSON, so explicit nulls and wrong kinds survive.</param>
    /// <param name="Parent">Expected <c>bond_projection</c> row (class columns and version), or null for no row.</param>
    /// <param name="Lifecycle">Expected <c>bond_lifecycle_projection</c> row, or null.</param>
    /// <param name="Accrual">Expected <c>bond_accrual_convention_projection</c> row, or null.</param>
    /// <param name="Issuer">Expected <c>bond_issuer_projection</c> row, or null.</param>
    /// <param name="Prior">A record written first through the same path, to prove stale rows are cleared.</param>
    /// <param name="CommonTerms">Raw common-terms JSON; defaults to display name and currency only.</param>
    /// <param name="Retired">Whether the record carries an effective-to date.</param>
    public sealed record GoldenCase(
        string Name,
        string AssetClass,
        string Terms,
        string? Parent,
        string? Lifecycle,
        string? Accrual,
        string? Issuer,
        GoldenCase? Prior = null,
        string? CommonTerms = null,
        bool Retired = false);

    private static readonly GoldenCase Full = new(
        "full fixed coupon, callable past its call date, issuer and accrual conventions",
        "Bond",
        """{"maturity":"2091-06-15","issueDate":"2021-06-15","couponType":"Fixed","couponRate":5.25,"dayCount":"30/360","isCallable":true,"callDate":"2026-06-15","subclass":"Corporate","seniority":"Senior","issuerName":"Acme Corp","par":1000,"paymentFrequency":"SemiAnnual","legalFinalMaturity":"2091-12-15","preRefundDate":null,"mandatoryPutDate":"2029-06-15"}""",
        Parent: """{"issuer_name":"Acme Corp","seniority":"Senior","subclass":"Corporate","issue_date":"2021-06-15","maturity_date":"2091-06-15","coupon_kind":"Fixed","fixed_coupon_rate":5.25,"floating_rate_index":null,"floating_spread_bps":null,"version":11}""",
        Lifecycle: """{"lifecycle_stat":"Callable","issue_date":"2021-06-15","call_date":"2026-06-15","maturity_date":"2091-06-15","is_callable":true,"version":11,"subclass":"Corporate","par":1000,"payment_frequency":"SemiAnnual","legal_final_maturity":"2091-12-15","pre_refund_date":null,"mandatory_put_date":"2029-06-15"}""",
        Accrual: """{"day_count_convention":"30/360","settlement_cycle_days":2,"holiday_calendar_id":"US","coupon_kind":"Fixed","fixed_coupon_rate":5.25,"floating_rate_index":null,"floating_spread_bps":null,"version":11}""",
        Issuer: $$"""{"issuer_name":"Acme Corp","display_name":"{{DisplayName}}","primary_identifier_value":"{{Primary}}","currency":"{{Currency}}","maturity_date":"2091-06-15","seniority":"Senior","version":11}""",
        CommonTerms: $$"""{"displayName":"{{DisplayName}}","currency":"{{Currency}}","settlementCycleDays":2,"holidayCalendarId":"US"}""");

    public static IReadOnlyList<GoldenCase> Cases { get; } =
    [
        Full,
        new("legacy nested floating coupon, no issuer", "Bond",
            """{"maturity":"2092-06-30","coupon":{"kind":"Floating","index":"SOFR","spreadBps":125,"dayCountConvention":"ACT/360"},"isCallable":false,"subclass":"FloatingRate"}""",
            Parent: """{"issuer_name":null,"seniority":null,"subclass":"FloatingRate","issue_date":null,"maturity_date":"2092-06-30","coupon_kind":"Floating","fixed_coupon_rate":null,"floating_rate_index":"SOFR","floating_spread_bps":125,"version":11}""",
            Lifecycle: """{"lifecycle_stat":"Active","issue_date":null,"call_date":null,"maturity_date":"2092-06-30","is_callable":false,"version":11,"subclass":"FloatingRate","par":null,"payment_frequency":null,"legal_final_maturity":null,"pre_refund_date":null,"mandatory_put_date":null}""",
            Accrual: """{"day_count_convention":"ACT/360","settlement_cycle_days":null,"holiday_calendar_id":null,"coupon_kind":"Floating","fixed_coupon_rate":null,"floating_rate_index":"SOFR","floating_spread_bps":125,"version":11}""",
            Issuer: null),
        new("flat coupon wins over the nested fallback field by field", "Bond",
            """{"maturity":"2092-06-30","couponType":"Fixed","couponRate":4,"coupon":{"kind":"Floating","index":"SOFR","dayCountConvention":"ACT/365"},"isCallable":false}""",
            Parent: """{"issuer_name":null,"seniority":null,"subclass":null,"issue_date":null,"maturity_date":"2092-06-30","coupon_kind":"Fixed","fixed_coupon_rate":4,"floating_rate_index":"SOFR","floating_spread_bps":null,"version":11}""",
            Lifecycle: """{"lifecycle_stat":"Active","issue_date":null,"call_date":null,"maturity_date":"2092-06-30","is_callable":false,"version":11,"subclass":null,"par":null,"payment_frequency":null,"legal_final_maturity":null,"pre_refund_date":null,"mandatory_put_date":null}""",
            Accrual: """{"day_count_convention":"ACT/365","settlement_cycle_days":null,"holiday_calendar_id":null,"coupon_kind":"Fixed","fixed_coupon_rate":4,"floating_rate_index":"SOFR","floating_spread_bps":null,"version":11}""",
            Issuer: null),
        new("issuer read from the common terms", "Bond",
            """{"maturity":"2092-06-30","isCallable":false}""",
            Parent: """{"issuer_name":"Common Issuer","seniority":null,"subclass":null,"issue_date":null,"maturity_date":"2092-06-30","coupon_kind":null,"fixed_coupon_rate":null,"floating_rate_index":null,"floating_spread_bps":null,"version":11}""",
            Lifecycle: """{"lifecycle_stat":"Active","issue_date":null,"call_date":null,"maturity_date":"2092-06-30","is_callable":false,"version":11,"subclass":null,"par":null,"payment_frequency":null,"legal_final_maturity":null,"pre_refund_date":null,"mandatory_put_date":null}""",
            Accrual: """{"day_count_convention":null,"settlement_cycle_days":null,"holiday_calendar_id":null,"coupon_kind":null,"fixed_coupon_rate":null,"floating_rate_index":null,"floating_spread_bps":null,"version":11}""",
            Issuer: $$"""{"issuer_name":"Common Issuer","display_name":"{{DisplayName}}","primary_identifier_value":"{{Primary}}","currency":"{{Currency}}","maturity_date":"2092-06-30","seniority":null,"version":11}""",
            CommonTerms: $$"""{"displayName":"{{DisplayName}}","currency":"{{Currency}}","issuerName":"Common Issuer"}"""),
        new("matured", "Bond",
            """{"maturity":"2001-01-19","isCallable":true,"callDate":"2000-01-19"}""",
            Parent: """{"issuer_name":null,"seniority":null,"subclass":null,"issue_date":null,"maturity_date":"2001-01-19","coupon_kind":null,"fixed_coupon_rate":null,"floating_rate_index":null,"floating_spread_bps":null,"version":11}""",
            Lifecycle: """{"lifecycle_stat":"Matured","issue_date":null,"call_date":"2000-01-19","maturity_date":"2001-01-19","is_callable":true,"version":11,"subclass":null,"par":null,"payment_frequency":null,"legal_final_maturity":null,"pre_refund_date":null,"mandatory_put_date":null}""",
            Accrual: """{"day_count_convention":null,"settlement_cycle_days":null,"holiday_calendar_id":null,"coupon_kind":null,"fixed_coupon_rate":null,"floating_rate_index":null,"floating_spread_bps":null,"version":11}""",
            Issuer: null),
        new("when-issued", "Bond",
            """{"maturity":"2109-01-01","issueDate":"2099-01-01","isCallable":false}""",
            Parent: """{"issuer_name":null,"seniority":null,"subclass":null,"issue_date":"2099-01-01","maturity_date":"2109-01-01","coupon_kind":null,"fixed_coupon_rate":null,"floating_rate_index":null,"floating_spread_bps":null,"version":11}""",
            Lifecycle: """{"lifecycle_stat":"WhenIssued","issue_date":"2099-01-01","call_date":null,"maturity_date":"2109-01-01","is_callable":false,"version":11,"subclass":null,"par":null,"payment_frequency":null,"legal_final_maturity":null,"pre_refund_date":null,"mandatory_put_date":null}""",
            Accrual: """{"day_count_convention":null,"settlement_cycle_days":null,"holiday_calendar_id":null,"coupon_kind":null,"fixed_coupon_rate":null,"floating_rate_index":null,"floating_spread_bps":null,"version":11}""",
            Issuer: null),
        new("retired outranks every other state", "Bond",
            """{"maturity":"2001-01-19","isCallable":false}""",
            Parent: """{"issuer_name":null,"seniority":null,"subclass":null,"issue_date":null,"maturity_date":"2001-01-19","coupon_kind":null,"fixed_coupon_rate":null,"floating_rate_index":null,"floating_spread_bps":null,"version":11}""",
            Lifecycle: """{"lifecycle_stat":"Retired","issue_date":null,"call_date":null,"maturity_date":"2001-01-19","is_callable":false,"version":11,"subclass":null,"par":null,"payment_frequency":null,"legal_final_maturity":null,"pre_refund_date":null,"mandatory_put_date":null}""",
            Accrual: """{"day_count_convention":null,"settlement_cycle_days":null,"holiday_calendar_id":null,"coupon_kind":null,"fixed_coupon_rate":null,"floating_rate_index":null,"floating_spread_bps":null,"version":11}""",
            Issuer: null,
            Retired: true),
        new("wrong JSON kinds read as absent", "Bond",
            """{"maturity":"2092-06-30","couponType":7,"couponRate":"5","isCallable":"true","par":"1000","issueDate":7,"issuerName":["Acme"]}""",
            Parent: """{"issuer_name":null,"seniority":null,"subclass":null,"issue_date":null,"maturity_date":"2092-06-30","coupon_kind":null,"fixed_coupon_rate":null,"floating_rate_index":null,"floating_spread_bps":null,"version":11}""",
            Lifecycle: """{"lifecycle_stat":"Active","issue_date":null,"call_date":null,"maturity_date":"2092-06-30","is_callable":false,"version":11,"subclass":null,"par":null,"payment_frequency":null,"legal_final_maturity":null,"pre_refund_date":null,"mandatory_put_date":null}""",
            Accrual: """{"day_count_convention":null,"settlement_cycle_days":null,"holiday_calendar_id":null,"coupon_kind":null,"fixed_coupon_rate":null,"floating_rate_index":null,"floating_spread_bps":null,"version":11}""",
            Issuer: null),
        new("dropping the issuer clears the issuer row and keeps the rest", "Bond",
            """{"maturity":"2091-06-15","isCallable":false}""",
            Parent: """{"issuer_name":null,"seniority":null,"subclass":null,"issue_date":null,"maturity_date":"2091-06-15","coupon_kind":null,"fixed_coupon_rate":null,"floating_rate_index":null,"floating_spread_bps":null,"version":11}""",
            Lifecycle: """{"lifecycle_stat":"Active","issue_date":null,"call_date":null,"maturity_date":"2091-06-15","is_callable":false,"version":11,"subclass":null,"par":null,"payment_frequency":null,"legal_final_maturity":null,"pre_refund_date":null,"mandatory_put_date":null}""",
            Accrual: """{"day_count_convention":null,"settlement_cycle_days":null,"holiday_calendar_id":null,"coupon_kind":null,"fixed_coupon_rate":null,"floating_rate_index":null,"floating_spread_bps":null,"version":11}""",
            Issuer: null,
            Prior: Full),
        new("missing maturity clears all four tables", "Bond", """{"isCallable":false,"couponType":"Fixed","couponRate":5}""",
            Parent: null, Lifecycle: null, Accrual: null, Issuer: null, Prior: Full),
        new("unparseable maturity clears all four tables", "Bond", """{"maturity":"june","isCallable":false}""",
            Parent: null, Lifecycle: null, Accrual: null, Issuer: null, Prior: Full),
        new("class change clears all four tables", "Equity", """{"shareClass":"Common"}""",
            Parent: null, Lifecycle: null, Accrual: null, Issuer: null, Prior: Full),
    ];

    private static readonly (string Table, Func<GoldenCase, string?> Expected)[] Tables =
    [
        ("bond_projection", static golden => golden.Parent),
        ("bond_lifecycle_projection", static golden => golden.Lifecycle),
        ("bond_accrual_convention_projection", static golden => golden.Accrual),
        ("bond_issuer_projection", static golden => golden.Issuer),
    ];

    [SecurityMasterDatabaseFact]
    public async Task Bond_LeavesTheGoldenRowsInAllFourTables()
    {
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var failures = new List<string>();

        foreach (var golden in Cases)
        {
            var securityId = Guid.NewGuid();
            if (golden.Prior is { } prior)
            {
                await store.UpsertProjectionAsync(Record(securityId, prior));
                foreach (var (table, _) in Tables)
                {
                    (await ReadRowAsync(table, securityId))
                        .Should().NotBeNull($"the prior record of [{golden.Name}] must project a '{table}' row for the clear to be meaningful");
                }
            }

            await store.UpsertProjectionAsync(Record(securityId, golden));

            foreach (var (table, expectedFor) in Tables)
            {
                var row = await ReadRowAsync(table, securityId);
                var expected = expectedFor(golden)?.Replace(Primary, IdentifierValue(securityId), StringComparison.Ordinal);
                if (!await JsonbEqualAsync(expected, row))
                {
                    failures.Add($"[{golden.Name}] {table}: expected {expected ?? "<no row>"} but the store wrote {row ?? "<no row>"}");
                }
            }
        }

        failures.Should().BeEmpty();
    }

    [SecurityMasterDatabaseFact]
    public async Task Bond_StaysReadableThroughItsReferenceProjectionStore()
    {
        // The read side was not touched by the migration; this proves the rows the registry writes
        // still decode through the unchanged query store, end to end through UpsertProjectionAsync.
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var securityId = Guid.NewGuid();
        await store.UpsertProjectionAsync(Record(securityId, Full));

        var reader = new PostgresBondReferenceProjectionStore(_fixture.Options);
        var bond = await reader.GetBondAsync(securityId);
        bond.Should().NotBeNull();
        bond!.IssuerName.Should().Be("Acme Corp");
        bond.CouponKind.Should().Be("Fixed");
        bond.FixedCouponRate.Should().Be(5.25m);
        bond.DayCountConvention.Should().Be("30/360");
        bond.SettlementCycleDays.Should().Be(2);
        bond.LifecycleStat.Should().Be("Callable");
        bond.MaturityDate.Should().Be(new DateOnly(2091, 6, 15));
        bond.Par.Should().Be(1000m);

        (await reader.GetIssuerLadderAsync("Acme Corp")).Should().Contain(row => row.SecurityId == securityId);
    }

    private async Task<string?> ReadRowAsync(string table, Guid securityId)
    {
        await using var connection = new NpgsqlConnection(_fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"select (to_jsonb(t) - 'security_id')::text from {_fixture.Options.Schema}.{table} t where security_id = @security_id;";
        command.Parameters.AddWithValue("security_id", securityId);
        return (string?)await command.ExecuteScalarAsync();
    }

    private async Task<bool> JsonbEqualAsync(string? left, string? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        await using var connection = new NpgsqlConnection(_fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select @left::jsonb = @right::jsonb;";
        command.Parameters.AddWithValue("left", left);
        command.Parameters.AddWithValue("right", right);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static string IdentifierValue(Guid securityId) => $"Golden-Bond-{securityId:N}";

    private static SecurityProjectionRecord Record(Guid securityId, GoldenCase golden)
    {
        using var terms = JsonDocument.Parse(golden.Terms);
        using var common = JsonDocument.Parse(golden.CommonTerms ?? $$"""{"displayName":"{{DisplayName}}","currency":"{{Currency}}"}""");
        var effectiveFrom = DateTimeOffset.UtcNow.AddDays(-1);
        return new(
            securityId,
            golden.AssetClass,
            golden.Retired ? SecurityStatusDto.Inactive : SecurityStatusDto.Active,
            DisplayName,
            Currency,
            SecurityIdentifierKind.InternalCode.ToString(),
            IdentifierValue(securityId),
            common.RootElement.Clone(),
            terms.RootElement.Clone(),
            JsonSerializer.SerializeToElement(new { sourceSystem = "integration-test", updatedBy = "bond-golden-fixture" }),
            Version,
            effectiveFrom,
            golden.Retired ? effectiveFrom.AddHours(1) : null,
            [new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, IdentifierValue(securityId), true, effectiveFrom)],
            []);
    }
}
