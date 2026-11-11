// Read only unconditional root defaults. Media/attribute overrides must never
// replace the manifest's base value, even when authored before the base rule.
export function rootTokenDefaults(css, rootSelector = ':root') {
  const text = css.replace(/\/\*[\s\S]*?\*\//g, '');
  const values = new Map();
  let start = 0;
  while (start < text.length) {
    const open = text.indexOf('{', start);
    if (open < 0) break;
    let depth = 1, close = open + 1;
    for (; close < text.length && depth; close++) {
      if (text[close] === '{') depth++;
      else if (text[close] === '}') depth--;
    }
    if (depth) throw new Error('Unclosed token stylesheet block');
    const selector = text.slice(start, open).trim();
    if (selector.split(',').some(part => part.trim() === rootSelector)) {
      for (const match of text.slice(open + 1, close - 1).matchAll(/(--[\w-]+)\s*:\s*([^;{}]+);/g)) {
        values.set(match[1], match[2].trim());
      }
    }
    start = close;
  }
  return values;
}

// Qualified records describe authored declarations in each published selector.
// Inherited roles remain available through the base record; removed local
// declarations must not survive as a fictitious theme override.
export function refreshScopedTokens(tokens, scopes) {
  const kinds = new Map(tokens.map(token => [token.name, token.kind]));
  const seen = new Set();
  const output = tokens.flatMap(token => {
    if (!token.scope) return [token];
    const key = `${token.definedIn}\0${token.scope}`;
    const value = scopes.get(key)?.get(token.name);
    if (value === undefined) return [];
    seen.add(`${key}\0${token.name}`);
    return [{ ...token, value }];
  });
  for (const [key, values] of scopes) {
    const [definedIn, scope] = key.split('\0');
    for (const [name, value] of [...values].sort(([a], [b]) => a < b ? -1 : a > b ? 1 : 0)) {
      if (seen.has(`${key}\0${name}`)) continue;
      const kind = kinds.get(name);
      if (!kind) throw new Error(`Classify new scoped token before publication: ${name}`);
      output.push({ name, value, kind, definedIn, scope });
    }
  }
  return output;
}
