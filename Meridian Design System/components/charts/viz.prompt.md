`ROLES` maps analytical series meaning to stroke, width, dash and marker tokens.
Use the same role across charts: actual, prior, benchmark, scenario, target,
estimated, positive or negative. Scenario terracotta is distinct from loss red.
These values remain CSS token references so scoped themes continue to apply.

`Fmt` carries the legacy chart defaults: two-decimal prices, compact USD currency,
integer basis points, unsigned compact volume and short English dates. These
helpers format display values, not authoritative accounting calculations. Pass
finite values already expressed in the correct units; override chart formatters
for non-USD currency, different instrument precision or locale requirements.
Missing/restricted conditions belong to the surrounding value/figure contract;
this carrier does not infer them from numbers.
