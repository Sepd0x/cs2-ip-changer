// 15 Nov. 2026 (audit F-01): a day that has ended is read once, not on every dashboard computation. The summary used to read the
// reports, counters, clicks and errors of the last 60 days row by row each time it was worked out (about 6 reads for each row
// kept: 39,000 rows per daily user in the 60-day window), so D1's free 5 M reads a day ran out at about 25 daily users. Now the
// day after it ends the cron (or the first summary that finds it missing) writes one row per install-day into iday, with what
// the summary needs of that day already added up, and the summary reads that row (one row read) instead of the 20 to 130 it
// came from. The day's own tables stay as they are: nothing is deleted or changed, iday is a copy that can be dropped and
// made again (rows: the same pseudonymous install ID the other tables have, so Delete my stats and the 12-month cleanup
// remove it too: src/index.js forget, src/dashboard.js prune).
//
// What is kept per install-day is the raw material of the summary, never a result: the sums of the day's minutes, the newest
// report's time with its version and mode, the counters as [sum, highest], the clicks, the errors and the hours with a report. So the
// summary works out exactly the same numbers from it as from the tables (test/roll.test.mjs compares the two).
//
// iday_done holds the days written. They are written oldest first, so the written days are one run from the first day with data
// up to the newest, and the summary needs only the newest of them: from the next day on it reads the tables as before.

export const GRACE_MS = 10 * 60_000;   // a day is closed 10 minutes after midnight UTC: a report in flight at midnight lands first
export const IDAY_TABLE = 'CREATE TABLE IF NOT EXISTS iday (day TEXT NOT NULL, id TEXT NOT NULL, dev INTEGER NOT NULL DEFAULT 0, ms INTEGER, ta TEXT, tm TEXT, country TEXT, lk INTEGER, os TEXT, '
  + 'r INTEGER NOT NULL DEFAULT 0, kept INTEGER NOT NULL DEFAULT 0, j TEXT NOT NULL, PRIMARY KEY (day, id, dev))';
export const IDAY_INDEX = 'CREATE INDEX IF NOT EXISTS iday_id ON iday (id)';
export const IDAY_DONE_TABLE = 'CREATE TABLE IF NOT EXISTS iday_done (day TEXT PRIMARY KEY)';
export const IDAY_DDL = [IDAY_TABLE, IDAY_INDEX, IDAY_DONE_TABLE];

const ymd = (t) => new Date(t).toISOString().slice(0, 10);
const nextDay = (d) => ymd(Date.parse(`${d}T00:00:00Z`) + 864e5);
const noTable = (e) => /no such table/i.test(String(e && e.message));
const LINK_CASE = "CASE link WHEN 'usb' THEN 3 WHEN 'hotspot' THEN 2 WHEN 'other' THEN 1 WHEN 'none' THEN 0 END";

/** The newest day written, or null when none (or no table yet). One row on the primary key. */
export async function lastRolled(db) {
  try { return (await db.prepare('SELECT MAX(day) AS d FROM iday_done').first())?.d ?? null; } catch (e) { if (noTable(e)) return null; throw e; }
}

/** The first day to read from the tables: the day after the newest day written, never later than the first open day. */
export function rawFromDay(last, now) {
  const closedBefore = ymd(now - GRACE_MS);
  if (!last) return null;
  const n = nextDay(last);
  return n < closedBefore ? n : closedBefore;
}

/**
 * Writes the iday rows of one day (read from the day indexes of pings, counts, clicks and errors) and marks it written. The rows go
 * first, in slices, and the mark last: a run that stops half way leaves a day that is simply written again.
 */
