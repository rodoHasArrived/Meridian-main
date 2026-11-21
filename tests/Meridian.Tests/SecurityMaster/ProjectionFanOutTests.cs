using FluentAssertions;
using Meridian.Storage.SecurityMaster;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// An upsert used to run every registered projection writer, each deleting rows for a class the
/// security never had (N6 in the extensibility review). The store now visits only the writers a
/// write can affect; these pin that selection, since a writer left out here is a projection that
/// silently stops being published or cleared.
/// </summary>
public sealed class ProjectionFanOutTests
{
    [Fact]
    public void AFirstWrite_RunsOnlyTheCurrentClassWriter()
    {
        PostgresSecurityMasterStore.ProjectionWritersFor(priorAssetClass: null, "Equity")
            .Should().Equal("Equity");
    }

    [Fact]
    public void ARewriteInTheSameClass_RunsOnlyThatWriter_WhateverTheCasing()
    {
        PostgresSecurityMasterStore.ProjectionWritersFor("equity", "Equity")
            .Should().Equal(new[] { "Equity" }, "the stored class is compared case-insensitively, like the writers' own gates");
    }

    [Fact]
    public void AClassChange_RunsThePriorWriterToClearAndTheCurrentWriterToPublish()
    {
        var writers = PostgresSecurityMasterStore.ProjectionWritersFor("Equity", "Deposit");

        writers.Should().BeEquivalentTo(new[] { "Equity", "Deposit" });
        writers.Should().Equal(
            PostgresSecurityMasterStore.ProjectedAssetClasses.Where(c => c is "Equity" or "Deposit"),
            "writers run in registry order");
    }

    [Fact]
    public void AChangeToAnUnprojectedClass_RunsOnlyThePriorWriter()
    {
        // Every catalog class is now projected; a class the catalog does not know has no writer,
        // and the Equity rows still go.
        PostgresSecurityMasterStore.ProjectionWritersFor("Equity", "UnknownClass")
            .Should().Equal("Equity");
    }

    [Fact]
    public void AnUnprojectedClassWithNoHistory_RunsNoWriter()
    {
        PostgresSecurityMasterStore.ProjectionWritersFor(null, "UnknownClass").Should().BeEmpty();
        PostgresSecurityMasterStore.ProjectionWritersFor("Unknown", "UnknownClass").Should().BeEmpty();
    }

    [Fact]
    public void CustomAsset_HasItsOwnProfileFieldWriter()
    {
        // CustomAsset used to be the one unprojected class; migration 064 gives it a profile-keyed
        // projection, so moving to or from it runs its writer too.
        PostgresSecurityMasterStore.ProjectionWritersFor(null, "CustomAsset").Should().Equal("CustomAsset");
        PostgresSecurityMasterStore.ProjectionWritersFor("Equity", "CustomAsset")
            .Should().BeEquivalentTo(new[] { "Equity", "CustomAsset" });
        PostgresSecurityMasterStore.ProjectionWritersFor("CustomAsset", "Option")
            .Should().Equal("Option", "CustomAsset");
    }

    [Fact]
    public void EveryProjectedClass_IsReachableAsCurrentAndAsPrior()
    {
        foreach (var assetClass in PostgresSecurityMasterStore.ProjectedAssetClasses)
        {
            PostgresSecurityMasterStore.ProjectionWritersFor(null, assetClass).Should().Equal(assetClass);
            PostgresSecurityMasterStore.ProjectionWritersFor(assetClass, "UnknownClass").Should().Equal(assetClass);
        }
    }
}
