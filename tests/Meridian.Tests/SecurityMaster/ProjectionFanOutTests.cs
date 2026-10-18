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
            .Should().Equal("Equity", "the stored class is compared case-insensitively, like the writers' own gates");
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
        // CustomAsset is profile-governed and has no projection table; the Equity rows still go.
        PostgresSecurityMasterStore.ProjectionWritersFor("Equity", "CustomAsset")
            .Should().Equal("Equity");
    }

    [Fact]
    public void AnUnprojectedClassWithNoHistory_RunsNoWriter()
    {
        PostgresSecurityMasterStore.ProjectionWritersFor(null, "CustomAsset").Should().BeEmpty();
        PostgresSecurityMasterStore.ProjectionWritersFor("Unknown", "CustomAsset").Should().BeEmpty();
    }

    [Fact]
    public void EveryProjectedClass_IsReachableAsCurrentAndAsPrior()
    {
        foreach (var assetClass in PostgresSecurityMasterStore.ProjectedAssetClasses)
        {
            PostgresSecurityMasterStore.ProjectionWritersFor(null, assetClass).Should().Equal(assetClass);
            PostgresSecurityMasterStore.ProjectionWritersFor(assetClass, "CustomAsset").Should().Equal(assetClass);
        }
    }
}
