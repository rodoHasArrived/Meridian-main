import crypto from 'node:crypto';
import fs from 'node:fs/promises';
import path from 'node:path';
import { instance } from '@viz-js/viz';

export function escapeLabel(value) {
  return value
    .replace(/\\/g, '\\\\')
    .replace(/"/g, '\\"')
    .replace(/\{/g, '\\{')
    .replace(/\}/g, '\\}')
    .replace(/\|/g, '\\|')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;');
}

export function displayName(name) {
  return name
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/View Model/g, 'ViewModel')
    .replace(/Ui/g, 'UI')
    .trim();
}

export function normalizeTypeName(typeName) {
  return typeName.split('.').pop()?.trim() ?? typeName.trim();
}

function workspaceDisplayName(workspaceId) {
  const labels = new Map([
    ['trading', 'Trading'],
    ['portfolio', 'Portfolio'],
    ['accounting', 'Accounting'],
    ['reporting', 'Reporting'],
    ['strategy', 'Strategy'],
    ['data', 'Data'],
    ['settings', 'Settings'],
  ]);

  return labels.get(workspaceId.toLowerCase()) ?? displayName(workspaceId);
}

const workspaceOrder = new Map([
  ['trading', 0],
  ['portfolio', 1],
  ['accounting', 2],
  ['reporting', 3],
  ['strategy', 4],
  ['data', 5],
  ['settings', 6],
]);

export function parsePagesFile(content) {
  const lines = content.split(/\r?\n/);
  const pages = [];
  let currentSection = 'Uncategorized';

  for (const line of lines) {
    const sectionMatch = line.match(/^\/\/\s+(.+?)$/);
    if (sectionMatch) {
      currentSection = sectionMatch[1].trim();
      continue;
    }

    const classMatch = line.match(/^public\s+partial\s+class\s+(\w+)\s*:\s*Page/);
    if (classMatch) {
      pages.push({ pageType: classMatch[1], section: currentSection });
    }
  }

  return pages;
}

export function parseNavigationService(content) {
  const lines = content.split(/\r?\n/);
  const pages = [];
  let currentGroup = 'Uncategorized';

  for (const line of lines) {
    const groupMatch = line.match(/^\s*\/\/\s+(.+?)$/);
    if (groupMatch) {
      currentGroup = groupMatch[1].trim();
      continue;
    }

    const registerMatch = line.match(/RegisterPage\("([^"]+)",\s*typeof\((\w+)\)\);/);
    if (registerMatch) {
      pages.push({
        tag: registerMatch[1],
        pageType: registerMatch[2],
        group: currentGroup,
      });
    }
  }

  return pages;
}

export function parseShellPageDescriptors(content) {
  const pages = [];
  const pagePattern = /(?:ShellPageRegistryBuilder\.)?Page<([\w.]+)>\(([\s\S]*?)\)/g;

  for (const match of content.matchAll(pagePattern)) {
    const pageType = normalizeTypeName(match[1]);
    const args = match[2];
    const stringArgs = [...args.matchAll(/"((?:\\.|[^"\\])*)"/g)].map(([, value]) => value);
    if (stringArgs.length < 5) {
      continue;
    }

    const orderMatch = args.match(/,\s*(\d+)\s*,\s*ShellNavigationVisibilityTier\.(\w+)/);
    pages.push({
      pageType,
      tag: stringArgs[0],
      title: stringArgs[1],
      subtitle: stringArgs[2],
      workspaceId: stringArgs[3],
      section: stringArgs[4],
      order: orderMatch ? Number.parseInt(orderMatch[1], 10) : pages.length,
      visibilityTier: orderMatch?.[2] ?? 'Unspecified',
    });
  }

  const seen = new Set();
  return pages
    .filter((page) => {
      const key = page.tag.toLowerCase();
      if (seen.has(key)) {
        return false;
      }

      seen.add(key);
      return true;
    })
    .sort((left, right) => {
      const leftWorkspace = workspaceOrder.get(left.workspaceId.toLowerCase()) ?? Number.MAX_SAFE_INTEGER;
      const rightWorkspace = workspaceOrder.get(right.workspaceId.toLowerCase()) ?? Number.MAX_SAFE_INTEGER;
      return leftWorkspace - rightWorkspace
        || left.workspaceId.localeCompare(right.workspaceId)
        || left.visibilityTier.localeCompare(right.visibilityTier)
        || left.order - right.order
        || left.title.localeCompare(right.title);
    });
}

