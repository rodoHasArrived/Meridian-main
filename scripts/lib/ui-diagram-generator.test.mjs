import test from 'node:test';
import assert from 'node:assert/strict';

import {
  buildUiImplementationDot,
  buildUiNavigationDot,
  parseMainPageXaml,
  parseNavigationService,
  parsePagesFile,
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
