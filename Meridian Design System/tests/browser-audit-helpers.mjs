// Helpers shared by the library audit and its rendered negative regressions.
// A canceled stylesheet/script is still a missing resource; package references
// do not perform the application's intentionally canceled API journeys.
export function failedAssetRequest(request, base = '') {
  return {
    url: request.url().replace(base, ''),
    error: request.failure()?.errorText ?? 'Network request failed',
    resourceType: request.resourceType(),
  };
}

export function monitorResourceFailures(page, failures, base = '') {
  page.on('requestfailed', request => failures.push(failedAssetRequest(request, base)));
  page.on('response', response => {
    if (response.status() >= 400) failures.push({ url: response.url().replace(base, ''), status: response.status() });
  });
}

export async function auditVisibleFrames(page, axeSource) {
  const frames = [];
  for (const frame of page.frames()) {
    // Hidden documents are outside the accessibility tree. Audit the real
    // rendered children individually, including srcdoc and nested previews.
    if (frame !== page.mainFrame() && !await (await frame.frameElement()).isVisible()) continue;
    await frame.addScriptTag({ content: axeSource });
    const result = await frame.evaluate(async () => {
      // The authored styles have loaded. Axe's optional stylesheet preloader
      // clones CSSOM imports against the document URL and can invent 404s.
      // Disabling that preloader does not disable any selected WCAG rules.
      const audit = await window.axe.run(document, {
        preload: false, iframes: false,
        runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'] },
      });
      return {
        passes: audit.passes.length,
        incomplete: audit.incomplete.map(item => item.id),
        violations: audit.violations.map(item => ({
          id: item.id, impact: item.impact,
          nodes: item.nodes.map(node => ({ target: node.target, summary: node.failureSummary })),
        })),
      };
    });
    frames.push({ url: frame.url(), ...result });
  }
  return {
    passes: frames.reduce((sum, frame) => sum + frame.passes, 0),
    incomplete: [...new Set(frames.flatMap(frame => frame.incomplete))],
    violations: frames.flatMap((frame, index) => frame.violations.map(item => ({ ...item, frame: index, url: frame.url }))),
    frames,
  };
}
