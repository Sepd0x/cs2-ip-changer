// 26 nov. 2026: os testes da auditoria do site (S1, S5, S7). Correm sobre cópias em pastas temporárias.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, writeFileSync, cpSync, mkdtempSync, readdirSync, existsSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { spawnSync } from 'node:child_process';
import { pathToFileURL, fileURLToPath } from 'node:url';

const ROOT = fileURLToPath(new URL('../', import.meta.url));
const SITE = join(ROOT, 'site');
const { build } = await import(pathToFileURL(join(ROOT, 'tools/site-i18n.mjs')).href);

function copySite() {
  const dir = mkdtempSync(join(tmpdir(), 'audit-site-'));
  cpSync(SITE, join(dir, 'site'), { recursive: true });
  cpSync(join(ROOT, 'README.md'), join(dir, 'README.md'));
  return dir;
}

test('S1: uma versão estável (v1.0.0) não deixa textos por traduzir', () => {
  const dir = copySite();
  const rel = JSON.parse(readFileSync(join(dir, 'site/release.json'), 'utf8'));
  Object.assign(rel, { tag: 'v1.0.0', version: '1.0.0', published: '2026-11-27' });
  writeFileSync(join(dir, 'site/release.json'), JSON.stringify(rel, null, 1) + '\n');
  const r = spawnSync(process.execPath, [join(ROOT, 'tools/site-pages.mjs'), '--root', dir], { encoding: 'utf8' });
  assert.equal(r.status, 0, r.stderr);
  const { report } = build({ site: join(dir, 'site') });
  const keys = [...new Set(report.missing.map((m) => m.key))];
  assert.deepEqual(keys, [], 'por traduzir depois de --release v1.0.0:\n' + keys.join('\n'));
});

test('S5: os quadrados do VirusTotal leem o número em todas as línguas', () => {
  const src = readFileSync(join(SITE, 'assets/js/readout.js'), 'utf8');
  assert.match(src, /vn\.textContent\.match\(\/\\d\+\/g\)/, 'readout.js lê os dois números do texto, em qualquer ordem');
  const { virustotal: vt } = JSON.parse(readFileSync(join(SITE, 'release.json'), 'utf8'));
  for (const lang of ['', 'pt-br/', 'es/', 'ru/', 'tr/']) {
    const html = readFileSync(join(SITE, lang + 'index.html'), 'utf8');
    const text = /id="vt-rd"><!--rel:vt-->([^<]*)<!--\/rel-->/.exec(html)[1];
    // o que o readout.js faz: os dois números, e os motores são o maior
    const nums = (text.match(/\d+/g) || []).map(Number);
    assert.equal(nums.length, 2, `${lang || 'en/'}: "${text}" não dá quadrados`);
    assert.deepEqual([Math.min(...nums), Math.max(...nums)], [vt.flagged, vt.engines], `${lang || 'en/'}: "${text}"`);
  }
});

// S6 (the site says beta next to a stable version) is left for the release: whether 1.0.0 leaves the beta is the owner's call.

test('S7: o menu do telemóvel só procura na página as ligações #id (as das línguas não são seletores)', () => {
  const src = readFileSync(join(SITE, 'assets/js/site-navigation.js'), 'utf8');
  assert.doesNotMatch(src, /document\.querySelector\(link\.getAttribute\('href'\)\)/);
  assert.match(src, /href\.charAt\(0\) === '#' \? document\.getElementById\(href\.slice\(1\)\) : null/);
  // o menu tem mesmo ligações que não são #id (as das línguas): sem a guarda, querySelector lançava SyntaxError
  const html = readFileSync(join(SITE, 'index.html'), 'utf8');
  const i = html.indexOf('<details class="site-menu'), j = html.indexOf('</details>', html.indexOf('menu-langs', i));
  assert.ok([...html.slice(i, j).matchAll(/href="([^"]*)"/g)].some((m) => !m[1].startsWith('#')));
});
