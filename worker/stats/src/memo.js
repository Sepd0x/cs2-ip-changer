// 12 Nov. 2026: D1 went over its free 5 million rows read a day (9 M in 24 h) and refused the app's reports until midnight UTC.
// Each dashboard load read about 66,000 rows (the summary of 30 days). Now the dashboard Worker keeps what it worked out in a
// small table (memo, one row per question), so a load reads that row and only works it out again when it is old. Only added-up
// numbers go in here, never an install ID (the summary and the history never have one), so Delete my stats has nothing to find.
// The workers.dev address has no Cache API, and an isolate's memory is gone after a few idle minutes: the table is shared by all.

// Same text as schema.sql; made here the first time a write finds no table (a read that finds none is a miss), so a load
// never waits an extra round trip for it.
export const MEMO_TABLE = 'CREATE TABLE IF NOT EXISTS memo (k TEXT PRIMARY KEY, at INTEGER NOT NULL, stamp TEXT, v TEXT NOT NULL)';
const noTable = (e) => /no such table/i.test(String(e?.message));

/** A short, stable hash of a text (FNV-1a, 32 bits): the excluded IDs go into a key without the IDs themselves. */
export function fnv(s) {
  let h = 0x811c9dc5;
  for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 0x01000193) >>> 0; }
  return h.toString(16).padStart(8, '0');
}

/**
 * The answer kept under key: used while it is younger than maxAge, or older but with the same stamp (nothing was written since),
 * else worked out again by compute() and kept. stamp is a promise of a short text (or null for none). Never fails a request on
 * its own: a memo that can't be read or written is the old way (compute every time). A hit waits one D1 round trip (the memo
 * row and the stamp together); a miss three (that, the reads themselves, the write).
 */
export function memo(db, opt) {
  // The pages ask for the same answer at once (/data asks /api/summary and /api/brief together): one isolate works it out once
  const k = opt.key;
  const on = inflight.get(db) || (inflight.set(db, new Map()), inflight.get(db));
  if (on.has(k)) return on.get(k);
  const p = memoOnce(db, opt).finally(() => on.delete(k));
  on.set(k, p);
  return p;
}
const inflight = new WeakMap();

async function memoOnce(db, { key, now, maxAge, stamp = null, compute }) {
  const stP = Promise.resolve(stamp).catch(() => null);
  const [hit, st] = await Promise.all([db.prepare('SELECT at, stamp, v FROM memo WHERE k = ?').bind(key).first().catch(() => null), stP]);
  if (hit && hit.at <= now && (now - hit.at < maxAge || (st != null && hit.stamp === st))) {
    try { return JSON.parse(hit.v); } catch { /* worked out again below */ }
  }
  const data = await compute();
  const put = () => db.prepare('INSERT INTO memo (k, at, stamp, v) VALUES (?, ?, ?, ?) ON CONFLICT (k) DO UPDATE SET at = excluded.at, stamp = excluded.stamp, v = excluded.v')
    .bind(key, now, st ?? null, JSON.stringify(data)).run();
  try { await put(); } catch (e) {
    try { if (noTable(e)) { await db.prepare(MEMO_TABLE).run(); await put(); } } catch { /* the answer still goes out */ }
  }
  return data;
}

/** Old answers out (the daily cron): keys carry their UTC day, so yesterday's are never read again. */
export async function pruneMemo(db, now, keepMs = 2 * 864e5) {
  try { return Number((await db.prepare('DELETE FROM memo WHERE at < ?').bind(now - keepMs).run())?.meta?.changes ?? 0); }
  catch (e) { if (noTable(e)) return 0; throw e; }
}

/** Forget the answers whose key starts with prefix (after a write they depend on, e.g. the daily GitHub picture). Keys are
 *  plain ASCII below "~", so prefix to prefix + "~" is all of them, on the primary key. */
export async function forgetMemo(db, prefix) {
  try { await db.prepare('DELETE FROM memo WHERE k >= ? AND k < ?').bind(prefix, prefix + '~').run(); }
  catch (e) { if (!noTable(e)) throw e; }
}
