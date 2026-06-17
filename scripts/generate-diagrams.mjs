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

    for (const item of rendered) {
      const suffix = item.svgChanged ? 'svg updated' : 'svg unchanged';
      console.log(`Rendered ${path.basename(item.dotPath)} -> ${path.basename(item.svgPath)} (${suffix})`);
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
