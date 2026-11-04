// Tests of the stats dashboard v4 (Nov. 2026, publish/auditoria-2026-11-04/dashboard-v4/spec.md). Run with the rest:
//   node --test test/worker.test.mjs   (it imports this file)
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { fakeD1 } from './d1.mjs';
import { summary, ACT_STEPS, STUCK } from '../src/api.js';

const ID = (n) => n.toString(16).padStart(32, '0');
const F0 = { version: 'all', country: 'all', mode: 'all', link: 'all' };
const NOW = Date.parse('2026-11-04T15:30:00Z');
const ago = (n, now = NOW) => new Date(now - n * 864e5).toISOString().slice(0, 10);

/** A small database: install-days as [daysAgo, id, link, counters, app]. Each install-day gets one report with 10 minutes. */
export function seed(rows, { now = NOW, clicks = [], errors = [] } = {}) {
  const DB = fakeD1();
  const ping = DB.raw.prepare('INSERT INTO pings (day, ts, id, app, link, minutes, dev, country, mode) VALUES (?, ?, ?, ?, ?, 10, 0, ?, ?)');
  const cnt = DB.raw.prepare('INSERT INTO counts (day, id, name, n, dev) VALUES (?, ?, ?, ?, 0)');
  for (const [d, id, link, counts = {}, app = '0.1.0-beta.10', country = 'PT', mode = 'game'] of rows) {
    ping.run(ago(d, now), now - d * 864e5, ID(id), app, link, country, mode);
    for (const [k, n] of Object.entries({ id_install: 1, ...counts })) cnt.run(ago(d, now), ID(id), k, n);
  }
  for (const [d, id, name, n] of clicks) DB.raw.prepare('INSERT INTO clicks (day, id, name, n, dev) VALUES (?, ?, ?, ?, 0)').run(ago(d, now), ID(id), name, n);
  for (const [d, id, type, at, n] of errors) DB.raw.prepare("INSERT INTO errors (day, id, app, type, at, n, dev) VALUES (?, ?, '0.1.0-beta.10', ?, ?, ?, 0)").run(ago(d, now), ID(id), type, at, n);
  return DB;
}
const sum7 = async (DB, period = 7, now = NOW) => JSON.parse(JSON.stringify(await summary(DB, { period, f: F0, dev: false, now })));

// ------------------------------------------------------------------ step 1: fixes without design

test('v4 step 1: install age bands come out in their order, not in the order they were first seen', async () => {
  const DB = seed([[0, 1, 'usb', { age_7_13: 1 }], [0, 2, 'usb', { age_0: 1 }], [0, 3, 'usb', { age_2_3: 1 }], [0, 4, 'usb', { age_30p: 1 }], [0, 5, 'usb', { age_1: 1 }]]);
  const s = await sum7(DB);
  assert.deepEqual(s.age.map((a) => a.band), ['0', '1', '2-3', '7-13', '30+']);
});

test('v4 step 1: "Telemóvel ligado" in the v3 funnel no longer counts phone_setup_done (it only means the pane opened already done)', async () => {
  const DB = seed([[0, 1, 'none', { first_start: 1, phone_setup_done: 1 }], [0, 2, 'usb', { first_start: 1 }]]);
  const s = await sum7(DB);
  assert.deepEqual(s.funnel.map((x) => x.n), [2, 1, 0]);
});

// ------------------------------------------------------------------ step 5: activation per install, within 7 days

test('v4 step 5: an install with first_start on day 0, USB on day 1 and Protected on day 2 reaches 3 steps (v3 counted 1)', async () => {
  const DB = seed([[4, 1, 'none', { first_start: 1, accounts: 1 }], [3, 1, 'usb', { accounts: 1 }], [2, 1, 'usb', { tp_10_30: 1, ip_changed: 1 }]]);
  const s = await sum7(DB);
  assert.deepEqual(ACT_STEPS, ['first_start', 'phone_seen', 'protected', 'first_match']);
  assert.deepEqual(s.activation.steps.map((x) => x.n), [1, 1, 1, 0]);
  assert.equal(s.funnel[1].n, 0, 'the v3 same-day funnel stays as it was (only day 0)');
  assert.deepEqual(s.activation.ip_changed, { n: 1, of: 1, same_only: 0 });
  assert.equal(s.activation.immature, 1, 'its 7 days are not over yet');
});

