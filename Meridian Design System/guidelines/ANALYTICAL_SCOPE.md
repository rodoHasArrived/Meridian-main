# Analytical scope

> The object a figure is true of, stated on the figure. Component: `core/ObjectScope`
> (`ObjectScopeProvider` · `ScopeMark` · `ScopedValue` · `ScopeMismatch`). Objects:
> `guidelines/OBJECT_MODEL.md`. Figures: `guidelines/VALUE_ENVELOPE.md`.

`Duration 8.22` is not one number. It is at least four:

| Reading | Scope | What it claims |
| --- | --- | --- |
| The instrument's duration | `security` | True of every holding of this instrument |
| This holding's duration | `position` | True of one owner's slice |
| The portfolio's duration | `portfolio` | True of a population; this holding is one contributor |
| The accounting duration proxy | `account` | True of what the book carries, on one basis |

A screen that prints the label without the object is asking the reader to infer which of the four
it meant. Meridian does not ask that. **Every figure either sits on a surface that has declared its
object, or it wears its own scope word.**

## The scope ladder

Six object types can own a figure. The vocabulary is the object model's, so the word on a figure is
the word in the `ObjectHeader` above it.

| Scope | Kind | Answers |
| --- | --- | --- |
| `security` | instrument | How does the instrument behave, wherever it is held? |
| `lot` | holding | What did this acquisition cost, and what is left of it? |
| `position` | holding | What does this holding own, owe, and contribute? |
| `account` | aggregate | What does the book carry? |
| `portfolio` | aggregate | What is true of the whole population? |
| `legalEntity` | aggregate | What does the entity report? |

`kind` is what makes the *relation* between two scopes nameable, and the relation is what the
reader actually needs: instrument truth travels with the thing; holding truth belongs to one owner;
aggregate truth is a population in which this object is one contributor.

## The law

1. **A workspace declares its object once.** `ObjectScopeProvider scope="position"` wraps the
   surface. The scope word is then redundant on the surface's own figures and is printed muted or
   not at all.
2. **A figure that belongs to a different object wears its scope word.** No exceptions for
   "obvious" cases: `Duration` on a position page is the instrument's, and it says so.
3. **The first foreign figure in a region explains itself.** `explain` prints the relation
   sentence once — *"A property of the instrument — true of every holding of it, not of this
   one."* — rather than repeating it on every row.
4. **A region can be audited.** `ScopeMismatch` lists the figures inside it that belong to another
   object. Rendering nothing is the clean state; the notice is not decoration, it is a finding
   about the layout.
5. **Nest rather than relabel.** A position drawer inside a security workspace re-declares
   `scope="position"` for its subtree. Do not hand-label figures to fake a scope change.
6. **Scope is not condition, authority, or basis.** It answers *what object*, not *how good*,
   *who says so*, or *on which basis*. Those are `ValueEnvelope`'s job and they compose:
   a figure can be position-scoped, provisional, vendor-sourced, and statutory all at once.

## Where it is load-bearing

| Surface | Declared scope | Figures that must wear a word |
| --- | --- | --- |
| Security Master | `security` | Every holding figure — market value held, weight, contribution |
| Position Detail | `position` | Instrument analytics (duration, OAS, convexity), portfolio contributions, lot-level cost |
| Portfolio / book views | `portfolio` or `account` | Instrument properties quoted for a constituent |
| Comparison plates | the compared object | The scope column — identical measures across columns must share one scope |
| Traces | the object the result belongs to | Inputs drawn from a different object |

## Anti-patterns

- **Scope by adjacency.** Putting instrument analytics under a heading called *Security* and
  hoping the reader carries the heading down the column. Headings scroll; scope words do not.
- **Scope by unit.** `0.21` is not self-evidently a contribution because it is small.
- **The mega-page.** One surface carrying security, position, portfolio and accounting figures
  under one object header makes every number ambiguous. Split the objects into workspaces and
  relate them explicitly (`guidelines/WORKSTATION_HIERARCHY.md`).
- **Silent aggregation.** A portfolio figure printed on a position page with no contribution
  framing invites the reader to think the holding *is* the portfolio.
- **Decorating every figure.** If a region's figures all belong to the surface's own object, the
  scope words add noise. Declare the surface, mark the exceptions.

## Acceptance

A screen satisfies this law when: the surface declares its object; every figure whose object
differs carries the scope word; `ScopeMismatch` in any region either renders nothing or lists
exactly the figures a reviewer would flag; and no two figures with the same label and different
scopes appear in one region without both being marked.