export function parseMainPageXaml(content) {
  const lines = content.split(/\r?\n/);
  const workspacePages = [];
  let currentWorkspace = 'Shell';
  let currentTag = null;
  let currentLabel = null;

  for (const line of lines) {
    const workspaceMatch = line.match(/<!--\s+═══\s+(.+?)\s+Workspace\s+═══\s+-->/);
    if (workspaceMatch) {
      currentWorkspace = displayName(workspaceMatch[1].trim());
      continue;
    }

    const tagMatch = line.match(/<ListBoxItem\s+Tag="([^"]+)"/);
    if (tagMatch) {
      currentTag = tagMatch[1];
      currentLabel = null;
      continue;
    }

    if (!currentTag) {
      continue;
    }

    const labelMatch = line.match(/Text="([^"]+)"\s+Style="\{StaticResource NavLabelStyle\}"/);
    if (labelMatch) {
      currentLabel = labelMatch[1];
    }

    if (line.includes('</ListBoxItem>')) {
      workspacePages.push({
        workspace: currentWorkspace,
        tag: currentTag,
        label: currentLabel ?? currentTag,
      });
      currentTag = null;
      currentLabel = null;
    }
  }

  return workspacePages;
}

export function parseInjectedMembers(content) {
  const fieldMatches = [...content.matchAll(/private\s+readonly\s+([\w\.]+)\s+_(\w+);/g)];
  return fieldMatches.map(([, typeName, variable]) => ({
    typeName: normalizeTypeName(typeName),
    variable,
  }));
}

export function parseTransientPages(content) {
  return [...content.matchAll(/services\.AddTransient<([^>]+)>\(\);/g)].map(([, typeName]) => normalizeTypeName(typeName));
}

function createFingerprint(inputs) {
  const hash = crypto.createHash('sha256');
  for (const value of inputs) {
    hash.update(value);
    hash.update('\n---\n');
  }
  return hash.digest('hex').slice(0, 12);
}

function createAutogenHeader() {
  return [
    '// -----------------------------------------------------------------------------',
    '// <auto-generated>',
    '// Generated by scripts/generate-diagrams.mjs.',
    '// Do not edit this file directly; update the WPF source files instead.',
    '// -----------------------------------------------------------------------------',
    '',
  ].join('\n');
}

