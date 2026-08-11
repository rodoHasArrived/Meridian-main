#!/usr/bin/env python3
"""Guard: every provider named in the shipped sample config must be a real DataSourceKind.

`config/appsettings.sample.json` is the file operators copy when they first configure Meridian.
It previously advertised a `StockSharp` data source that is not a `DataSourceKind` member, so
`DataSourceKindConverter.Read` — which fails closed on unknown values — threw a `JsonException`
at startup for anyone who followed it. The same block shipped `Rithmic.Password` and
`CQG.Password` fields, teaching users to put live broker credentials in a JSON file that the
sample's own banner tells them never to hold secrets.

This gate keeps the sample honest in both directions:

- every provider value the sample names (``DataSource`` and each ``DataSources.Sources[].Provider``)
  must exist in ``src/Meridian.Core/Config/DataSourceKind.cs``;
- the sample must not carry password/secret-shaped keys with the credential guidance it disclaims.

Run: ``python3 build/scripts/ci/check-sample-config-datasources.py``
"""

from __future__ import annotations

import json
import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
SAMPLE = REPO_ROOT / "config" / "appsettings.sample.json"
KIND_SOURCE = REPO_ROOT / "src" / "Meridian.Core" / "Config" / "DataSourceKind.cs"
TYPE_SOURCE = REPO_ROOT / "src" / "Meridian.Core" / "Config" / "DataSourceConfig.cs"
MASKER_SOURCE = REPO_ROOT / "src" / "Meridian.Core" / "Config" / "SensitiveValueMasker.cs"
REGISTRY_SOURCE = REPO_ROOT / "src" / "Meridian.Core" / "Config" / "SensitiveKeyRegistry.cs"
CATALOG_SOURCE = (
    REPO_ROOT / "src" / "Meridian.Infrastructure" / "Adapters" / "Core"
    / "ProviderCapabilityDescriptorCatalog.cs"
)
STREAMING_SOURCE = (
    REPO_ROOT / "src" / "Meridian.Application" / "Composition" / "Features"
    / "ProviderFeatureRegistration.Registry.cs"
)

# Fallback vocabulary if the masker cannot be parsed. The live list is read from
# SensitiveValueMasker so this guard and the runtime redaction share one definition of "secret".
FALLBACK_SECRET_NAMES = frozenset(
    {
        "KeyId", "SecretKey", "ApiKey", "Token", "Password", "Secret",
        "Credentials", "ConnectionString", "AccessKey", "BearerToken",
        "ClientSecret", "PrivateKey", "Certificate",
    }
)


FALLBACK_SECRET_FRAGMENTS = frozenset(
    {
        "password", "pwd", "secret", "key", "token", "credential",
        "connectionstring", "connection_string",
        "auth", "authorization", "session", "refresh", "bearer", "certificate",
    }
)


def secret_key_fragments() -> frozenset[str]:
    """Mirror ``SensitiveKeyRegistry.Fragments``, the runtime predicate.

    ``SensitiveKeyRegistry`` is explicit that per-surface lists are the bug it exists to fix, and
    ``IsSensitive`` matches any key *containing* a fragment. Exact-name membership let real
    property names through — ``ApiToken``, ``AccessToken``, ``RefreshToken`` are all sensitive at
    runtime but match no exact entry.
    """
    try:
        text = REGISTRY_SOURCE.read_text(encoding="utf-8")
        block = text[text.index("Fragments") :]
        block = block[: block.index("];")]
        found = {f.casefold() for f in re.findall(r'"([A-Za-z_]+)"', block)}
        return frozenset(found or FALLBACK_SECRET_FRAGMENTS)
    except (OSError, ValueError):
        return FALLBACK_SECRET_FRAGMENTS


def secret_key_names() -> frozenset[str]:
    """Mirror ``SensitiveValueMasker.SensitivePropertyNames``.

    An anchored pattern over a hand-written shortlist missed compound names the repository
    already treats as secret — ``SecretKey``, ``ClientSecret``, ``BearerToken``, ``AccessKey``.
    Reading the masker keeps one vocabulary instead of two that drift.
    """
    try:
        text = MASKER_SOURCE.read_text(encoding="utf-8")
        block = text[text.index("SensitivePropertyNames") :]
        block = block[: block.index("};")]
        names = {name for name in re.findall(r'"([A-Za-z]+)"', block)}
        return frozenset(names or FALLBACK_SECRET_NAMES)
    except (OSError, ValueError):
        return FALLBACK_SECRET_NAMES


