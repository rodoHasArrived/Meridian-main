import assert from 'node:assert/strict';
import { test } from 'node:test';
import { rootTokenDefaults, refreshScopedTokens } from '../scripts/token_defaults.mjs';

test('conditional overrides cannot be published as unconditional defaults', () => {
  const values = rootTokenDefaults(`
    @media (prefers-reduced-motion: reduce) { :root { --motion: 0ms; } }
    :root[data-theme="dark"] { --ink: white; }
    /* :root { --fake: red; } */
    :root { --motion: 240ms; --ink: #20252B; }
    @media (prefers-color-scheme: dark) { :root { --ink: white; } }
    :root, html[data-override] { --motion: 300ms; }
  `);
  assert.deepEqual(Object.fromEntries(values), {'--motion': '300ms', '--ink': '#20252B'});
});

test('unclosed CSS cannot silently publish a partial token contract', () => {
  assert.throws(() => rootTokenDefaults(':root { --ink: black;'), /Unclosed/);
});

test('theme metadata follows removed overrides and newly authored theme inputs', () => {
  const tokens = [
    { name: '--ink', value: 'black', kind: 'color', definedIn: 'theme.css' },
    { name: '--brand-ink', value: 'black', kind: 'color', definedIn: 'theme.css' },
    { name: '--ink', value: 'white', kind: 'color', definedIn: 'theme.css', scope: 'html[data-brand="sample"]' },
  ];
  const scopes = new Map([['theme.css\0html[data-brand="sample"]', new Map([['--brand-ink', 'navy']])]]);
  const result = refreshScopedTokens(tokens, scopes);
  assert.equal(result.filter(token => !token.scope).length, 2);
  assert.deepEqual(result.filter(token => token.scope), [{ name: '--brand-ink', value: 'navy', kind: 'color', definedIn: 'theme.css', scope: 'html[data-brand="sample"]' }]);
  assert.deepEqual(refreshScopedTokens(result, scopes), result);
  assert.throws(() => refreshScopedTokens(tokens, new Map([['theme.css\0html[data-brand="sample"]', new Map([['--unclassified', 'red']])]])), /Classify/);
});