export function buildUiNavigationDot({ fingerprint, navigationPages, workspacePages }) {
  const workspaceMap = new Map();
  for (const item of workspacePages) {
    if (!workspaceMap.has(item.workspace)) {
      workspaceMap.set(item.workspace, []);
    }
    workspaceMap.get(item.workspace).push(item);
  }

  const navByTag = new Map(navigationPages.map((item) => [item.tag, item]));
  const registeredTags = new Set(navigationPages.map((item) => item.tag));
  const shellTags = new Set(workspacePages.map((item) => item.tag));

  const hiddenPages = navigationPages.filter((item) => !shellTags.has(item.tag));
  const shellOnlyTags = workspacePages.filter((item) => !registeredTags.has(item.tag));

  const lines = [
    'digraph UiNavigationMap {',
    '  graph [',
    '    rankdir=LR,',
    '    labelloc=t,',
    '    label="Meridian WPF UI navigation map\\n(auto-generated from ShellPageRegistryBuilder + feature modules)",',
    '    fontname="Segoe UI",',
    '    fontsize=20,',
    '    pad=0.4,',
    '    nodesep=0.35,',
    '    ranksep=1.0,',
    '    bgcolor="#0f172a"',
    '  ];',
    '  node [fontname="Segoe UI", shape=box, style="rounded,filled", color="#475569", fontcolor="#e2e8f0", fillcolor="#1e293b"];',
    '  edge [fontname="Segoe UI", color="#94a3b8", penwidth=1.3, arrowsize=0.8];',
    '',
    `  generated [shape=note, fillcolor="#1d4ed8", color="#60a5fa", label="Source fingerprint: ${escapeLabel(fingerprint)}\\nWorkspaces: ${workspaceMap.size}\\nRegistry pages: ${navigationPages.length}"];`,
    '  shell [shape=component, fillcolor="#0f766e", color="#2dd4bf", label="ShellNavigationCatalog\\nWorkspace registry"];',
    '  navigation [shape=component, fillcolor="#7c3aed", color="#c4b5fd", label="NavigationService\\nBuildDefault registry"];',
    '  shell -> navigation [label="resolve canonical page tags", color="#38bdf8"];',
    '',
  ];

  let workspaceIndex = 0;
  for (const [workspace, items] of workspaceMap.entries()) {
    workspaceIndex += 1;
    const workspaceId = `workspace_${workspaceIndex}`;
    lines.push(`  subgraph cluster_${workspaceId} {`);
    lines.push(`    label="${escapeLabel(workspace)} workspace";`);
    lines.push('    color="#334155";');
    lines.push('    style="rounded";');
    lines.push(`    ${workspaceId} [shape=folder, fillcolor="#1d4ed8", color="#93c5fd", label="${escapeLabel(workspace)}\\n${items.length} shell entries"];`);
    lines.push(`    shell -> ${workspaceId} [color="#22d3ee"];`);

    for (const item of items) {
      const nodeId = `page_${item.tag}`;
      const nav = navByTag.get(item.tag);
      const pageType = nav?.pageType ?? 'Missing registration';
      lines.push(`    ${nodeId} [label="${escapeLabel(item.label)}\\n${escapeLabel(item.tag)} → ${escapeLabel(pageType)}", fillcolor="#1e293b"];`);
      lines.push(`    ${workspaceId} -> ${nodeId};`);
    }

    lines.push('  }');
    lines.push('');
  }

  if (hiddenPages.length > 0) {
    lines.push('  subgraph cluster_hidden_pages {');
    lines.push('    label="Registered but not in sidebar shell";');
    lines.push('    color="#7c2d12";');
    lines.push('    style="rounded,dashed";');
    for (const item of hiddenPages) {
      const nodeId = `hidden_${item.tag}`;
      lines.push(`    ${nodeId} [label="${escapeLabel(item.tag)}\\n${escapeLabel(item.pageType)}", fillcolor="#431407", color="#fb923c"];`);
      lines.push(`    navigation -> ${nodeId} [style=dashed, color="#fb923c"];`);
    }
    lines.push('  }');
    lines.push('');
  }

  if (shellOnlyTags.length > 0) {
    lines.push('  subgraph cluster_shell_only {');
    lines.push('    label="Shell entries missing registrations";');
    lines.push('    color="#991b1b";');
    lines.push('    style="rounded,dashed";');
    for (const item of shellOnlyTags) {
      const nodeId = `shell_only_${item.tag}`;
      lines.push(`    ${nodeId} [label="${escapeLabel(item.label)}\\n${escapeLabel(item.tag)}", fillcolor="#450a0a", color="#fca5a5"];`);
      lines.push(`    shell -> ${nodeId} [style=dashed, color="#fca5a5"];`);
    }
    lines.push('  }');
    lines.push('');
  }

  lines.push('  { rank=min; generated; shell; navigation; }');
  lines.push('}');
  return `${createAutogenHeader()}${lines.join('\n')}\n`;
}

