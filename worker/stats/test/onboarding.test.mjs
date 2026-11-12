// 10 nov. 2026 (app onboarding, beta.13): of 14 new users none plugged in a phone. The app now has a first-run tour, downloads ADB by
// itself in Phone setup and says once on Regions what the phone adds. These counts reach the Worker and the "Onde encalham" page.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { counterOk } from '../src/index.js';
import { summary } from '../src/api.js';
import { seed } from './v4.test.mjs';

const F0 = { version: 'all', country: 'all', mode: 'all', link: 'all' };
const NOW = Date.parse('2026-11-04T15:30:00Z');
const sum7 = async (DB) => JSON.parse(JSON.stringify(await summary(DB, { period: 7, f: F0, dev: false, now: NOW })));

test('onboarding: the Worker takes the new counts (none of them is a band family)', () => {
  for (const n of ['tour_shown', 'tour_again', 'tour_done', 'tour_setup', 'tour_skip_1', 'tour_skip_5', 'adb_auto', 'adb_ok', 'adb_fail', 'pitch_shown'])
    assert.ok(counterOk(n), n);
});

test('onboarding: the tour, the ADB download and the Regions note are counted per new install', async () => {
  const DB = seed([
    [1, 1, 'none', { first_start: 1, tour_shown: 1, tour_done: 1, tour_setup: 1, adb_auto: 1, adb_ok: 1, phone_setup_noadbtool: 1, phone_left_nousb: 1 }],
    [1, 2, 'none', { first_start: 1, tour_shown: 1, tour_skip_2: 1, pitch_shown: 1 }],
    [1, 3, 'none', { first_start: 1, adb_auto: 1, adb_fail: 1 }],
    [1, 4, 'none', { first_start: 1 }],
  ], { now: NOW, clicks: [[1, 2, 'RegionsPitchSetup_Click', 1]] });
  const o = (await sum7(DB)).stuck.onboarding;
  assert.deepEqual(o, { tour: 2, tour_done: 1, tour_setup: 1, tour_skip: [0, 1, 0, 0, 0], adb_ok: 1, adb_fail: 1, pitch: 1, pitch_setup: 1 });
});

test('onboarding: before any install sends them it says nothing (null), never 0', async () => {
  const DB = seed([[1, 1, 'none', { first_start: 1 }]], { now: NOW });
  assert.equal((await sum7(DB)).stuck.onboarding, null);
});

test('onboarding: the funnel that existed keeps counting (phone_setup_ and the Setup_ click of the ADB step)', async () => {
  const DB = seed([[1, 1, 'none', { first_start: 1, phone_setup_noadbtool: 1, phone_left_nousb: 1, adb_auto: 1, adb_ok: 1 }]],
    { now: NOW, clicks: [[1, 1, 'Setup_noadbtool', 1]] });
  const st = (await sum7(DB)).stuck.setup.find((x) => x.step === 'noadbtool');
  assert.deepEqual([st.open, st.pressed], [1, 1]);
});

test('onboarding: the Dados page shows the line only when there is something', () => {
  const app = readFileSync(new URL('../dashboard/v4/app.js', import.meta.url), 'utf8');
  assert.match(app, /\$\{S\.onboarding \? `<p class="note" id="e-onboarding">/);
});
