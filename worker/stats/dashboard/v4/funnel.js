// Beta.13 (7 nov. 2026, the owner's 3.png: "33, 0, 6, 0%"): the rows of a funnel block, only numbers, no HTML. app.js draws them;
// the Worker's tests load this file as it is (worker/stats/test/beta13.test.mjs).
// - The bars are against the first step that has a number, and only the steps of the funnel get one.
// - "do 1.º" and "do anterior" are [n, of] pairs from the second step on, the second step included (it said nothing before).
// - A step from another source (outside: the exe downloads come from GitHub, from every origin, the app's own updates too) is shown
//   beside the funnel: no bar against the visits and no % of a step it doesn't come from.
// - A step bigger than the one before is flagged (bigger): a funnel can only shrink, so it is a different count, never drawn as
//   if it came from the step above.
'use strict';
(function (root) {
  function funnelRows(steps) {
    const inside = steps.filter((s) => !s.outside);
    const first = inside.find((s) => s.n != null) || null;
    let prev = null;
    return steps.map((s) => {
      if (s.outside) return { ...s, bar: null, ofFirst: null, ofPrev: null };
      const na = s.n == null;
      const isFirst = s === first;
      const row = {
        ...s,
        bar: na ? null : first && first.n ? Math.min(100, (100 * s.n) / first.n) : 0,
        ofFirst: !na && !isFirst && first && first.n ? [s.n, first.n] : null,
        ofPrev: !na && !isFirst && prev != null && prev.n ? [s.n, prev.n] : null,
        bigger: !na && prev != null && s.n > prev.n,
      };
      if (!na) prev = s;
      return row;
    });
  }
  root.V4F = { funnelRows };
})(typeof window !== 'undefined' ? window : globalThis);
