# Meridian.Core.UnitTests

Core configuration and resilience tests. The production reference is Meridian.Core; process-wide state tests keep the existing serialized collection definitions.

These cases retain the namespaces and assertions they had in Meridian.Tests. Host-dependent cases
remain in that project. Both assemblies are included in Meridian.sln and the canonical non-integration
CI roster; a class filter for a moved test must target this project.

For incremental development:

```sh
python build/python/cli/buildctl.py test --project tests/Meridian.Core.UnitTests/Meridian.Core.UnitTests.csproj --profile session:meridian.core.unittests --queue
```

Use `--fresh` without `--profile` for independent outputs. See
[build observability](../../docs/development/build-observability.md) for profiles and timing.