def streaming_source_ids() -> frozenset[str]:
    """Factory ids registered for real-time streaming.

    Enum membership is necessary but not sufficient for the top-level ``DataSource``: Yahoo is a
    defined ``DataSourceKind`` yet has no streaming factory, so selecting it fails at collector
    startup. Advertising it as a primary real-time source is exactly the trap this guard exists
    to stop.
    """
    try:
        text = STREAMING_SOURCE.read_text(encoding="utf-8")
        return frozenset(re.findall(r'RegisterStreamingFactory\(\s*"([^"]+)"', text))
    except OSError:
        return frozenset()


# DataSourceKind names and catalog family keys agree by casefold except where the enum uses the
# short broker code. Keep this map tiny and explicit rather than guessing with prefix matching.
CATALOG_ALIASES = {"ib": "ibkr"}


def historical_capable_families() -> frozenset[str]:
    """Families declaring a historical provider in ProviderCapabilityDescriptorCatalog.

    Two spellings appear in the catalog and both mean the same thing:

    * named — ``new("synthetic", Historical: typeof(SyntheticHistoricalDataProvider), ...)``;
    * positional — ``new("alpaca", typeof(Streaming), typeof(Historical), ...)``, where the
      second ``typeof`` before any named argument is the historical slot.

    Reading only the named form would wrongly call Alpaca backfill-incapable, so both are parsed.
    An unreadable catalog returns empty and the caller skips the check rather than inventing a
    verdict.
    """
    try:
        text = CATALOG_SOURCE.read_text(encoding="utf-8")
    except OSError:
        return frozenset()

    capable: set[str] = set()
    for match in re.finditer(r'new\(\s*\n?\s*"([a-z0-9_.-]+)"\s*,(.*?)(?=\n\s*new\(|\n\s*\];)', text, re.S):
        family, body = match.group(1), match.group(2)
        if re.search(r"\bHistorical:\s*typeof\(", body):
            capable.add(family)
            continue
        # Positional args stop at the first named argument (Name: value).
        positional = re.split(r"\b[A-Z][A-Za-z]*:", body, maxsplit=1)[0]
        if len(re.findall(r"\btypeof\(", positional)) >= 2:
            capable.add(family)
    return frozenset(capable)


def strip_jsonc(raw: str) -> str:
    """Remove // line comments that sit outside string literals, then trailing commas."""
    out: list[str] = []
    for line in raw.splitlines():
        in_string = False
        escaped = False
        cut: int | None = None
        for index, char in enumerate(line):
            if escaped:
                escaped = False
                continue
            if char == "\\":
                escaped = True
                continue
            if char == '"':
                in_string = not in_string
                continue
            if not in_string and char == "/" and index + 1 < len(line) and line[index + 1] == "/":
                cut = index
                break
        out.append(line if cut is None else line[:cut])
    return re.sub(r",(\s*[}\]])", r"\1", "\n".join(out))


def declared_kinds() -> dict[str, int]:
    """Return the enum's member names mapped to their numeric values.

    Both forms matter: ``DataSourceKindConverter`` accepts a string name *and* a JSON number
    (``reader.TokenType == JsonTokenType.Number``), so a sample carrying ``"DataSource": 99``
    fails at startup exactly like an unknown name.
    """
    text = KIND_SOURCE.read_text(encoding="utf-8")
    body = text[text.index("enum DataSourceKind") :]
    pairs = re.findall(r"^\s{4}([A-Za-z][A-Za-z0-9]*)\s*=\s*(\d+)\s*,?\s*$", body, re.MULTILINE)
    return {name: int(value) for name, value in pairs}


def declared_source_types() -> dict[str, int]:
    """Return ``DataSourceType`` member names mapped to their numeric values.

    ``DataSourceConfig.Type`` carries ``[JsonConverter(JsonStringEnumConverter<DataSourceType>)]``,
    so an unreadable value is not a milder problem than a bad provider — it fails deserialization
    and takes the whole sample down with it.
    """
    text = TYPE_SOURCE.read_text(encoding="utf-8")
    body = text[text.index("enum DataSourceType") :]
    pairs = re.findall(r"^\s{4}([A-Za-z][A-Za-z0-9]*)\s*=\s*(\d+)\s*,?\s*$", body, re.MULTILINE)
    return {name: int(value) for name, value in pairs}