export async function rollDay(db, d) {
  const q = async (sql) => (await db.prepare(sql).bind(d).all()).results ?? [];
  const [pg, hr, cn, ck, er] = await Promise.all([
    q(`SELECT id, dev, SUM(minutes) AS ms, MAX(ts || '|' || app) AS ta, MAX(ts || '|' || COALESCE(mode, '')) AS tm, MAX(country) AS country, MAX(${LINK_CASE}) AS lk, MAX(os) AS os, COUNT(*) AS r
       FROM pings WHERE day = ? GROUP BY id, dev`),
    q('SELECT id, dev, CAST(ts / 3600000 AS INTEGER) AS h FROM pings WHERE day = ? GROUP BY id, dev, h'),
    q('SELECT id, dev, name, SUM(n) AS n, MAX(n) AS mx FROM counts WHERE day = ? GROUP BY id, dev, name'),
    q('SELECT id, dev, name, SUM(n) AS n FROM clicks WHERE day = ? GROUP BY id, dev, name'),
    q('SELECT id, dev, type, at, app, SUM(n) AS n FROM errors WHERE day = ? GROUP BY id, dev, type, at, app'),
  ]);
  // Only an install-day with a report is kept: the summary starts from the reports, and counters with no report are ignored there
  const recs = new Map();
  for (const p of pg) recs.set(`${p.id}|${p.dev}`, { p, c: {}, k: {}, e: [], h: [], kept: 0 });
  const rec = (r) => recs.get(`${r.id}|${r.dev}`);
  for (const r of cn) { const x = rec(r); if (x) { x.c[r.name] = [r.n, r.mx]; if (r.name === 'id_install') x.kept = 1; } }
  for (const r of ck) { const x = rec(r); if (x) x.k[r.name] = r.n; }
  for (const r of er) { const x = rec(r); if (x) x.e.push([r.type, r.at, r.app, r.n]); }
  for (const r of hr) { const x = rec(r); if (x) x.h.push(r.h); }
  const ins = 'INSERT OR REPLACE INTO iday (day, id, dev, ms, ta, tm, country, lk, os, r, kept, j) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)';
  const stmts = [...recs.values()].map(({ p, c, k, e, h, kept }) => db.prepare(ins)
    .bind(d, p.id, p.dev, p.ms ?? null, p.ta, p.tm, p.country ?? null, p.lk ?? null, p.os ?? null, p.r, kept, JSON.stringify({ c, k, e, h })));
  stmts.unshift(db.prepare('DELETE FROM iday WHERE day = ?').bind(d));
  for (let i = 0; i < stmts.length; i += 150) await db.batch(stmts.slice(i, i + 150));
  await db.prepare('INSERT OR REPLACE INTO iday_done (day) VALUES (?)').bind(d).run();
  return recs.size;
}

/**
 * Writes the days that have ended and are not written yet, oldest first, at most max of them. now = the real clock. Never throws:
 * a database without the tables yet gets them, any other failure leaves the days for the next run (the summary reads the tables
 * for them in the meantime). Returns the days written.
 */
export async function rollMissing(db, now, max = 2) {
  const done = [];
  // Two summaries worked out at the same moment (the /data page asks for 7 and 30 days) would write the same day twice
  if (busy.has(db)) return done;
  busy.add(db);
  try {
    const closedBefore = ymd(now - GRACE_MS);
    let last = await lastRolled(db);
    for (let i = 0; i < max; i++) {
      let d;
      if (last) d = nextDay(last);
      else d = (await db.prepare('SELECT MIN(day) AS d FROM pings').first())?.d;
      if (!d || d >= closedBefore) break;
      try { await rollDay(db, d); } catch (e) {
        if (!noTable(e)) throw e;
        for (const s of IDAY_DDL) await db.prepare(s).run();
        await rollDay(db, d);
      }
      done.push(d); last = d;
    }
  } catch { /* the next run tries again */ } finally { busy.delete(db); }
  return done;
}
const busy = new WeakSet();

