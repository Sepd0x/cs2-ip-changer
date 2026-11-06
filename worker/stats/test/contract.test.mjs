// The telemetry contract (contract.json): the Worker reads it, and the dashboard's labels and click map must cover it.
// The app's side is checked in tests/TelemetryTests.cs against the same file.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { counterOk, contract } from '../src/index.js';
import { LABELS } from '../src/api.js';

const here = (p) => fileURLToPath(new URL(p, import.meta.url));

test('every band in the contract is a counter the Worker takes, and nothing next to it', () => {
  for (const [fam, bands] of Object.entries(contract.bands)) {
    for (const b of bands) assert.ok(counterOk(`${fam}_${b}`), `${fam}_${b}`);
    assert.ok(!counterOk(`${fam}_made_up`), `${fam}_made_up`);
  }
});

test('the dashboard has a label for every band it draws, setup step, check and brand in the contract', () => {
  for (const [fam, rows] of Object.entries(LABELS.bands)) {
    const keys = rows.map(([k]) => k);
    assert.deepEqual(keys, fam === 'data' ? contract.data : contract.bands[fam], fam);
  }
  assert.deepEqual(Object.keys(LABELS.setup), contract.setup);
  assert.deepEqual(Object.keys(LABELS.checks), contract.checks);
  assert.deepEqual(Object.keys(LABELS.brand), contract.brands);
  assert.deepEqual(contract.bands.chk, contract.checks);
  assert.deepEqual(contract.bands.fs, contract.screens);
});

test('the click map has the contract screens and grid', () => {
  const UI = new Function(readFileSync(here('../dashboard/ui-map.js'), 'utf8') + ';return UI;')();
  assert.deepEqual(UI.screens.map((s) => s.id), contract.screens);
  assert.equal(UI.COLS, contract.grid.cols);
  assert.equal(UI.ROWS, contract.grid.rows);
});