SECRET_KEYS_FOLDED = frozenset(name.casefold() for name in secret_key_names())
SECRET_FRAGMENTS = secret_key_fragments()


def is_secret_key(key: str, value: object) -> bool:
    """Flag credential-shaped keys the way the runtime redactor would.

    Two rules, because the runtime predicate alone is too broad for a config sample:

    * an **exact** ``SensitiveValueMasker`` property name is always a credential field, whatever
      its value — this is what catches an empty ``"Password": ""`` placeholder;
    * a **fragment** match (the runtime ``SensitiveKeyRegistry.IsSensitive`` rule) is flagged for
      scalar values. Fragments like ``refresh``, ``key`` and ``certificate`` legitimately appear in
      non-credential settings — ``StatusRefreshIntervalSeconds`` (int) and
      ``AllowSelfSignedCertificates`` (bool) are both in this sample — and a secret is never an
      interval or a flag, so numbers and booleans are exempt.

    **Containers get the narrower noun rule, not the fragments.** ``"ApiTokens": []`` must be
    caught: it is not an exact masker name, so restricting containers to exact names lets a
    credential-shaped placeholder array through, and recursion does not help when no nested key
    independently matches. But the runtime fragment list is deliberately broad because
    over-redacting a *value* is harmless, whereas over-flagging a *key* fails CI on legitimate
    configuration. ``PaperTrading.Sessions`` is the concrete counter-example — an ordinary
    paper-session object holding ``BaseDirectory``
    (``ConfigJsonSchemaGenerator.cs:168-175``) that the ``session`` fragment would condemn. So a
    container matches only when its name *ends with* a credential noun from the masker list,
    singular or plural: ``ApiTokens`` and ``Certificates`` match, ``Sessions`` and
    ``Authentication`` do not.
    """
    folded = key.casefold()
    if folded in SECRET_KEYS_FOLDED:
        return True
    if isinstance(value, (dict, list)):
        singular = folded[:-1] if folded.endswith("s") else folded
        return any(
            candidate.endswith(name)
            for candidate in (folded, singular)
            for name in SECRET_KEYS_FOLDED
        )
    # `null` is a credential placeholder just like `""` — Meridian models nullable credential
    # properties such as BackfillConfig.ApiToken, so `"ApiToken": null` still teaches operators to
    # fill a secret into JSON.
    if isinstance(value, (bool, int, float)):
        return False
    return any(fragment in folded for fragment in SECRET_FRAGMENTS)


def walk_secret_keys(node: object, path: str = "") -> list[str]:
    """Report secret-shaped keys regardless of value.

    The Rithmic and CQG password fields this guard exists to keep out were **empty strings**:
    the harm was the shape of the sample teaching operators to type a live credential into
    JSON, not the placeholder value. Requiring a non-empty value would let the exact regression
    this guard was written for pass.
    """
    findings: list[str] = []
    if isinstance(node, dict):
        for key, value in node.items():
            here = f"{path}.{key}" if path else key
            if is_secret_key(key, value):
                findings.append(here)
            findings.extend(walk_secret_keys(value, here))
    elif isinstance(node, list):
        for i, value in enumerate(node):
            findings.extend(walk_secret_keys(value, f"{path}[{i}]"))
    return findings


