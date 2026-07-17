import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { generateUiDiagrams, generateWpfScreenTracker } from './lib/ui-diagram-generator.mjs';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(__dirname, '..');

async function main() {
  const renderAll = process.argv.includes('--all');
  const trackerOnly = process.argv.includes('--tracker-only');

  if (!trackerOnly) {
    const rendered = await generateUiDiagrams({ repoRoot, renderAll });

    // Diagrams are emitted to both docs/diagrams/ and docs/diagrams/ui/, so log
    // repo-relative paths — basenames alone render the two copies identically.
    for (const item of rendered) {
      const suffix = item.svgChanged ? 'svg updated' : 'svg unchanged';
      const from = path.relative(repoRoot, item.dotPath).replaceAll(path.sep, '/');
      const to = path.relative(repoRoot, item.svgPath).replaceAll(path.sep, '/');
      console.log(`Rendered ${from} -> ${to} (${suffix})`);
    }
  }

  const tracker = await generateWpfScreenTracker({ repoRoot });
  const trackerSuffix = tracker.markdownChanged || tracker.jsonChanged ? 'updated' : 'unchanged';
  console.log(
    `Rendered ${path.basename(tracker.markdownPath)} + ${path.basename(tracker.jsonPath)} (${trackerSuffix}, ${tracker.summary.openTaskCount} open task(s))`,
  );
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
