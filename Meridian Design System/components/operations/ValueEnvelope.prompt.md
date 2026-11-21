# ValueEnvelope

One contract for what is known about a figure — condition, authority, evidence, clocks, materiality — rendered in three fixed disclosure tiers (inline marks → hover envelope → `EnvelopePanel`). Use it instead of stacking condition, provenance, authority, freshness and materiality components beside one number. `envelopeCheck`/`EnvelopeCoherence` run the temporal and evidential coherence rules on the data. Law: `guidelines/VALUE_ENVELOPE.md`.

The optional onInspect action exposes button semantics and Enter/Space activation; informational envelopes remain focusable for their linked metadata tooltip. Evidence links in the full panel have usable pointer targets.
