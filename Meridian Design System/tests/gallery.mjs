// Catalogue metadata is sourced only from the compiler's public manifest.
export function normalizeCards(manifest) {
  if (!manifest || !Array.isArray(manifest.cards)) throw new Error('The package manifest has no card catalogue.');
  const paths = new Set();
  return manifest.cards.map(record => {
    if (!record || ['name', 'group', 'path', 'viewport'].some(key => typeof record[key] !== 'string' || !record[key].trim())) {
      throw new Error('A card is missing its name, group, path, or viewport.');
    }
    const path = record.path.trim();
    if (!/^[a-zA-Z0-9_./-]+\.card\.html$/.test(path) || path.startsWith('/') || path.split('/').some(part => !part || part === '.' || part === '..')) {
      throw new Error(`Unsafe card path: ${path}`);
    }
    if (paths.has(path.toLowerCase())) throw new Error(`Duplicate card path: ${path}`);
    paths.add(path.toLowerCase());
    const size = /^(\d+)x(\d+)$/i.exec(record.viewport.trim());
    const [width, height] = size ? size.slice(1).map(Number) : [];
    if (!Number.isSafeInteger(width) || !Number.isSafeInteger(height) || width <= 0 || height <= 0) {
      throw new Error(`Invalid card viewport: ${record.viewport}`);
    }
    if (record.subtitle != null && typeof record.subtitle !== 'string') throw new Error(`Invalid card description: ${path}`);
    return { path, name: record.name.trim(), group: record.group.trim(), subtitle: record.subtitle?.trim() || '', viewport: `${width}x${height}`, width, height };
  }).sort((a, b) => a.group.localeCompare(b.group, 'en') || a.name.localeCompare(b.name, 'en') || a.path.localeCompare(b.path, 'en'));
}

export function filterCards(cards, { query = '', group = '' } = {}) {
  const terms = query.trim().toLowerCase();
  return cards.filter(card => (!group || card.group === group) && (!terms || [card.name, card.subtitle, card.group, card.path].join(' ').toLowerCase().includes(terms)));
}

export function cardGroups(cards) {
  const groups = new Map();
  for (const card of cards) groups.set(card.group, (groups.get(card.group) || 0) + 1);
  return [...groups].sort(([a], [b]) => a.localeCompare(b, 'en'));
}

function element(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}