export function buildUiImplementationDot({ fingerprint, appPages, pagesBySection, navigationPages, mainWindowDeps, mainPageDeps }) {
  const uniqueAppPages = new Set(appPages);
  const sectionMap = new Map();
  for (const page of pagesBySection) {
    if (!sectionMap.has(page.section)) {
      sectionMap.set(page.section, []);
    }
    sectionMap.get(page.section).push(page.pageType);
  }

  const lines = [
    'digraph UiImplementationFlow {',
    '  graph [',
    '    rankdir=TB,',
    '    labelloc=t,',
    '    label="Meridian WPF UI implementation flow\\n(auto-generated from App + ShellPageRegistryBuilder source)",',
    '    fontname="Segoe UI",',
    '    fontsize=20,',
    '    pad=0.4,',
    '    nodesep=0.3,',
    '    ranksep=0.65,',
    '    bgcolor="#0f172a"',
    '  ];',
    '  node [fontname="Segoe UI", shape=box, style="rounded,filled", color="#475569", fontcolor="#e2e8f0", fillcolor="#1e293b"];',
    '  edge [fontname="Segoe UI", color="#94a3b8", penwidth=1.3, arrowsize=0.8];',
    '',
    `  generated [shape=note, fillcolor="#1d4ed8", color="#60a5fa", label="Source fingerprint: ${escapeLabel(fingerprint)}\\nTransient pages in App.xaml.cs: ${uniqueAppPages.size}\\nRegistry tags: ${navigationPages.length}"];`,
    '  app [shape=component, fillcolor="#0f766e", color="#2dd4bf", label="App.xaml.cs\\nHost + DI composition"];',
    '  window [shape=component, fillcolor="#2563eb", color="#93c5fd", label="MainWindow.xaml(.cs)\\nRoot frame + global shortcuts"];',
    '  page [shape=component, fillcolor="#7c3aed", color="#c4b5fd", label="MainPage.xaml(.cs)\\nSidebar shell + command palette"];',
    '  nav [shape=component, fillcolor="#9333ea", color="#d8b4fe", label="NavigationService.cs\\nBuildDefault registry + frame navigation"];',
    '  views [shape=component, fillcolor="#1d4ed8", color="#60a5fa", label="ShellPageRegistryBuilder\\nPage descriptors"];',
    '  frame [shape=tab, fillcolor="#0f766e", color="#5eead4", label="ContentFrame / RootFrame"];',
    '  app -> window [label="resolve MainWindow from DI"];',
    '  window -> page [label="RootFrame.Navigate(MainPage)"];',
    '  page -> nav [label="navigate by tag"];',
    '  nav -> frame [label="Frame.Navigate(page)"];',
    '  nav -> views [label="page type lookup"];',
    '',
  ];

  const mainWindowLabel = mainWindowDeps.map((item) => item.typeName).join('\\n');
  const mainPageLabel = mainPageDeps.map((item) => item.typeName).join('\\n');
  lines.push(`  main_window_services [fillcolor="#172554", color="#93c5fd", label="MainWindow dependencies\\n${escapeLabel(mainWindowLabel)}"];`);
  lines.push(`  main_page_services [fillcolor="#3b0764", color="#d8b4fe", label="MainPage dependencies\\n${escapeLabel(mainPageLabel)}"];`);
  lines.push('  window -> main_window_services [label="constructor injection"];');
  lines.push('  page -> main_page_services [label="constructor injection"];');
  lines.push('');

  let sectionIndex = 0;
  for (const [section, pageTypes] of sectionMap.entries()) {
    sectionIndex += 1;
    const clusterId = `section_${sectionIndex}`;
    const registeredCount = pageTypes.filter((pageType) => uniqueAppPages.has(pageType)).length;
    lines.push(`  subgraph cluster_${clusterId} {`);
    lines.push(`    label="${escapeLabel(section)}";`);
    lines.push('    color="#334155";');
    lines.push('    style="rounded";');
    lines.push(`    ${clusterId} [shape=folder, fillcolor="#0f172a", color="#64748b", label="${escapeLabel(section)}\\n${pageTypes.length} page classes\\n${registeredCount} transient registrations"];`);
    lines.push(`    views -> ${clusterId};`);

    for (const pageType of pageTypes.slice(0, 8)) {
      const nodeId = `${clusterId}_${pageType}`;
      const registered = uniqueAppPages.has(pageType) ? 'DI transient' : 'Not in App DI';
      lines.push(`    ${nodeId} [label="${escapeLabel(displayName(pageType.replace(/Page$/, '')))}\\n${escapeLabel(pageType)}\\n${registered}", fillcolor="#111827"];`);
      lines.push(`    ${clusterId} -> ${nodeId};`);
    }

    if (pageTypes.length > 8) {
      const remaining = pageTypes.length - 8;
      const moreNodeId = `${clusterId}_more`;
      lines.push(`    ${moreNodeId} [shape=note, fillcolor="#0f172a", color="#64748b", label="+ ${remaining} more page classes"];`);
      lines.push(`    ${clusterId} -> ${moreNodeId};`);
    }

    lines.push('  }');
    lines.push('');
  }

  lines.push('  { rank=min; generated; app; }');
  lines.push('}');
  return `${createAutogenHeader()}${lines.join('\n')}\n`;
}

function groupRegistryPagesByWorkspace(registryPages) {
  const workspaceMap = new Map();
  for (const page of registryPages) {
    const workspaceName = workspaceDisplayName(page.workspaceId);
    if (!workspaceMap.has(workspaceName)) {
      workspaceMap.set(workspaceName, []);
    }

    workspaceMap.get(workspaceName).push(page);
  }

  return workspaceMap;
}

