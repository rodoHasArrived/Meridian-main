using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Domain;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The securities.listing_mic column has one definition: the store's write rule and migration
/// 048's backfill must resolve the same MIC from the same common terms.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ListingMicColumnTests
{
    private const string MigrationFile = "048_security_master_listing_mic.sql";

    [Theory]
    [InlineData("""{"primaryListingMic":" xnas ","exchange":"NYSE"}""", "XNAS")]
    [InlineData("""{"exchange":"NYSE"}""", "XNYS")]
    [InlineData("""{"exchange":"Nasdaq"}""", "XNAS")]
    [InlineData("""{"exchange":"XLON"}""", "XLON")]
    [InlineData("""{"primaryListingMic":"NASDAQ","exchange":"AMEX"}""", "XASE")]
    [InlineData("""{"primaryListingMic":"ZZZZ"}""", "ZZZZ")]
    [InlineData("""{"exchange":"SMART"}""", null)]
    [InlineData("""{"exchange":"ZZZZ"}""", null)]
    [InlineData("""{}""", null)]
    public void ResolveListingMic_PrefersAWellFormedPrimaryListingMic_ThenTheMappedExchange(string commonTerms, string? expected)
    {
        using var document = JsonDocument.Parse(commonTerms);

        PostgresSecurityMasterStore.ResolveListingMic(document.RootElement).Should().Be(expected);
    }

    [Fact]
    public void Migration_IsAdditive_AndLeavesExchangeCodeUntouched()
    {
        var sql = ReadMigration();

        sql.Should().Contain("add column if not exists listing_mic text null");
        sql.Should().Contain("where listing_mic is null");
        sql.Should().Contain("create index if not exists ix_securities_listing_mic");
        sql.Should().NotContain("set exchange_code");
        sql.Should().NotContain("drop ");
    }

    [Fact]
    public void Migration_BackfillMapsExactlyTheBundledCodesAndExchangeNames()
    {
        var sql = ReadMigration();

        foreach (var code in MicReference.Codes)
        {
            sql.Should().Contain($"'{code}'", $"bundled MIC {code} must be passed through by the backfill");
        }

        foreach (var (name, mic) in MicReference.ExchangeNames)
        {
            sql.Should().Contain($"when upper(btrim(exchange_code)) = '{name.ToUpperInvariant()}' then '{mic}'");
        }

        CountOf(sql, "when upper(btrim(exchange_code)) = '").Should().Be(
            MicReference.ExchangeNames.Count, "the backfill must not map names the write path does not");
    }

    private static int CountOf(string haystack, string needle)
        => haystack.Split(needle, StringSplitOptions.None).Length - 1;

    private static string ReadMigration()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Meridian.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the repository root must be discoverable from the test output");
        return File.ReadAllText(Path.Combine(
            directory!.FullName, "src", "Meridian.Storage", "SecurityMaster", "Migrations", MigrationFile));
    }
}
