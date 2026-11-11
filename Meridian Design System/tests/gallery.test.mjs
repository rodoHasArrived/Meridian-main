import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import { cardGroups, filterCards, normalizeCards } from './gallery.mjs';

const sample = (overrides = {}) => ({ path: 'components/core/example.card.html', name: 'Example', group: 'Core', viewport: '900x560', ...overrides });
test('the supported catalogue is exactly the manifest inventory, with optional descriptions and deterministic grouping', async () => {
  const manifest = JSON.parse(await readFile(new URL('../_ds_manifest.json', import.meta.url), 'utf8'));
  const cards = normalizeCards(manifest);
  assert.equal(cards.length, manifest.cards.length);
  assert.deepEqual(cards, normalizeCards({ cards: [...manifest.cards].reverse() }));
  assert.equal(cardGroups(cards).reduce((total, [, count]) => total + count, 0), cards.length);
  assert.deepEqual(normalizeCards({ cards: [sample()] })[0], { ...sample(), subtitle: '', width: 900, height: 560 });
});
test('search includes names, descriptions, groups and paths and combines with the selected group', () => {
  const cards = normalizeCards({ cards: [sample({ subtitle: 'Retained journal evidence' }), sample({ path: 'components/data/grid.card.html', name: 'Records', group: 'Data' })] });
  for (const query of ['EXAMPLE', 'journal evidence', 'CORE', 'core/example.card']) assert.equal(filterCards(cards, { query }).length, 1);
  assert.equal(filterCards(cards, { query: 'records', group: 'Core' }).length, 0);
  assert.equal(filterCards(cards, { query: 'records', group: 'Data' }).length, 1);
  assert.equal(filterCards(cards, { query: 'no matching specimen' }).length, 0);
  assert.equal(filterCards(cards, { query: '  ' }).length, 2);
});
test('card metadata remains literal text rather than being rewritten or treated as markup', () => {
  const name = '<img src=x onerror=alert(1)>';
  assert.equal(normalizeCards({ cards: [sample({ name })] })[0].name, name);
});
test('duplicate, unsafe or non-card paths fail instead of silently changing the catalogue', () => {
  assert.throws(() => normalizeCards({ cards: [sample(), sample()] }), /Duplicate/);
  assert.throws(() => normalizeCards({ cards: [sample(), sample({ path: 'COMPONENTS/core/example.card.html' })] }), /Duplicate/);
  for (const path of ['../outside.card.html', '/absolute.card.html', 'https://example.com/card.card.html', 'a//b.card.html', 'a/./b.card.html', 'a/%2e%2e/b.card.html', 'a\\b.card.html', 'card.html', 'a.card.html?x=1', 'a.card.html#x']) {
    assert.throws(() => normalizeCards({ cards: [sample({ path })] }), /Unsafe/, path);
  }
});
test('missing required metadata, invalid descriptions and non-positive or unsafe viewport dimensions fail visibly', () => {
  assert.throws(() => normalizeCards({}), /catalogue/);
  for (const key of ['name', 'group', 'path', 'viewport']) assert.throws(() => normalizeCards({ cards: [sample({ [key]: '' })] }), /missing/);
  for (const viewport of ['0x560', '900x0', '-1x5', '900', 'wide', '2.5x100', '9007199254740992x560']) {
    assert.throws(() => normalizeCards({ cards: [sample({ viewport })] }), /viewport/);
  }
  assert.throws(() => normalizeCards({ cards: [sample({ subtitle: 42 })] }), /description/);
});