function countByVisibility(pages, visibilityTier) {
  return pages.filter((page) => page.visibilityTier === visibilityTier).length;
}

export function buildWpfScreenSummaryDot({ fingerprint, registryPages }) {
  const workspaceMap = groupRegistryPagesByWorkspace(registryPages);
  const lines = [
    'digraph WpfScreenSummary {',
    '  graph [',
    '    rankdir=LR,',
    '    labelloc=t,',
    '    label="Meridian WPF screen summary\\n(auto-generated from ShellPageRegistryBuilder + feature modules)",',
    '    fontname="Segoe UI",',
    '    fontsize=20,',
    '    pad=0.4,',
    '    nodesep=0.35,',
    '    ranksep=0.85,',
    '    bgcolor="#0f172a"',
    '  ];',
    '  node [fontname="Segoe UI", shape=box, style="rounded,filled", color="#475569", fontcolor="#e2e8f0", fillcolor="#1e293b"];',
    '  edge [fontname="Segoe UI", color="#94a3b8", penwidth=1.3, arrowsize=0.8];',
    '',
    `  generated [shape=note, fillcolor="#1d4ed8", color="#60a5fa", label="Source fingerprint: ${escapeLabel(fingerprint)}\\nWorkspaces: ${workspaceMap.size}\\nScreens: ${registryPages.length}"];`,
    '  shell [shape=component, fillcolor="#0f766e", color="#2dd4bf", label="WPF Shell\\nMainWindow + MainPage"];',
    '  catalog [shape=component, fillcolor="#7c3aed", color="#c4b5fd", label="ShellNavigationCatalog\\nworkspace + screen descriptors"];',
    '  shell -> catalog [label="loads screen registry", color="#38bdf8"];',
    '',
  ];

  let workspaceIndex = 0;
  for (const [workspaceName, pages] of workspaceMap.entries()) {
    workspaceIndex += 1;
    const sectionCount = new Set(pages.map((page) => page.section)).size;
    const launchpad = pages.find((page) => page.order === 0) ?? pages[0];
    const nodeId = `workspace_${workspaceIndex}`;
    lines.push(`  ${nodeId} [shape=folder, fillcolor="#1d4ed8", color="#93c5fd", label="${escapeLabel(workspaceName)}\\n${pages.length} screens / ${sectionCount} sections\\nPrimary ${countByVisibility(pages, 'Primary')} | Secondary ${countByVisibility(pages, 'Secondary')} | Overflow ${countByVisibility(pages, 'Overflow')}\\nLaunchpad: ${escapeLabel(launchpad.title)} → ${escapeLabel(launchpad.pageType)}"];`);
    lines.push(`  catalog -> ${nodeId};`);
  }

  lines.push('');
  lines.push('  { rank=min; generated; shell; catalog; }');
  lines.push('}');
  return `${createAutogenHeader()}${lines.join('\n')}\n`;
}

