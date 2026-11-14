// Ordinary HTML consumption of the actual 4.3.0 namespace. All values are synthetic.
(() => {
  'use strict';
  const h = React.createElement;
  const { DataGrid, DataCondition, DataConditionValue, ObjectScopeProvider, ScopedValue, Toggle, OrderTicket, Button } = window.MeridianDesignSystem_205d17;
  const conditions = ['confirmed', 'provisional', 'estimated', 'stale', 'overridden', 'missing', 'na', 'pending', 'exception', 'reconciled', 'restricted'];
  const seed = [
    { id: 'SYN-01', name: 'Synthetic bond holding', amount: 125000, evidence: 'Confirmed statement' },
    { id: 'SYN-02', name: 'Synthetic fund interest', amount: 82450.25, evidence: 'Review required' },
    { id: 'SYN-03', name: 'Synthetic receivable', amount: 6320, evidence: 'Confirmed statement' }
  ];
  const money = value => value.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  function App() {
    const [condition, setCondition] = React.useState('stale');
    const [theme, setTheme] = React.useState('light');
    const [compact, setCompact] = React.useState(false);
    const [review, setReview] = React.useState(false);
    const [evidence, setEvidence] = React.useState(false);
    const [order, setOrder] = React.useState('No simulated order submitted.');
    const rows = review ? seed.filter(row => row.evidence === 'Review required') : seed;
    const hidden = ['restricted', 'missing', 'na'].includes(condition);
    const columns = [
      { key: 'name', label: 'Record', width: 230 },
      { key: 'amount', label: 'Amount (USD)', align: 'right', cell: row => h(DataConditionValue, { value: money(row.amount), condition, detail: condition === 'restricted' ? 'Fixture policy' : condition === 'stale' ? '2026-11-10 20:00:00Z' : undefined, align: 'right' }) },
      { key: 'evidence', label: 'Evidence', width: 170 }
    ];
    return h(React.Fragment, null,
      h('div', { className: 'controls' },
        h('label', { className: 'field' }, 'Data condition', h('select', { 'aria-label': 'Data condition', value: condition, onChange: event => { setCondition(event.target.value); setEvidence(false); } }, conditions.map(value => h('option', { key: value, value }, value)))),
        h('label', { className: 'field' }, 'Theme', h('select', { 'aria-label': 'Theme', value: theme, onChange: event => { setTheme(event.target.value); document.documentElement.dataset.theme = event.target.value; } }, h('option', { value: 'light' }, 'Light'), h('option', { value: 'dark' }, 'Dark'))),
        h(Toggle, { label: 'Compact rows', checked: compact, onChange: setCompact }),
        h(Toggle, { label: 'Only review required', checked: review, onChange: setReview })),
      h('div', { className: 'release-grid' },
        h('section', { className: 'surface', 'aria-labelledby': 'register-title' },
          h('h2', { id: 'register-title' }, 'Evidence register'),
          h('p', { className: 'note' }, 'Fixture · SYN-ACCOUNT · USD · unposted · statement as of 2026-11-10 20:00:00Z'),
          h('div', { className: 'state-message', role: 'status' }, h(DataCondition, { condition, chip: true }), ' ', condition === 'stale' ? 'Last good values retained for inspection. Posting remains outside this simulation.' : hidden ? 'Value text is withheld by this condition; unknown values are never substituted with zero.' : 'Condition applies to every synthetic amount; no aggregate total is asserted.'),
          h(DataGrid, { rows, columns, rowKey: 'id', density: compact ? 'dense' : 'standard', label: 'Synthetic evidence records', caption: 'Synthetic evidence · USD · 2026-11-10 20:00:00Z', maxHeight: 360 }),
          h('div', { className: 'actions' },
            h(Button, { variant: 'ghost', onClick: () => { setCondition('confirmed'); setReview(false); setEvidence(false); } }, 'Reset fixture'),
            h(Button, { variant: 'ghost', disabled: hidden, onClick: () => setEvidence(value => !value), 'aria-expanded': evidence, 'aria-controls': 'source-context' }, 'Inspect evidence')),
          evidence ? h('div', { id: 'source-context' }, h('h3', null, 'Source history'), h('p', null, 'Synthetic statement SYN-STMT-17 · revision R17 · received 2026-11-11 09:00:00Z. Source values have no manual overrides; the review difference on SYN-02 is unresolved.')) : null,
          h('details', { className: 'fixture-ticket' }, h('summary', null, 'Order validation fixture'), h('p', { className: 'note' }, 'Live mode tests draft acknowledgement. This local callback never sends an order.'), h(OrderTicket, { symbol: 'SYN', lastPrice: 100, environment: 'live', onSubmit: draft => setOrder(`Simulated ${draft.side} ${draft.qty} ${draft.symbol}; no order sent.`) }), h('p', { role: 'status', 'data-order-result': true }, order))),
        h('aside', { 'aria-label': 'Scope and condition contracts' },
          h('section', { className: 'surface' }, h('h2', null, 'A figure states its scope'),
            h(ObjectScopeProvider, { scope: 'position', object: 'SYN-01' },
              h(ScopedValue, { label: 'Market value', value: hidden ? (condition === 'restricted' ? 'Restricted' : 'Missing') : '$125,000.00' }),
              h(ScopedValue, { scope: 'security', label: 'Duration', value: '8.22 years', explain: true }),
              h(ScopedValue, { scope: 'portfolio', label: 'Duration contribution', value: '0.21 years', explain: true }))),
          h('section', { className: 'surface' }, h('h2', null, 'Condition vocabulary'), h('ul', { className: 'status-list' }, conditions.map(value => h('li', { key: value }, h(DataCondition, { condition: value, chip: true }))))),
          h('section', { className: 'surface' }, h('h2', null, 'Inspect and recover'), h('p', null, 'Tab moves between controls. Space changes a switch. Filters, density and theme are local state.'), h('p', { className: 'note' }, 'The source bundle, fonts and preview runtime load from the extracted package. No provider, credentials or network services are needed.')))));
  }
  ReactDOM.createRoot(document.getElementById('release-root')).render(h(App));
})();
