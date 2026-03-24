#!/usr/bin/env node
/**
 * Generate PNG images from Graphviz DOT source files
 * Uses @viz-js/viz for DOT rendering and sharp for PNG conversion
 */

import { instance } from '@viz-js/viz';
import sharp from 'sharp';
import { readdir, readFile, writeFile } from 'fs/promises';
import { join, basename, dirname } from 'path';
import { fileURLToPath } from 'url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const projectRoot = join(__dirname, '..', '..');
const DIAGRAMS_DIR = join(projectRoot, 'docs', 'diagrams');

async function main() {
  const viz = await instance();

  // Find all DOT files
  const files = await readdir(DIAGRAMS_DIR);
  const dotFiles = files.filter(f => f.endsWith('.dot'));

  console.log(`Found ${dotFiles.length} DOT files to process`);

  for (const dotFile of dotFiles) {
    const dotPath = join(DIAGRAMS_DIR, dotFile);
    const pngFile = dotFile.replace('.dot', '.png');
    const pngPath = join(DIAGRAMS_DIR, pngFile);

    try {
      // Read DOT file
      const dotSource = await readFile(dotPath, 'utf-8');

      // Render to SVG
      const svgOutput = viz.renderString(dotSource, { format: 'svg' });

      // Write SVG file
      const svgFile = dotFile.replace('.dot', '.svg');
      const svgPath = join(DIAGRAMS_DIR, svgFile);
      await writeFile(svgPath, svgOutput);

      // Convert SVG to PNG using sharp with high DPI
      const pngBuffer = await sharp(Buffer.from(svgOutput))
        .png()
        .resize({ width: 2400, withoutEnlargement: true }) // High resolution
        .toBuffer();

      // Write PNG file
      await writeFile(pngPath, pngBuffer);

      console.log(`Generated: ${pngFile}, ${svgFile}`);
    } catch (err) {
      console.error(`Error processing ${dotFile}: ${err.message}`);
    }
  }

  console.log('\nDone generating PNG and SVG diagrams');
}

main().catch(console.error);
