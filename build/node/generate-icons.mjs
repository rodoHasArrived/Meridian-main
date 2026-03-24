#!/usr/bin/env node
/**
 * Generate Windows desktop app icons from the source SVG when present.
 *
 * Usage: node build/node/generate-icons.mjs
 *
 * The WPF project does not currently require generated icons to compile, so
 * this script becomes a no-op when the source SVG has not been checked in yet.
 */

import sharp from 'sharp';
import { readFileSync, mkdirSync, existsSync, unlinkSync } from 'fs';
import { join, dirname } from 'path';
import { fileURLToPath } from 'url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const projectRoot = join(__dirname, '..', '..');
const assetsDir = join(projectRoot, 'src', 'Meridian.Wpf', 'Assets');
const svgPath = join(assetsDir, 'AppIcon.svg');

// Keep the existing Windows shell/store icon filenames so packaging can reuse
// them if/when the desktop app starts consuming icon assets again.
const iconSizes = [
  { name: 'Square44x44Logo.png', size: 44 },
  { name: 'Square44x44Logo.scale-100.png', size: 44 },
  { name: 'Square44x44Logo.scale-125.png', size: 55 },
  { name: 'Square44x44Logo.scale-150.png', size: 66 },
  { name: 'Square44x44Logo.scale-200.png', size: 88 },
  { name: 'Square44x44Logo.scale-400.png', size: 176 },
  { name: 'Square44x44Logo.targetsize-16.png', size: 16 },
  { name: 'Square44x44Logo.targetsize-24.png', size: 24 },
  { name: 'Square44x44Logo.targetsize-32.png', size: 32 },
  { name: 'Square44x44Logo.targetsize-48.png', size: 48 },
  { name: 'Square44x44Logo.targetsize-256.png', size: 256 },
  { name: 'Square150x150Logo.png', size: 150 },
  { name: 'Square150x150Logo.scale-100.png', size: 150 },
  { name: 'Square150x150Logo.scale-125.png', size: 188 },
  { name: 'Square150x150Logo.scale-150.png', size: 225 },
  { name: 'Square150x150Logo.scale-200.png', size: 300 },
  { name: 'Square150x150Logo.scale-400.png', size: 600 },
  { name: 'StoreLogo.png', size: 50 },
  { name: 'StoreLogo.scale-100.png', size: 50 },
  { name: 'StoreLogo.scale-125.png', size: 63 },
  { name: 'StoreLogo.scale-150.png', size: 75 },
  { name: 'StoreLogo.scale-200.png', size: 100 },
  { name: 'StoreLogo.scale-400.png', size: 200 },
  { name: 'LargeTile.scale-100.png', size: 310 },
  { name: 'LargeTile.scale-125.png', size: 388 },
  { name: 'LargeTile.scale-150.png', size: 465 },
  { name: 'LargeTile.scale-200.png', size: 620 },
  { name: 'SmallTile.scale-100.png', size: 71 },
  { name: 'SmallTile.scale-125.png', size: 89 },
  { name: 'SmallTile.scale-150.png', size: 107 },
  { name: 'SmallTile.scale-200.png', size: 142 },
  { name: 'Wide310x150Logo.scale-100.png', size: 310, height: 150 },
  { name: 'Wide310x150Logo.scale-125.png', size: 388, height: 188 },
  { name: 'Wide310x150Logo.scale-150.png', size: 465, height: 225 },
  { name: 'Wide310x150Logo.scale-200.png', size: 620, height: 300 },
  { name: 'SplashScreen.scale-100.png', size: 620, height: 300 },
  { name: 'SplashScreen.scale-125.png', size: 775, height: 375 },
  { name: 'SplashScreen.scale-150.png', size: 930, height: 450 },
  { name: 'SplashScreen.scale-200.png', size: 1240, height: 600 },
  { name: 'BadgeLogo.png', size: 24 },
  { name: 'BadgeLogo.scale-100.png', size: 24 },
  { name: 'BadgeLogo.scale-125.png', size: 30 },
  { name: 'BadgeLogo.scale-150.png', size: 36 },
  { name: 'BadgeLogo.scale-200.png', size: 48 },
  { name: 'BadgeLogo.scale-400.png', size: 96 },
];

async function generateIcons() {
  console.log('Generating Windows desktop app icons from SVG...\n');

  if (!existsSync(svgPath)) {
    console.log(`No desktop icon source found at ${svgPath}; skipping icon generation.`);
    return;
  }

  mkdirSync(assetsDir, { recursive: true });

  const svgBuffer = readFileSync(svgPath);
  let successCount = 0;
  let errorCount = 0;

  for (const icon of iconSizes) {
    const outputPath = join(assetsDir, icon.name);
    const width = icon.size;
    const height = icon.height || icon.size;

    try {
      // Remove placeholder if exists
      const placeholderPath = outputPath + '.placeholder';
      if (existsSync(placeholderPath)) {
        unlinkSync(placeholderPath);
        console.log(`  Removed placeholder: ${icon.name}.placeholder`);
      }

      await sharp(svgBuffer)
        .resize(width, height, {
          fit: 'contain',
          background: { r: 0, g: 0, b: 0, alpha: 0 }
        })
        .png()
        .toFile(outputPath);

      console.log(`  Generated: ${icon.name} (${width}x${height})`);
      successCount++;
    } catch (error) {
      console.error(`  Error generating ${icon.name}: ${error.message}`);
      errorCount++;
    }
  }

  console.log(`\nDone! Generated ${successCount} icons, ${errorCount} errors.`);

  if (errorCount > 0) {
    process.exit(1);
  }
}

generateIcons().catch(err => {
  console.error('Fatal error:', err);
  process.exit(1);
});