export function buildWpfScreenCatalogDot({ fingerprint, registryPages }) {
  const workspaceMap = groupRegistryPagesByWorkspace(registryPages);
  const lines = [
    'digraph WpfScreenCatalog {',
    '  graph [',
    '    rankdir=TB,',
    '    labelloc=t,',
    '    label="Meridian WPF screen catalog\\n(workspaces, sections, route tags, and page classes from live shell registry)",',
    '    fontname="Segoe UI",',
    '    fontsize=20,',
    '    pad=0.4,',
    '    nodesep=0.25,',
    '    ranksep=0.55,',
    '    bgcolor="#0f172a"',
    '  ];',
    '  node [fontname="Segoe UI", shape=box, style="rounded,filled", color="#475569", fontcolor="#e2e8f0", fillcolor="#1e293b"];',
    '  edge [fontname="Segoe UI", color="#94a3b8", penwidth=1.1, arrowsize=0.7];',
    '',
    `  generated [shape=note, fillcolor="#1d4ed8", color="#60a5fa", label="Source fingerprint: ${escapeLabel(fingerprint)}\\nScreens: ${registryPages.length}\\nSource: src/Meridian.Wpf shell registry"];`,
    '  registry [shape=component, fillcolor="#0f766e", color="#2dd4bf", label="ShellPageRegistryBuilder\\nfeature modules + catalog partials"];',
    '  generated -> registry [color="#38bdf8"];',
    '',
  ];

  let workspaceIndex = 0;
  for (const [workspaceName, pages] of workspaceMap.entries()) {
    workspaceIndex += 1;
    const workspaceId = `workspace_${workspaceIndex}`;
    const sectionMap = new Map();
    for (const page of pages) {
      if (!sectionMap.has(page.section)) {
        sectionMap.set(page.section, []);
      }

      sectionMap.get(page.section).push(page);
    }

    lines.push(`  subgraph cluster_${workspaceId} {`);
    lines.push(`    label="${escapeLabel(workspaceName)} workspace";`);
    lines.push('    color="#334155";');
    lines.push('    style="rounded";');
    lines.push(`    ${workspaceId} [shape=folder, fillcolor="#1d4ed8", color="#93c5fd", label="${escapeLabel(workspaceName)}\\n${pages.length} screens\\n${sectionMap.size} sections"];`);
    lines.push(`    registry -> ${workspaceId} [color="#22d3ee"];`);

    let sectionIndex = 0;
    for (const [sectionName, sectionPages] of sectionMap.entries()) {
      sectionIndex += 1;
      const sectionId = `${workspaceId}_section_${sectionIndex}`;
      lines.push(`    ${sectionId} [shape=folder, fillcolor="#172554", color="#93c5fd", label="${escapeLabel(sectionName)}\\n${sectionPages.length} screens"];`);
      lines.push(`    ${workspaceId} -> ${sectionId};`);

      for (const page of sectionPages) {
        const nodeId = `${sectionId}_${page.tag}`;
        const fill = page.visibilityTier === 'Primary'
          ? '#1e293b'
          : page.visibilityTier === 'Secondary'
            ? '#312e81'
            : '#431407';
        lines.push(`    ${nodeId} [fillcolor="${fill}", label="${escapeLabel(page.title)}\\n${escapeLabel(page.tag)} → ${escapeLabel(page.pageType)}\\n${escapeLabel(page.visibilityTier)} | order ${page.order}"];`);
        lines.push(`    ${sectionId} -> ${nodeId};`);
      }
    }

    lines.push('  }');
    lines.push('');
  }

  lines.push('  { rank=min; generated; registry; }');
  lines.push('}');
  return `${createAutogenHeader()}${lines.join('\n')}\n`;
}

export function buildWpfWorkspaceScreenDot({ fingerprint, workspaceName, pages }) {
  const sectionMap = new Map();
  for (const page of pages) {
    if (!sectionMap.has(page.section)) {
      sectionMap.set(page.section, []);
    }

    sectionMap.get(page.section).push(page);
  }

  const lines = [
    `digraph Wpf${workspaceName.replace(/\s+/g, '')}Screens {`,
    '  graph [',
    '    rankdir=LR,',
    '    labelloc=t,',
    `    label="Meridian WPF ${escapeLabel(workspaceName)} screens\\n(auto-generated from live shell registry)",`,
    '    fontname="Segoe UI",',
    '    fontsize=20,',
    '    pad=0.4,',
    '    nodesep=0.35,',
    '    ranksep=0.85,',
    '    bgcolor="#0f172a"',
    '  ];',
    '  node [fontname="Segoe UI", shape=box, style="rounded,filled", color="#475569", fontcolor="#e2e8f0", fillcolor="#1e293b"];',
    '  edge [fontname="Segoe UI", color="#94a3b8", penwidth=1.2, arrowsize=0.75];',
    '',
    `  generated [shape=note, fillcolor="#1d4ed8", color="#60a5fa", label="Source fingerprint: ${escapeLabel(fingerprint)}\\n${escapeLabel(workspaceName)} screens: ${pages.length}\\nSections: ${sectionMap.size}"];`,
    `  workspace [shape=folder, fillcolor="#0f766e", color="#2dd4bf", label="${escapeLabel(workspaceName)} workspace\\nPrimary ${countByVisibility(pages, 'Primary')} | Secondary ${countByVisibility(pages, 'Secondary')} | Overflow ${countByVisibility(pages, 'Overflow')}"];`,
    '  generated -> workspace [color="#38bdf8"];',
    '',
  ];

  let sectionIndex = 0;
  for (const [sectionName, sectionPages] of sectionMap.entries()) {
    sectionIndex += 1;
    const sectionId = `section_${sectionIndex}`;
    lines.push(`  subgraph cluster_${sectionId} {`);
    lines.push(`    label="${escapeLabel(sectionName)}";`);
    lines.push('    color="#334155";');
    lines.push('    style="rounded";');
    lines.push(`    ${sectionId} [shape=folder, fillcolor="#172554", color="#93c5fd", label="${escapeLabel(sectionName)}\\n${sectionPages.length} screens"];`);
    lines.push(`    workspace -> ${sectionId};`);

    for (const page of sectionPages) {
      const fill = page.visibilityTier === 'Primary'
        ? '#1e293b'
        : page.visibilityTier === 'Secondary'
          ? '#312e81'
          : '#431407';
      lines.push(`    screen_${page.tag} [fillcolor="${fill}", label="${escapeLabel(page.title)}\\n${escapeLabel(page.tag)} → ${escapeLabel(page.pageType)}\\n${escapeLabel(page.visibilityTier)} | order ${page.order}"];`);
      lines.push(`    ${sectionId} -> screen_${page.tag};`);
    }

    lines.push('  }');
    lines.push('');
  }

  lines.push('  { rank=min; generated; workspace; }');
  lines.push('}');
  return `${createAutogenHeader()}${lines.join('\n')}\n`;
}

