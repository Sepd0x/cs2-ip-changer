// Beta.12 (6 nov. 2026, publish/untangle-2026-11-06): without Chrome, tools/shots.mjs left the old social card and the manifest
// said it was current. The manifest now records which Status piece the card was rendered from (tests/DesignSyncTests.cs checks it).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { socialFrom, cutShot } from '../../../tools/shots.mjs';
import { mkdtempSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';

test('shots: the social card shows the new Status only when it was just rendered', () => {
  const old = { social: { from: 'aaa' } };
  assert.equal(socialFrom(old, true, 'bbb'), 'bbb', 'rendered now: from the new Status');
  assert.equal(socialFrom(old, false, 'bbb'), 'aaa', 'no Chrome: the card still shows what it showed');
  assert.equal(socialFrom({}, false, 'bbb'), null, 'never recorded and not rendered: not known, never the new one');
});

// 26 nov.: the dashboard's Settings and Accounts came out cut at 1534 px (the screen's MaxWindowTrackSize, two monitors) and the
// run said nothing. The demo's --mapa writes <page>.left.txt (pixels that still scroll); shots.mjs stops unless it is 0.
test('shots: a dashboard capture that is not the whole page stops the run', () => {
  const dir = mkdtempSync(join(tmpdir(), 'shots-'));
  writeFileSync(join(dir, 'settings.left.txt'), '0');
  writeFileSync(join(dir, 'accounts.left.txt'), '171');
  writeFileSync(join(dir, 'log.left.txt'), 'x');
  assert.equal(cutShot(dir, 'settings'), null, 'nothing left to scroll: whole');
  assert.match(cutShot(dir, 'accounts'), /171 px/, 'still scrolls: cut');
  assert.ok(cutShot(dir, 'log'), 'not a number: not known to be whole');
  assert.match(cutShot(dir, 'regions'), /no regions\.left\.txt/, 'an old demo build that does not say: not known to be whole');
});