test('v4 step 5: a step on day 7 is outside the first 7 days; D1 and D7 only count installs whose day is over', async () => {
  const DB = seed([[9, 1, 'none', { first_start: 1, accounts: 1 }], [2, 1, 'usb', { tp_lt10: 1 }], [8, 1, 'none', { accounts: 1 }],
    [1, 2, 'usb', { first_start: 1, accounts: 1 }]]);
  const s = await sum7(DB, 30);
  assert.deepEqual(s.activation.steps.map((x) => x.n), [2, 1, 0, 0], 'install 1 got the phone on day 7: not counted; install 2 got it on day 0');
  assert.deepEqual(s.activation.d1, { n: 1, back: 1 }, 'install 2 (first seen yesterday) is left out of D1');
  assert.deepEqual(s.activation.d7, { n: 0, back: 0 }, 'D7 is day 7 to 13: install 1 (9 days old) is not counted until its day 13 is over');
  const c = s.activation.cohorts.find((x) => x.day === ago(9));
  assert.deepEqual(c.steps, [1, 0, 0, 0]);
  assert.equal(s.activation.cohorts.find((x) => x.day === ago(1)).d1, null);
});

test('v4 step 5: the steps are nested; Protected counts as the phone seen (an adapter reported as other still needs the phone), a match without Protected stays beside', async () => {
  const DB = seed([[1, 1, 'other', { first_start: 1, accounts: 1, tp_lt10: 1, m_valve: 2 }], [1, 2, 'hotspot', { first_start: 1, accounts: 1, m_comm: 1 }], [1, 3, 'none', { first_start: 1, accounts: 1 }]]);
  const s = await sum7(DB);
  assert.deepEqual(s.activation.steps.map((x) => x.n), [3, 2, 1, 1]);
  assert.deepEqual(s.activation.any, { protected: 1, first_match: 2 });
  assert.equal(s.activation.steps[2].of_prev, 0.5);
  assert.deepEqual(s.stuck.buckets.map((b) => b.n), [1, 1, 0, 1], 'the Protected install on another adapter is not "never saw the phone"');
});

test('v4 step 5: segments use the first day\'s country, version, mode and link (as the spec asks)', async () => {
  const DB = seed([[3, 1, 'none', { first_start: 1, accounts: 1 }, '0.1.0-beta.9', 'BR', 'always'], [2, 1, 'usb', { tp_lt10: 1 }, '0.1.0-beta.10', 'BR', 'game'],
    [1, 2, 'none', { first_start: 1, accounts: 1 }, '0.1.0-beta.10', 'PT', 'game']]);
  const s = await sum7(DB);
  assert.deepEqual(s.activation.segments.country, [{ k: 'BR', n: 1, protected: 1, first_match: 0 }, { k: 'PT', n: 1, protected: 0, first_match: 0 }]);
  assert.deepEqual(s.activation.segments.link.map((x) => [x.k, x.n]), [['none', 2]], 'install 1 got USB on day 1: its first day was none');
  assert.deepEqual(s.activation.segments.version.map((x) => x.k), ['0.1.0-beta.10', '0.1.0-beta.9']);
  assert.ok(s.activation.segments.mode.some((x) => x.k === 'always'));
});

test('v4 step 5: the period before is the same cohort maths, for the comparison', async () => {
  const DB = seed([[10, 1, 'usb', { first_start: 1, accounts: 1 }], [9, 2, 'none', { first_start: 1, accounts: 1 }], [1, 3, 'usb', { first_start: 1, accounts: 1 }]]);
  const s = await sum7(DB);
  assert.equal(s.activation.n, 1);
  assert.equal(s.activation.prev.n, 2);
  assert.deepEqual(s.activation.prev.steps.map((x) => x.n), [2, 1, 0, 0]);
});