export async function readUiDiagramInputs(repoRoot) {
  const paths = {
    app: path.join(repoRoot, 'src', 'Meridian.Wpf', 'App.xaml.cs'),
    mainWindow: path.join(repoRoot, 'src', 'Meridian.Wpf', 'MainWindow.xaml.cs'),
    mainPageXaml: path.join(repoRoot, 'src', 'Meridian.Wpf', 'Views', 'MainPage.xaml'),
    mainPageCodeBehind: path.join(repoRoot, 'src', 'Meridian.Wpf', 'Views', 'MainPage.xaml.cs'),
    navigationService: path.join(repoRoot, 'src', 'Meridian.Wpf', 'Services', 'NavigationService.cs'),
    pages: path.join(repoRoot, 'src', 'Meridian.Wpf', 'Views', 'Pages.cs'),
  };
  const registryPaths = [
    ...(await listFilesRecursive(path.join(repoRoot, 'src', 'Meridian.Wpf', 'Features'), (filePath) => filePath.endsWith('.cs'))),
    ...(await listFilesRecursive(path.join(repoRoot, 'src', 'Meridian.Wpf', 'Models'), (filePath) => path.basename(filePath).startsWith('ShellNavigationCatalog') && filePath.endsWith('.cs'))),
    path.join(repoRoot, 'src', 'Meridian.Wpf', 'Shell', 'Services', 'ShellPageRegistryBuilder.cs'),
  ].sort((left, right) => left.localeCompare(right));

  const entries = await Promise.all(
    Object.entries(paths).map(async ([key, filePath]) => [key, await fs.readFile(filePath, 'utf8')]),
  );
  const registryEntries = await Promise.all(
    registryPaths.map(async (filePath) => {
      const relativePath = path.relative(repoRoot, filePath).replaceAll(path.sep, '/');
      return `// ${relativePath}\n${await fs.readFile(filePath, 'utf8')}`;
    }),
  );

  return {
    ...Object.fromEntries(entries),
    registrySources: registryEntries.join('\n\n'),
  };
}

