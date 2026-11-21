Masthead chrome — the near-black brand bar (`#2E2E2E`) capping the light workstation: brand mark + breadcrumb · command search · UTC clock · environment mode badge · signed-in operator.

```jsx
<WorkstationTopbar environment="PAPER" />
```

Pass as little as possible. With `templates/_scaffold/` loaded the masthead reads the shared world itself: the breadcrumb and back affordance come from `MeridianAppMap` + the URL, the clock ticks off `MeridianWorld`, the operator chip is `MeridianWorld.OPERATOR`, and the search field is bound by `ds-base.js`. `moduleLabel` is only the no-app-map fallback; `clock` only freezes the clock deliberately.

The breadcrumb is Domain › parent screen › screen › object, and drops from the outside in as the bar narrows so the command field keeps its width. For the object crumb to read "1100 · Cash & equivalents" instead of "Account 1100", name it once from the screen that loaded the record:

```js
MeridianAppMap.nameObject("account", rec.code, rec.code + " · " + rec.name);
```

Override with `breadcrumb={[…]}` only to append something the URL cannot express (a wizard step, an open drawer). Back renders only when there is real in-suite history or a parent screen — never a dead chevron.

Environment is always visible: `LIVE` (red), `PAPER` (blue), `FIXTURE` (amber). Use the light brand mark (`meridian-mark-light.svg`) on the dark bar.
