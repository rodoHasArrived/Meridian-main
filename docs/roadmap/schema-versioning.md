# Roadmap Schema Versioning

Every roadmap registry starts with:

```yaml
schema:
  id: meridian.<registry-name>
  version: "1.0.0"
  minimum_renderer_version: "1.0.0"
```

## Version rules

| Change | Version impact |
| --- | --- |
| Clarify description text | Patch |
| Add optional field | Minor |
| Add enum value | Minor |
| Add required field | Major |
| Rename or remove a field | Major |
| Change status meaning | Major |
| Change generated output semantics | Major or renderer major |

Validators must fail on unknown schema IDs, unsupported major versions, duplicate IDs, broken references, and accepted or done work without evidence.
