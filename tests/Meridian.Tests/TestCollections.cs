using Xunit;

namespace Meridian.Tests;

/// <summary>
/// Collection definition that disables parallelization for tests that modify
/// process-wide state (e.g., environment variables) and would otherwise interfere
/// with concurrently running tests.
/// </summary>
[CollectionDefinition("Sequential", DisableParallelization = true)]
public sealed class SequentialCollection { }
