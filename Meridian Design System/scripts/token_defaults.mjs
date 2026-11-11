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