def main() -> int:
    if not SAMPLE.exists():
        print(f"error: {SAMPLE} not found", file=sys.stderr)
        return 1

    kinds = declared_kinds()
    if not kinds:
        print(f"error: no enum members parsed from {KIND_SOURCE}", file=sys.stderr)
        return 1

    document = json.loads(strip_jsonc(SAMPLE.read_text(encoding="utf-8")))
    errors: list[str] = []

    sources = document.get("DataSources", {}).get("Sources", []) or []
    rules = document.get("DataSources", {}).get("FailoverRules", []) or []

    # Ids drawn into real-time routing. CollectorModeRunner calls CreateStreamingClient for every
    # provider named by a failover rule (`:122`) without consulting DataSourceConfig.Type, so a
    # backfill-only kind referenced there throws at startup just like a bad top-level DataSource.
    realtime_ids: set[str] = set()
    for rule in rules:
        candidates = [rule.get("PrimaryProviderId")]
        backups = rule.get("BackupProviderIds")
        if isinstance(backups, list):
            candidates += backups
        for candidate in candidates:
            if isinstance(candidate, str) and candidate.strip():
                realtime_ids.add(candidate.casefold())

    source_types = declared_source_types()
    if not source_types:
        errors.append(f"no DataSourceType members parsed from {TYPE_SOURCE.relative_to(REPO_ROOT)}")
    types_folded = {name.casefold(): name for name in source_types}
    types_by_number = {value: name for name, value in source_types.items()}
    streaming_types = {"realtime", "both"}

    def resolve_source_type(value: object) -> tuple[str | None, str | None]:
        """Resolve ``Type`` the way ``JsonStringEnumConverter<DataSourceType>`` would.

        Coercing an unreadable value to a non-streaming default is the failure this must not
        repeat: ``"Type": "Streaming"`` would silently exempt the source from the streaming-factory
        check while the application refuses to deserialize the file at all.
        """
        if isinstance(value, bool) or not isinstance(value, (str, int)):
            return None, "is neither a name nor a number"
        if isinstance(value, int):
            name = types_by_number.get(value)
            return (name, None) if name else (None, "is not a defined DataSourceType value")
        text = value.strip()
        # Enum.TryParse accepts numeric strings, so "2" reads as Both exactly as a JSON 2 would.
        if text.lstrip("+-").isdigit():
            name = types_by_number.get(int(text))
            return (name, None) if name else (None, "is not a defined DataSourceType value")
        name = types_folded.get(text.casefold())
        return (name, None) if name else (None, "is not a defined DataSourceType name")

    named: list[tuple[str, object, bool]] = []
    if "DataSource" in document:
        named.append(("DataSource", document["DataSource"], True))
    for index, source in enumerate(sources):
        if "Provider" not in source:
            continue
        source_id = source.get("Id")
        referenced_for_realtime = isinstance(source_id, str) and source_id.casefold() in realtime_ids

        # DataSourceConfig.Type defaults to RealTime when the JSON field is omitted
        # (src/Meridian.Core/Config/DataSourceConfig.cs:34), so a bare {"Provider": "Yahoo"} is
        # modelled as a real-time source and must be checked, not skipped.
        if "Type" in source:
            resolved_type, type_error = resolve_source_type(source["Type"])
            if type_error:
                errors.append(
                    f"DataSources.Sources[{index}].Type {source['Type']!r} {type_error} "
                    f"(valid: {', '.join(sorted(source_types))})"
                )
                # Unreadable: assume the strictest reading rather than exempting the source.
                resolved_type = "RealTime"
        else:
            resolved_type = "RealTime"

        needs_streaming = resolved_type.casefold() in streaming_types or referenced_for_realtime
        named.append((f"DataSources.Sources[{index}].Provider", source["Provider"], needs_streaming))

    valid = (
        f"names: {', '.join(sorted(kinds))}; numbers: "
        f"{', '.join(str(v) for v in sorted(kinds.values()))}"
    )
    folded = {name.casefold(): name for name in kinds}
    by_number = {value: name for name, value in kinds.items()}

    def resolve(value: object) -> tuple[str | None, str | None]:
        """Resolve a sample value the way DataSourceKindConverter does.

        The converter uses case-insensitive ``Enum.TryParse`` and also accepts a JSON number or a
        numeric string, rejecting undefined values via ``Enum.IsDefined``. Matching those exact
        semantics matters in both directions: rejecting ``"synthetic"`` or ``"5"`` would fail the
        gate for a config the application loads perfectly well.
        """
        # bool is a subclass of int in Python; a JSON true/false is never a valid enum value.
        if isinstance(value, bool) or not isinstance(value, (str, int)):
            return None, f"is neither a name nor a number, so the converter cannot read it ({valid})"
        if isinstance(value, int):
            name = by_number.get(value)
            return (name, None) if name else (None, f"is not a defined DataSourceKind value ({valid})")
        text = value.strip()
        if text.lstrip("+-").isdigit():
            name = by_number.get(int(text))
            return (
                (name, None)
                if name
                else (None, f"is a numeric string outside the defined DataSourceKind values ({valid})")
            )
        name = folded.get(text.casefold())
        return (name, None) if name else (None, f"is not a DataSourceKind member ({valid})")

    streaming = streaming_source_ids()
    if not streaming:
        # An empty parse must not silently downgrade the real-time check to "anything goes":
        # DataSourceKind contains historical-only members, so the Yahoo trap would reopen.
        errors.append(
            f"no streaming factories parsed from {STREAMING_SOURCE.relative_to(REPO_ROOT)}; "
            f"the real-time DataSource check cannot run. Fix the parser or the path rather than "
            f"letting the guard pass by default."
        )

    for where, value, needs_streaming in named:
        name, problem = resolve(value)
        if problem is not None:
            errors.append(
                f"{where} = {value!r} {problem}. DataSourceKindConverter fails closed, so this "
                f"sample would throw at startup."
            )
            continue
        # Enum membership is not enough for anything that streams: a kind with no streaming
        # factory is rejected at collector startup, whether it is the top-level selector, a source
        # declared RealTime/Both, or a source pulled in by a failover rule.
        if needs_streaming and streaming and name is not None:
            if name.casefold() not in {sid.casefold() for sid in streaming}:
                errors.append(
                    f"{where} = {value!r} resolves to {name}, which has no streaming factory "
                    f"(registered: {', '.join(sorted(streaming))}). It cannot participate in "
                    f"real-time routing; document it under backfill instead."
                )

    # Every failover backup id must name a configured source. CollectorModeRunner resolves an
    # unknown id by falling back to the top-level DataSource, so a stale reference silently mints a
    # client under a misleading identity and can promote it after the real feeds fail. Removing the
    # StockSharp source while leaving "stocksharp-tertiary" in a rule did exactly that.
    configured_ids = {
        source["Id"] for source in sources if isinstance(source.get("Id"), str)
    }
    # CollectorModeRunner resolves ids with StringComparison.OrdinalIgnoreCase over a
    # case-insensitive provider map, so the guard must not reject a casing the runtime accepts.
    configured_folded = {source_id.casefold() for source_id in configured_ids}
    # An unexpected shape must be an error, never a skip. Three separate blind spots in this guard
    # came from the same habit — a missing Type, a null credential value, and a null failover field
    # each fell through a permissive `isinstance` check while the runtime failed on them. The
    # runtime is unforgiving here: CollectorModeRunner calls `.Concat(rule.BackupProviderIds)` and
    # `providerMap.ContainsKey(rule.PrimaryProviderId)`, both of which throw on null.
    def require_provider_id(field: str, value: object, rule_index: int) -> str | None:
        """Return a normalized id, or record why the value is unusable."""
        if not isinstance(value, str):
            errors.append(
                f"DataSources.FailoverRules[{rule_index}].{field} = {value!r} is not a string. "
                f"CollectorModeRunner dereferences this value directly and throws on null or a "
                f"non-string, so an enabled failover rule would fail at startup."
            )
            return None
        if not value.strip():
            errors.append(
                f"DataSources.FailoverRules[{rule_index}].{field} is empty; it cannot resolve to a "
                f"configured source."
            )
            return None
        return value

    for rule_index, rule in enumerate(rules):
        referenced: list[tuple[str, object]] = [
            ("PrimaryProviderId", rule.get("PrimaryProviderId"))
        ]
        backups = rule.get("BackupProviderIds", [])
        if backups is None or not isinstance(backups, list):
            errors.append(
                f"DataSources.FailoverRules[{rule_index}].BackupProviderIds = {backups!r} is not a "
                f"list. CollectorModeRunner concatenates it directly and throws on null."
            )
            backups = []
        referenced += [(f"BackupProviderIds[{i}]", backup) for i, backup in enumerate(backups)]

        for field, provider_id in referenced:
            resolved = require_provider_id(field, provider_id, rule_index)
            if resolved is None:
                continue
            if resolved.casefold() not in configured_folded:
                errors.append(
                    f"DataSources.FailoverRules[{rule_index}].{field} = {resolved!r} does not "
                    f"match any configured source id ({', '.join(sorted(configured_ids)) or 'none'}). "
                    f"CollectorModeRunner would fall back to the top-level DataSource under that "
                    f"identity."
                )

    # ProviderRoutingMapper.GetEffectiveBindings synthesizes "legacy-default-realtime" and
    # "legacy-default-historical" bindings from these two ids, with Enabled: true and without
    # consulting EnableFailover. A dangling id is therefore not inert: ProviderRoutingEngine skips
    # the binding as a missing connection, so routing previews and requests lose their route while
    # every other gate stays green. Failover references were already checked; these were not.
    data_sources = document.get("DataSources", {})
    provider_by_folded_id = {
        source["Id"].casefold(): source.get("Provider")
        for source in sources
        if isinstance(source.get("Id"), str)
    }

    for field, must_stream in (
        ("DefaultRealTimeSourceId", True),
        ("DefaultHistoricalSourceId", False),
    ):
        if field not in data_sources:
            continue
        default_id = data_sources[field]
        if not isinstance(default_id, str) or not default_id.strip():
            errors.append(
                f"DataSources.{field} = {default_id!r} is not a usable source id; omit the field "
                f"rather than leaving it empty, or the synthesized binding names nothing."
            )
            continue
        default_folded = default_id.casefold()
        if default_folded not in configured_folded:
            errors.append(
                f"DataSources.{field} = {default_id!r} does not match any configured source id "
                f"({', '.join(sorted(configured_ids)) or 'none'}). GetEffectiveBindings still "
                f"synthesizes a binding for it, which ProviderRoutingEngine then drops as a "
                f"missing connection."
            )
            continue
        name, reason = resolve(provider_by_folded_id.get(default_folded))
        if name is None:
            errors.append(f"DataSources.{field} = {default_id!r} names a source whose Provider {reason}.")
            continue

        if must_stream:
            if streaming and name.casefold() not in {sid.casefold() for sid in streaming}:
                errors.append(
                    f"DataSources.{field} = {default_id!r} resolves to {name}, which has no "
                    f"streaming factory (registered: {', '.join(sorted(streaming))}), so the "
                    f"synthesized real-time binding cannot serve a live route."
                )
            continue

        # Resolving to a configured source is not enough for the historical default: the family
        # must actually implement backfill, and ProviderFactory gates every backfill provider on
        # its own `Backfill.Providers.<Provider>.Enabled` opt-in. Pointing this default at
        # Synthetic while that flag stayed false is exactly how this guard's own sample regressed
        # — the binding is synthesized, the provider is never registered, and
        # ProviderRoutingService drops the route as unsupported.
        historical = historical_capable_families()
        if historical and CATALOG_ALIASES.get(name.casefold(), name.casefold()) not in historical:
            errors.append(
                f"DataSources.{field} = {default_id!r} resolves to {name}, which declares no "
                f"historical provider in ProviderCapabilityDescriptorCatalog (capable: "
                f"{', '.join(sorted(historical))}), so no backfill route can be served."
            )
            continue

        backfill_providers = document.get("Backfill", {}).get("Providers", {})
        gate = next(
            (value for key, value in backfill_providers.items() if key.casefold() == name.casefold()),
            None,
        )
        # An absent block is not a pass. `EnabledWhenOptedIn(cfg?.Enabled)` sees a null config and
        # returns no provider, exactly as it does for Enabled: false — so omitting the block fails
        # at runtime identically while a permissive isinstance check would let it through. That is
        # the same blind spot this guard's own docstring records three earlier instances of.
        if not isinstance(gate, dict):
            errors.append(
                f"DataSources.{field} = {default_id!r} resolves to {name}, but the sample has no "
                f"Backfill.Providers.{name} block. ProviderFactory opts in on "
                f"`EnabledWhenOptedIn(cfg?.Enabled)`, which returns no provider for a missing "
                f"config just as it does for a disabled one, so the synthesized historical "
                f"binding would have no registered provider behind it."
            )
        elif gate.get("Enabled") is not True:
            errors.append(
                f"DataSources.{field} = {default_id!r} resolves to {name}, but "
                f"Backfill.Providers.{name}.Enabled is {gate.get('Enabled')!r}. "
                f"ProviderFactory returns null for a backfill provider that is not opted in, so "
                f"the synthesized historical binding would resolve to a family with no registered "
                f"historical provider."
            )

    for path in walk_secret_keys(document):
        errors.append(
            f"{path} is a secret-shaped key; the sample must not carry credential fields at all, "
            f"empty placeholder or not. Document an environment variable instead."
        )

    if errors:
        print("sample config validation: FAILED", file=sys.stderr)
        for error in errors:
            print(f"  - {error}", file=sys.stderr)
        return 1

    print(
        f"sample config validation: pass "
        f"({len(named)} provider reference(s) checked against {len(kinds)} DataSourceKind members)"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
