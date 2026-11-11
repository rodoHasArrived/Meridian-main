import assert from 'node:assert/strict';
import { test } from 'node:test';
import { rootTokenDefaults } from '../scripts/token_defaults.mjs';

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