export async function buildUiDiagramOutputs(repoRoot) {
  const contents = await readUiDiagramInputs(repoRoot);
  const fingerprint = createFingerprint(Object.values(contents));

  const registryPages = parseShellPageDescriptors(contents.registrySources);
  const pagesBySection = registryPages.map((page) => ({
    section: `${workspaceDisplayName(page.workspaceId)} / ${page.section}`,
    pageType: page.pageType,
  }));
  const navigationPages = registryPages.map((page) => ({
    group: `${workspaceDisplayName(page.workspaceId)} / ${page.section}`,
    tag: page.tag,
    pageType: page.pageType,
  }));
  const workspacePages = registryPages.map((page) => ({
    workspace: workspaceDisplayName(page.workspaceId),
    tag: page.tag,
    label: page.title,
  }));
  const appPages = parseTransientPages(contents.app);
  const mainWindowDeps = parseInjectedMembers(contents.mainWindow);
  const mainPageDeps = parseInjectedMembers(contents.mainPageCodeBehind);
  const screenDiagramFiles = new Map();
  for (const [workspaceName, pages] of groupRegistryPagesByWorkspace(registryPages).entries()) {
    const workspaceSlug = workspaceName.toLowerCase().replace(/[^a-z0-9]+/g, '-');
    screenDiagramFiles.set(
      `ui-wpf-screens-${workspaceSlug}.dot`,
      buildWpfWorkspaceScreenDot({ fingerprint, workspaceName, pages }),
    );
  }

  return {
    fingerprint,
    files: new Map([
      ['ui-navigation-map.dot', buildUiNavigationDot({ fingerprint, navigationPages, workspacePages })],
      ['ui-implementation-flow.dot', buildUiImplementationDot({ fingerprint, appPages, pagesBySection, navigationPages, mainWindowDeps, mainPageDeps })],
      ['ui-wpf-screen-summary.dot', buildWpfScreenSummaryDot({ fingerprint, registryPages })],
      ['ui-wpf-screen-catalog.dot', buildWpfScreenCatalogDot({ fingerprint, registryPages })],
      ...screenDiagramFiles,
    ]),
  };
}

async function listFilesRecursive(rootDir, predicate) {
  const entries = await fs.readdir(rootDir, { withFileTypes: true });
  const files = [];
  for (const entry of entries) {
    const filePath = path.join(rootDir, entry.name);
    if (entry.isDirectory()) {
      files.push(...await listFilesRecursive(filePath, predicate));
      continue;
    }

    if (entry.isFile() && predicate(filePath)) {
      files.push(filePath);
    }
  }

  return files;
}

async function writeFileIfChanged(filePath, contents) {
  let existing = null;
  try {
    existing = await fs.readFile(filePath);
  } catch {
    // ignore missing file
  }

  const nextBuffer = Buffer.isBuffer(contents) ? contents : Buffer.from(contents);
  if (existing && Buffer.compare(existing, nextBuffer) === 0) {
    return false;
  }

  await fs.writeFile(filePath, nextBuffer);
  return true;
}

export async function renderDotFile(viz, dotPath) {
  const dotSource = await fs.readFile(dotPath, 'utf8');
  const renderResult = viz.renderString(dotSource, { format: 'svg', engine: 'dot' });
  const svgOutput = typeof renderResult === 'string' ? renderResult : renderResult.output;

  if (!svgOutput) {
    const details = typeof renderResult === 'object' && renderResult?.errors
      ? renderResult.errors.join('\n')
      : 'Unknown render failure';
    throw new Error(`Failed to render ${path.basename(dotPath)}: ${details}`);
  }

  const svgPath = dotPath.replace(/\.dot$/, '.svg');
  const svgChanged = await writeFileIfChanged(svgPath, svgOutput);

  return {
    dotPath,
    svgPath,
    svgChanged,
  };
}

export async function generateUiDiagrams({ repoRoot, renderAll = false }) {
  const docsDiagramsDir = path.join(repoRoot, 'docs', 'diagrams');
  const uiDiagramsDir = path.join(docsDiagramsDir, 'ui');
  const { files } = await buildUiDiagramOutputs(repoRoot);
  await fs.mkdir(uiDiagramsDir, { recursive: true });

  for (const [fileName, contents] of files) {
    await writeFileIfChanged(path.join(docsDiagramsDir, fileName), contents);
    await writeFileIfChanged(path.join(uiDiagramsDir, fileName), contents);
  }

  const viz = await instance();
  const rootDotPaths = (await fs.readdir(docsDiagramsDir))
    .filter((fileName) => fileName.endsWith('.dot'))
    .filter((fileName) => renderAll || fileName.startsWith('ui-'))
    .map((fileName) => path.join(docsDiagramsDir, fileName));
  const uiDotPaths = [...files.keys()].map((fileName) => path.join(uiDiagramsDir, fileName));
  const dotPaths = [...rootDotPaths, ...uiDotPaths].sort((left, right) => left.localeCompare(right));

  const rendered = [];
  for (const dotPath of dotPaths) {
    rendered.push(await renderDotFile(viz, dotPath));
  }

  return rendered;
}
