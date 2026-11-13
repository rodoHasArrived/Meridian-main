using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.TestSupport;
using Meridian.Tests.FixedIncome;
using Npgsql;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The jsonb text a stored row reads back as, before and after migration 068's
/// <c>jsonb_set(asset_specific_terms, '{termsFormat}', to_jsonb(terms_format), true)</c>, is exactly the
/// pair the unit fixtures in <see cref="BondSecurityTermsDigestTests"/> pin, and the bond terms hash of the
/// stamped row equals the raw-text hash an untagged-era receipt retained.
/// </summary>
[Trait("Category", "Integration")]
public sealed class BondSecurityTermsDigestPostgresTests
{
    [SecurityMasterDatabaseFact]
    public async Task Migration068Stamp_IsUndoneByteForByte_SoTheUntaggedEraTermsHashStillMatches()
    {
        await using var server = await PostgresTestServer.CreateAsync("MERIDIAN_SECURITY_MASTER_CONNECTION_STRING");
        await using var connection = new NpgsqlConnection(server.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select $1::jsonb::text, jsonb_set($1::jsonb, '{termsFormat}', to_jsonb($2::text), true)::text, $3::jsonb::text", connection);
        command.Parameters.AddWithValue(BondSecurityTermsDigestTests.UntaggedTerms);
        command.Parameters.AddWithValue(SecurityTermsFormats.Flat);
        command.Parameters.AddWithValue("""{"issuer":"Acme"}""");
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        var untagged = reader.GetString(0);
        var stamped = reader.GetString(1);
        var common = reader.GetString(2);

        untagged.Should().Be(BondSecurityTermsDigestTests.UntaggedJsonbTerms);
        stamped.Should().Be(BondSecurityTermsDigestTests.StampedJsonbTerms);
        SecurityTermsEnvelope.RemoveRedundantDeclaredFormat(Parse(stamped)).Should().Be(untagged);
        BondSecurityTermsDigest.Compute(Parse(common), Parse(stamped))
            .Should().Be(BondSecurityTermsDigest.ComputeRawText(Parse(common), Parse(untagged)));
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
