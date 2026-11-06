// Beta.12 (6 nov. 2026, publish/untangle-2026-11-06): without Chrome, tools/shots.mjs left the old social card and the manifest
// said it was current. The manifest now records which Status piece the card was rendered from (tests/DesignSyncTests.cs checks it).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { socialFrom } from '../../../tools/shots.mjs';

test('shots: the social card shows the new Status only when it was just rendered', () => {
  const old = { social: { from: 'aaa' } };
  assert.equal(socialFrom(old, true, 'bbb'), 'bbb', 'rendered now: from the new Status');
  assert.equal(socialFrom(old, false, 'bbb'), 'aaa', 'no Chrome: the card still shows what it showed');
  assert.equal(socialFrom({}, false, 'bbb'), null, 'never recorded and not rendered: not known, never the new one');
});