/** The iday rows of the days in [from, to) for a view (who()), one record per install-day (two dev rows of one ID merged as the GROUP BY does). */
export async function readRolled(db, W, from, to) {
  const rows = (await db.prepare(`SELECT day, id, ms, ta, tm, country, lk, os, r, kept, j FROM iday WHERE ${W.sql} AND day >= ? AND day < ?`).bind(...W.args, from, to).all()).results ?? [];
  const by = new Map();
  for (const r of rows) {
    const j = JSON.parse(r.j), k = `${r.day}|${r.id}`;
    const x = by.get(k);
    if (!x) { by.set(k, { day: r.day, id: r.id, ms: r.ms || 0, ta: r.ta, tm: r.tm, country: r.country, lk: r.lk, os: r.os, r: r.r, kept: r.kept, c: j.c || {}, k: j.k || {}, e: j.e || [], h: j.h || [] }); continue; }
    x.ms += r.ms || 0; x.r += r.r; x.kept = Math.max(x.kept, r.kept);
    if (r.ta > x.ta) x.ta = r.ta;
    if (r.tm > x.tm) x.tm = r.tm;
    if (r.country != null && (x.country == null || r.country > x.country)) x.country = r.country;
    if (r.os != null && (x.os == null || r.os > x.os)) x.os = r.os;
    if (r.lk != null && (x.lk == null || r.lk > x.lk)) x.lk = r.lk;
    for (const [n, [s, m]] of Object.entries(j.c || {})) { const y = x.c[n]; x.c[n] = y ? [y[0] + s, Math.max(y[1], m)] : [s, m]; }
    for (const [n, s] of Object.entries(j.k || {})) x.k[n] = (x.k[n] || 0) + s;
    for (const [t, a, ap, n] of j.e || []) { const y = x.e.find((z) => z[0] === t && z[1] === a && z[2] === ap); if (y) y[3] += n; else x.e.push([t, a, ap, n]); }
    for (const h of j.h || []) if (!x.h.includes(h)) x.h.push(h);
  }
  return [...by.values()].sort((a, b) => (a.day === b.day ? (a.id < b.id ? -1 : 1) : a.day < b.day ? 1 : -1));
}

/** The records as the rows the summary's own queries return, so its code reads both the same way. perInstall(name): counters read per install-day. */
export function expand(recs, perInstall) {
  const pings = [], counts = [], clicks = [], errors = [], hours = [];
  for (const x of recs) {
    pings.push({ day: x.day, id: x.id, m: Math.min(x.ms, 1440), app: x.ta.slice(14), country: x.country, mode: x.tm.slice(14), lk: x.lk, os: x.os, r: x.r });
    for (const name of Object.keys(x.c).sort()) if (perInstall(name)) counts.push({ day: x.day, id: x.id, name, n: x.c[name][0], mx: x.c[name][1] });
    for (const name of Object.keys(x.k).sort()) clicks.push({ day: x.day, id: x.id, name, n: x.k[name] });
    for (const [type, at, app, n] of [...x.e].sort((a, b) => (a.join('|') < b.join('|') ? -1 : 1))) errors.push({ day: x.day, id: x.id, type, at, app, n });
    for (const h of [...x.h].sort((a, b) => a - b)) hours.push({ day: x.day, id: x.id, h });
  }
  return { pings, counts, clicks, errors, hours };
}

/** All the install IDs kept (id_install) with their first day, from the days written before `to`, and the (day, id) pairs from `from` on. */
export async function readKept(db, W, from, to) {
  const [first, days] = await Promise.all([
    db.prepare(`SELECT id, MIN(day) AS d, MAX(kept) AS kept FROM iday WHERE ${W.sql} AND day < ? GROUP BY id`).bind(...W.args, to).all(),
    db.prepare(`SELECT day, id FROM iday WHERE ${W.sql} AND day >= ? AND day < ?`).bind(...W.args, from, to).all(),
  ]);
  return { first: first.results ?? [], days: days.results ?? [] };
}

/** The time of the newest report among the days written in [from, to) (W null: any PC), or null. Looks at one day only. */
export async function readLatest(db, W, from, to) {
  const w = W ? `${W.sql} AND ` : '', a = W ? W.args : [];
  const r = await db.prepare(`SELECT MAX(ta) AS ta FROM iday WHERE ${w}day = (SELECT MAX(day) FROM iday WHERE ${w}day >= ? AND day < ?)`).bind(...a, ...a, from, to).first();
  return r?.ta ? Number(String(r.ta).slice(0, 13)) : null;
}
