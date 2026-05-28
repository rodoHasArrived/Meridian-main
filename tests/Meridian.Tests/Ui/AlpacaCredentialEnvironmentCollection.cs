using Xunit;

namespace Meridian.Tests.Ui;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AlpacaCredentialEnvironmentCollection
{
    public const string Name = "Alpaca credential environment";
}