function mountGallery() {
  const search = document.getElementById('gallery-search');
  const group = document.getElementById('gallery-group');
  const reset = document.getElementById('gallery-reset');
  const retry = document.getElementById('gallery-retry');
  const error = document.getElementById('gallery-error');
  const message = document.getElementById('gallery-error-message');
  const count = document.getElementById('gallery-count');
  const catalogue = document.getElementById('gallery-groups');
  const empty = document.getElementById('gallery-empty');
  let cards = [];
  let active = null;

  function closePreview() {
    if (!active) return;
    const prior = active;
    active = null;
    prior.abort.abort();
    clearTimeout(prior.timer);
    prior.refs.region.hidden = true;
    prior.refs.viewport.replaceChildren();
    prior.refs.toggle.textContent = 'Show preview';
    prior.refs.toggle.setAttribute('aria-label', `Show preview: ${prior.card.name}`);
    prior.refs.toggle.setAttribute('aria-expanded', 'false');
  }

  async function openPreview(card, refs) {
    closePreview();
    const state = { card, refs, abort: new AbortController(), settled: false, timer: null };
    active = state;
    refs.region.hidden = false;
    refs.region.setAttribute('aria-busy', 'true');
    refs.toggle.textContent = 'Hide preview';
    refs.toggle.setAttribute('aria-label', `Hide preview: ${card.name}`);
    refs.toggle.setAttribute('aria-expanded', 'true');
    refs.retry.hidden = true;
    refs.status.textContent = 'Loading specimen document…';
    function fail(reason) {
      if (active !== state || state.settled) return;
      state.settled = true;
      clearTimeout(state.timer);
      state.abort.abort();
      refs.viewport.replaceChildren();
      refs.region.setAttribute('aria-busy', 'false');
      refs.status.textContent = `Preview unavailable: ${reason}`;
      refs.retry.hidden = false;
    }
    state.timer = setTimeout(() => fail('the document did not load within 15 seconds.'), 15000);
    try {
      const response = await fetch(new URL(`../${card.path}`, document.baseURI), { signal: state.abort.signal });
      if (!response.ok) throw new Error(`HTTP ${response.status}. Open the card directly or retry.`);
      await response.body?.cancel();
      if (active !== state || state.settled) return;
      const frame = element('iframe');
      frame.title = `${card.name} specimen (${card.viewport})`;
      frame.width = String(card.width);
      frame.height = String(card.height);
      frame.style.width = `${card.width}px`;
      frame.style.height = `${card.height}px`;
      frame.addEventListener('error', () => fail('the specimen document could not be opened.'));
      frame.addEventListener('load', () => {
        if (active !== state || state.settled) return;
        state.settled = true;
        clearTimeout(state.timer);
        try {
          frame.contentDocument.documentElement.dataset.theme = 'light';
          if (frame.contentDocument.body) frame.contentDocument.body.dataset.theme = 'light';
        } catch { /* The specimen owns its runtime; document load does not certify its contents. */ }
        refs.region.setAttribute('aria-busy', 'false');
        refs.status.textContent = 'Document loaded. Open the card directly to inspect its full behavior.';
      });
      frame.src = new URL(`../${card.path}`, document.baseURI).href;
      refs.viewport.replaceChildren(frame);
    } catch (cause) {
      if (active === state && !state.settled) fail(cause instanceof Error ? cause.message : 'the document request failed.');
    }
  }

  function cardNode(card, index) {
    const article = element('article', 'gallery-card');
    article.dataset.path = card.path;
    const header = element('div', 'gallery-card-header');
    const title = element('h3', '', card.name);
    title.id = `gallery-card-${index}`;
    article.setAttribute('aria-labelledby', title.id);
    header.append(title);
    if (card.subtitle) header.append(element('p', 'gallery-description', card.subtitle));
    header.append(element('p', 'gallery-path', card.path), element('p', 'gallery-dimensions', `Intended viewport: ${card.width} × ${card.height}`));
    const actions = element('div', 'gallery-actions');
    const link = element('a', '', 'Open card');
    link.href = new URL(`../${card.path}`, document.baseURI).href;
    link.setAttribute('aria-label', `Open card: ${card.name}`);
    const toggle = element('button', '', 'Show preview');
    toggle.type = 'button';
    toggle.dataset.previewToggle = '';
    toggle.setAttribute('aria-label', `Show preview: ${card.name}`);
    toggle.setAttribute('aria-expanded', 'false');
    const region = element('div', 'gallery-preview');
    region.id = `gallery-preview-${index}`;
    region.hidden = true;
    region.setAttribute('role', 'region');
    region.setAttribute('aria-label', `Preview: ${card.name}`);
    toggle.setAttribute('aria-controls', region.id);
    const status = element('p', 'gallery-preview-status');
    status.setAttribute('role', 'status');
    const retryPreview = element('button', '', 'Retry preview');
    retryPreview.type = 'button';
    retryPreview.hidden = true;
    const viewport = element('div', 'gallery-preview-viewport');
    viewport.tabIndex = 0;
    viewport.setAttribute('role', 'region');
    viewport.setAttribute('aria-label', `Scrollable specimen viewport: ${card.name}`);
    region.append(status, retryPreview, viewport);
    const refs = { region, status, retry: retryPreview, viewport, toggle };
    toggle.addEventListener('click', () => active?.card.path === card.path ? closePreview() : openPreview(card, refs));
    retryPreview.addEventListener('click', () => { openPreview(card, refs); toggle.focus(); });
    actions.append(link, toggle);
    header.append(actions);
    article.append(header, region);
    return article;
  }

  function render() {
    closePreview();
    const shown = filterCards(cards, { query: search.value, group: group.value });
    count.textContent = `${shown.length} of ${cards.length} cards`;
    empty.hidden = shown.length !== 0;
    const sections = [];
    let index = 0;
    for (const [name, total] of cardGroups(shown)) {
      const section = element('section', 'gallery-group');
      section.dataset.group = name;
      const title = element('h2', '', name);
      title.append(element('span', '', `${total} ${total === 1 ? 'card' : 'cards'}`));
      const grid = element('div', 'gallery-card-grid');
      for (const card of shown.filter(item => item.group === name)) grid.append(cardNode(card, index++));
      section.append(title, grid);
      sections.push(section);
    }
    catalogue.replaceChildren(...sections);
  }

  async function loadCatalogue(restoreFocus = false) {
    closePreview();
    [search, group, reset, retry].forEach(control => { control.disabled = true; });
    count.textContent = 'Loading catalogue…';
    try {
      if (location.protocol === 'file:') throw new Error('Serve the package over local HTTP to load its manifest and specimen documents.');
      const response = await fetch(new URL('../_ds_manifest.json', document.baseURI));
      if (!response.ok) throw new Error(`The manifest request returned HTTP ${response.status}.`);
      cards = normalizeCards(await response.json());
      group.replaceChildren(new Option(`All groups (${cards.length})`, ''));
      for (const [name, total] of cardGroups(cards)) group.append(new Option(`${name} (${total})`, name));
      error.hidden = true;
      [search, group, reset].forEach(control => { control.disabled = false; });
      render();
      if (restoreFocus) search.focus();
    } catch (cause) {
      cards = [];
      catalogue.replaceChildren();
      empty.hidden = true;
      error.hidden = false;
      message.textContent = cause instanceof Error ? cause.message : 'The catalogue could not be loaded.';
      count.textContent = 'Catalogue unavailable';
      retry.disabled = false;
      if (restoreFocus) retry.focus();
    }
  }
  document.getElementById('gallery-filters').addEventListener('submit', event => event.preventDefault());
  search.addEventListener('input', render);
  group.addEventListener('change', render);
  reset.addEventListener('click', () => { search.value = ''; group.value = ''; render(); });
  retry.addEventListener('click', () => loadCatalogue(true));
  loadCatalogue();
}

if (typeof document !== 'undefined') mountGallery();