// ------------------------------------------------------------------ step 7: where they get stuck

test('v4 step 7: one install per bucket gives 1 each, and the buckets add up to the new installs', async () => {
  const DB = seed([
    [2, 1, 'none', { first_start: 1, accounts: 1, phone_setup_nousbdebugging: 1 }],
    [2, 2, 'usb', { first_start: 1, accounts: 1, ip_same: 1 }],
    [2, 3, 'usb', { first_start: 1, accounts: 1, tp_lt10: 1 }],
    [2, 4, 'usb', { first_start: 1, accounts: 1, tp_lt10: 1, m_valve: 1 }],
  ], { clicks: [[2, 1, 'Setup_nousbdebugging', 2]] });
  const s = await sum7(DB);
  assert.deepEqual(STUCK, ['never_phone', 'phone_not_protected', 'protected_no_match', 'reached_match']);
  assert.deepEqual(s.stuck.buckets.map((b) => b.n), [1, 1, 1, 1]);
  assert.equal(s.stuck.buckets.reduce((a, b) => a + b.n, 0), s.stuck.n);
  assert.deepEqual(s.stuck.buckets.map((b) => b.share), [0.25, 0.25, 0.25, 0.25]);
  const step = s.stuck.setup.find((x) => x.step === 'nousbdebugging');
  assert.deepEqual(step, { step: 'nousbdebugging', open: 1, left: null, pressed: 1 }, 'at close is not measured until the app sends phone_left_');
  assert.equal(s.stuck.left_measured, false);
  assert.equal(s.stuck.never_opened, 0);
  assert.deepEqual(s.stuck.phone_not_protected, { n: 1, ip_new: 0, ip_same_only: 1, red: [] });
});

test('v4 step 7: phone_left_<step> fills "at close"; a crash or an error on day 1 is a signal; never opened Phone setup', async () => {
  const DB = seed([
    [1, 1, 'none', { first_start: 1, accounts: 1, phone_setup_nousb: 1, phone_left_nousbdebugging: 1 }],
    [1, 2, 'none', { first_start: 1, accounts: 1, crash: 1, cable_hint: 2 }],
    [1, 3, 'none', { first_start: 1, accounts: 1 }],
  ], { errors: [[1, 3, 'HttpRequestException', 'Adb.Run', 4]] });
  const s = await sum7(DB);
  assert.equal(s.stuck.left_measured, true);
  assert.equal(s.stuck.setup.find((x) => x.step === 'nousbdebugging').left, 1);
  assert.equal(s.stuck.setup.find((x) => x.step === 'nousb').open, 1);
  assert.equal(s.stuck.never_opened, 2);
  const sig = Object.fromEntries(s.stuck.signals.map((x) => [x.id, x]));
  assert.equal(sig.day1_crash_or_error.installs, 2);
  assert.deepEqual([sig.cable_hint.n, sig.cable_hint.installs], [2, 1]);
  const e = s.errorsRecent.find((x) => x.type === 'HttpRequestException');
  assert.deepEqual([e.times, e.installs, e.new_installs_day1, e.is_new], [4, 1, 1, true]);
});

// ------------------------------------------------------------------ growth: installs, windows and stickiness

