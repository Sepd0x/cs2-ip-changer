// Packs worker/stats/dashboard/ (the dashboard page, its scripts, styles and the app captures) into worker/stats/src/assets.js,
// so the dashboard Worker serves them itself, each one behind the Access check. Run after changing anything in that folder:
//   node tools/dashboard-assets.mjs
import { readdirSync, readFileSync, writeFileSync, statSync } from 'node:fs';
import { join, relative, extname } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../worker/stats/dashboard/', import.meta.url));
const TYPES = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.css': 'text/css; charset=utf-8', '.png': 'image/png' };
const files = [];
const walk = (dir) => { for (const n of readdirSync(dir).sort()) { const p = join(dir, n); if (statSync(p).isDirectory()) walk(p); else if (TYPES[extname(n)]) files.push(p); } };
walk(root);
const lines = files.map((p) => {
  const rel = relative(root, p).replaceAll('\\', '/');
  const bin = extname(p) === '.png';
  const body = bin ? readFileSync(p).toString('base64') : readFileSync(p, 'utf8');
  return `  ${JSON.stringify(rel)}: { type: ${JSON.stringify(TYPES[extname(p)])}, b64: ${bin}, body: ${JSON.stringify(body)} },`;
});
writeFileSync(fileURLToPath(new URL('../worker/stats/src/assets.js', import.meta.url)),
  `// Made by tools/dashboard-assets.mjs from worker/stats/dashboard/. Don't edit: change the files there and run it again.\nexport const ASSETS = {\n${lines.join('\n')}\n};\n`);
console.log(`${files.length} files`);
