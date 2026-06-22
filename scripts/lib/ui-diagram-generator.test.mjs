import test from 'node:test';
import assert from 'node:assert/strict';

import {
  buildUiImplementationDot,
  buildUiNavigationDot,
  buildWpfScreenCatalogDot,
  buildWpfScreenSummaryDot,
  buildWpfWorkspaceScreenDot,
  parseMainPageXaml,
  parseNavigationService,
  parsePagesFile,
  parseShellPageDescriptors,
  parseTransientPages,
} from './ui-diagram-generator.mjs';

test('parseTransientPages normalizes fully qualified type names', () => {
  const content = `
services.AddTransient<DashboardPage>();
services.AddTransient<Meridian.Wpf.Views.WorkspacePage>();
services.AddTransient<Meridian.Wpf.Views.NotificationCenterPage>();
`;

  assert.deepEqual(parseTransientPages(content), [
    'DashboardPage',
    'WorkspacePage',
    'NotificationCenterPage',
  ]);
});

test('generated DOT is deterministic for the same fingerprint', () => {
  const navigation = buildUiNavigationDot({
    fingerprint: 'abc123fingerprint',
    navigationPages: [{ tag: 'Dashboard', pageType: 'DashboardPage', group: 'Primary' }],
    workspacePages: [{ workspace: 'Monitor', tag: 'Dashboard', label: 'Dashboard' }],
  });

  const implementation = buildUiImplementationDot({
    fingerprint: 'abc123fingerprint',
    appPages: ['DashboardPage'],
    pagesBySection: [{ section: 'Primary navigation pages', pageType: 'DashboardPage' }],
    navigationPages: [{ tag: 'Dashboard', pageType: 'DashboardPage', group: 'Primary' }],
    mainWindowDeps: [{ typeName: 'NavigationService', variable: 'navigationService' }],
    mainPageDeps: [{ typeName: 'ConnectionService', variable: 'connectionService' }],
  });

  assert.match(navigation, /Source fingerprint: abc123fingerprint/);
  assert.doesNotMatch(navigation, /Generated:/);
  assert.match(implementation, /Source fingerprint: abc123fingerprint/);
  assert.doesNotMatch(implementation, /Generated:/);
});

test('parsers extract sections, navigation tags, and workspace labels', () => {
  const pagesContent = `// Primary navigation pages\npublic partial class DashboardPage : Page { }`;
  const navContent = `// Primary navigation\nRegisterPage("Dashboard", typeof(DashboardPage));`;
  const xamlContent = `<!-- ═══ MONITOR Workspace ═══ -->\n<ListBoxItem Tag="Dashboard">\n  <TextBlock Text="Dashboard" Style="{StaticResource NavLabelStyle}" />\n</ListBoxItem>`;

  assert.deepEqual(parsePagesFile(pagesContent), [{ section: 'Primary navigation pages', pageType: 'DashboardPage' }]);
  assert.deepEqual(parseNavigationService(navContent), [{ group: 'Primary navigation', tag: 'Dashboard', pageType: 'DashboardPage' }]);
  assert.deepEqual(parseMainPageXaml(xamlContent), [{ workspace: 'MONITOR', tag: 'Dashboard', label: 'Dashboard' }]);
});

test('screen diagrams summarize workspace sections and screen metadata', () => {
  const registryPages = [
    {
      pageType: 'TradingWorkspaceShellPage',
      tag: 'TradingShell',
      title: 'Trading Workspace',
      subtitle: 'Monitor markets.',
      workspaceId: 'trading',
      section: 'Desk',
      order: 0,
      visibilityTier: 'Primary',
    },
    {
      pageType: 'OrderBookPage',
      tag: 'OrderBook',
      title: 'Order book',
      subtitle: 'Review depth.',
      workspaceId: 'trading',
      section: 'Market Feed',
      order: 20,
      visibilityTier: 'Primary',
    },
    {
      pageType: 'ProviderHealthPage',
      tag: 'ProviderHealth',
      title: 'Provider health',
      subtitle: 'Inspect readiness.',
      workspaceId: 'data',
      section: 'Operations Queue',
      order: 65,
      visibilityTier: 'Secondary',
    },
  ];

  const summary = buildWpfScreenSummaryDot({ fingerprint: 'abc123fingerprint', registryPages });
  const catalog = buildWpfScreenCatalogDot({ fingerprint: 'abc123fingerprint', registryPages });
  const workspace = buildWpfWorkspaceScreenDot({
    fingerprint: 'abc123fingerprint',
    workspaceName: 'Trading',
    pages: registryPages.filter((page) => page.workspaceId === 'trading'),
  });

  assert.match(summary, /Meridian WPF screen summary/);
  assert.match(summary, /Trading\\n2 screens \/ 2 sections\\nPrimary 2 \| Secondary 0 \| Overflow 0/);
  assert.match(summary, /Launchpad: Trading Workspace → TradingWorkspaceShellPage/);
  assert.match(catalog, /Meridian WPF screen catalog/);
  assert.match(catalog, /Order book\\nOrderBook → OrderBookPage\\nPrimary \| order 20/);
  assert.match(catalog, /Provider health\\nProviderHealth → ProviderHealthPage\\nSecondary \| order 65/);
  assert.match(workspace, /Meridian WPF Trading screens/);
  assert.match(workspace, /Trading workspace\\nPrimary 2 \| Secondary 0 \| Overflow 0/);
});

test('registry parser extracts current shell page descriptors', () => {
  const content = `
ShellPageRegistryBuilder.Page<DashboardPage>("Dashboard", "Reporting dashboard", "Monitor evidence readiness.", "reporting", "Dashboard", "\\uE71D", 20, ShellNavigationVisibilityTier.Primary, ["overview"], ["ReportingShell"]);
Page<Meridian.Wpf.Views.AccountingWorkspaceShellPage>("AccountingShell", "Accounting Workspace", "Govern accounting records.", "accounting", "Launchpad", "\\uE8D2", 0, ShellNavigationVisibilityTier.Primary, ["accounting"], ["FundLedger"]);
`;

  assert.deepEqual(parseShellPageDescriptors(content), [
    {
      pageType: 'AccountingWorkspaceShellPage',
      tag: 'AccountingShell',
      title: 'Accounting Workspace',
      subtitle: 'Govern accounting records.',
      workspaceId: 'accounting',
      section: 'Launchpad',
      order: 0,
      visibilityTier: 'Primary',
    },
    {
      pageType: 'DashboardPage',
      tag: 'Dashboard',
      title: 'Reporting dashboard',
      subtitle: 'Monitor evidence readiness.',
      workspaceId: 'reporting',
      section: 'Dashboard',
      order: 20,
      visibilityTier: 'Primary',
    },
  ]);
});