test('v4 step 9: distinct installs in fixed windows are not install-days; WAU per day; the median minutes of an install', async () => {
  const DB = seed([[0, 1, 'usb', { accounts: 1 }], [1, 1, 'usb', { accounts: 1 }], [2, 1, 'usb', { accounts: 1 }], [0, 2, 'usb', { accounts: 1, first_start: 1 }], [8, 3, 'usb', { accounts: 1 }]]);
  const s = await sum7(DB);
  assert.equal(s.days.reduce((a, d) => a + d.users, 0), 4, 'four install-days');
  assert.deepEqual([s.windows.d7.active, s.windows.d7.fresh, s.windows.d7.minutes_median], [2, 1, 10], 'two installs (30 and 10 minutes)');
  assert.equal(s.windows.prev7.active, 1);
  assert.equal(s.windows.d30, null, 'not read with a 7-day period');
  assert.equal(s.stick.at(-1).wau, 2);
  assert.equal(s.stick.at(-1).dau, 2);
  assert.equal(s.prevDays.length, 7);
  assert.equal(s.prevDays.find((d) => d.date === ago(8)).users, 1);
});

test('v4: per day, the reports from builds before beta.10, the link none, unclean exits, the home IP back, the leak test and Log errors', async () => {
  const DB = seed([[0, 1, 'none', { accounts: 1, unclean_exit: 1, ip_home_back: 2, leak_bad: 1, log_error: 3 }, '0.1.0-beta.9'], [0, 2, 'usb', { accounts: 1, leak_ok: 1 }, '0.1.0-beta.10']]);
  const d = (await sum7(DB)).days.at(-1);
  assert.deepEqual([d.reports, d.oldReports, d.none, d.unclean, d.homeBack, d.leakBad, d.leakOk, d.logErr], [2, 1, 1, 1, 2, 1, 1, 3]);
});

test('v4: "same weekday last week at the same time" counts only reports sent before this time a week ago', async () => {
  const DB = fakeD1();
  const ping = DB.raw.prepare('INSERT INTO pings (day, ts, id, app, link, minutes, dev) VALUES (?, ?, ?, ?, ?, 10, 0)');
  const cnt = DB.raw.prepare('INSERT INTO counts (day, id, name, n, dev) VALUES (?, ?, ?, ?, 0)');
  ping.run(ago(7), NOW - 7 * 864e5 - 3600e3, ID(1), '0.1.0-beta.10', 'usb'); cnt.run(ago(7), ID(1), 'first_start', 1); cnt.run(ago(7), ID(1), 'accounts', 1);
  ping.run(ago(7), NOW - 7 * 864e5 + 3600e3, ID(2), '0.1.0-beta.10', 'usb'); cnt.run(ago(7), ID(2), 'accounts', 1);
  const s = await sum7(DB);
  assert.deepEqual(s.sameTimeWeek, { date: ago(7), users: 1, fresh: 1, minutes: 10 });
  assert.equal((await sum7(DB, 1)).sameTimeWeek, null, 'a 1-day period does not read that day');
});

test('v4: no install ID and no row per install in what summary() returns', async () => {
  const DB = seed([[1, 1, 'none', { first_start: 1, accounts: 1, phone_setup_nousb: 1 }], [0, 2, 'usb', { first_start: 1, accounts: 1, tp_lt10: 1 }]], { errors: [[0, 2, 'E', 'A.B', 1]] });
  const text = JSON.stringify(await summary(DB, { period: 30, f: F0, dev: false, now: NOW }));
  assert.ok(!/[0-9a-f]{32}/.test(text));
});

// ------------------------------------------------------------------ the sums in SQL give the same numbers as the old read

