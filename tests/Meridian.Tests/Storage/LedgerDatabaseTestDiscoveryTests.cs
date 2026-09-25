using System.Reflection;
using FluentAssertions;

namespace Meridian.Tests.Storage;

public sealed class LedgerDatabaseTestDiscoveryTests
{
    [Fact]
    public void LedgerDatabaseFacts_AreSelectedByProductionCertification()
    {
        var excluded = typeof(LedgerDatabaseFactAttribute).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => method.IsDefined(typeof(LedgerDatabaseFactAttribute)))
                .Where(method => !HasIntegrationTrait(method) && !HasIntegrationTrait(type))
                .Select(method => $"{type.FullName}.{method.Name}"))
            .ToArray();

        excluded.Should().BeEmpty(
            "production certification selects Category=Integration and ordinary CI skips ledger database facts");
    }

    private static bool HasIntegrationTrait(MemberInfo member) =>
        member.GetCustomAttributesData().Any(attribute =>
            attribute.AttributeType == typeof(TraitAttribute)
            && attribute.ConstructorArguments.Count == 2
            && Equals(attribute.ConstructorArguments[0].Value, "Category")
            && Equals(attribute.ConstructorArguments[1].Value, "Integration"));
}