/** A random but repeatable database: installs over 40 days, every kind of counter, scanners, dev builds, filters' values. */
function randomDB(seedN) {
  let x = seedN;
  const rnd = () => { x = (x * 1103515245 + 12345) % 2147483648; return x / 2147483648; };
  const pick = (l) => l[Math.floor(rnd() * l.length)];
  const NAMES = ['first_start', 'accounts', 'servers', 'app_start', 'crash', 'ip_changed', 'ip_same', 'ip_no_network', 'ip_home_back', 'min_prot', 'min_cs2', 'cs2_start',
    'm_valve', 'm_comm', 'm_valve_lag', 'm_comm_lag', 'unclean_exit', 'leak_ok', 'leak_bad', 'log_error', 'upd_failed', 'upd_seen', 'cable_hint', 'tp_10_30', 'tp_lt10',
    'st_1_2', 'st_lt1', 'ping_30_50', 'jit_lt5', 'jitd_5_10', 'late_0', 'loss_0', 'sess_30_60', 'chg_air', 'chgt_5_10', 'chga_1', 'chgq_good', 'drop_0', 'rej_try',
    'scr_connection', 'scr_servers', 'path_connection_servers', 'age_0', 'age_7_13', 'act_3', 'set_CloseToTray', 'set_Mode', 'reg_fra', 'reg_mad', 'data_lt250m', 'data_1_2g',
    'chk_firewall', 'chk_egress', 'phone_setup_nousb', 'phone_left_nousb', 'fs_connection', 'scl_servers_50', 'kick_seen', 'updfrom_0_1_0_beta_9', 'net_dc', 'flags'];
  const DB = fakeD1();
  const ping = DB.raw.prepare('INSERT INTO pings (day, ts, id, app, link, minutes, dev, country, mode) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)');
  const cnt = DB.raw.prepare('INSERT INTO counts (day, id, name, n, dev) VALUES (?, ?, ?, ?, ?)');
  const clk = DB.raw.prepare('INSERT INTO clicks (day, id, name, n, dev) VALUES (?, ?, ?, ?, ?)');
  for (let id = 1; id <= 60; id++) {
    const dev = rnd() < 0.1 ? 1 : 0, scanner = rnd() < 0.15, start = Math.floor(rnd() * 40);
    for (let d = start; d >= 0; d -= 1 + Math.floor(rnd() * 4)) {
      const reports = 1 + Math.floor(rnd() * 3);
      for (let r = 0; r < reports; r++) ping.run(ago(d), NOW - d * 864e5 - r * 3600e3, ID(id), pick(['0.1.0-beta.9', '0.1.0-beta.10']), pick(['usb', 'hotspot', 'none', 'other', null]),
        scanner ? 0 : Math.floor(rnd() * 90), dev, pick(['PT', 'BR', '', null]), pick(['game', 'always', '', null]));
      cnt.run(ago(d), ID(id), 'id_install', 1, dev);
      if (d === start) cnt.run(ago(d), ID(id), 'first_start', 1, dev);
      if (!scanner) for (const n of NAMES) if (rnd() < 0.25) { cnt.run(ago(d), ID(id), n, 1 + Math.floor(rnd() * 4), dev); if (rnd() < 0.2) cnt.run(ago(d), ID(id), n, 1 + Math.floor(rnd() * 3), dev); }
      if (!scanner && rnd() < 0.5) clk.run(ago(d), ID(id), pick(['ChangeIp_Click', 'Setup_nousb', 'PhoneHelp_Click']), 1 + Math.floor(rnd() * 3), dev);
    }
  }
  return DB;
}

test('v4 performance: the counters summed by D1 (sums sql) give exactly the numbers of the old per-install read, with filters, dev and EXCLUDE_IDS', async () => {
  for (const seedN of [1, 7, 42]) {
    const DB = randomDB(seedN);
    for (const period of [1, 7, 30]) for (const f of [F0, { ...F0, version: '0.1.0-beta.10' }, { ...F0, country: '?' }, { ...F0, mode: 'game', link: 'usb' }, { ...F0, link: '?' }])
      for (const [dev, exclude] of [[false, []], [true, []], [false, [ID(3), ID(5)]]]) {
        const a = JSON.parse(JSON.stringify(await summary(DB, { period, f, dev, now: NOW, exclude, sums: 'sql' })));
        const b = JSON.parse(JSON.stringify(await summary(DB, { period, f, dev, now: NOW, exclude, sums: 'js' })));
        assert.deepEqual(a, b, `seed ${seedN}, period ${period}, ${JSON.stringify(f)}, dev ${dev}, exclude ${exclude.length}`);
      }
  }
});
