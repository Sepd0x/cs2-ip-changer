// Anonymous usage stats for CS2 IP Changer (beta). README > Telemetry says what the app sends.
//
// What is kept: a random install ID made on the PC, the app and Windows versions, the mode, how the phone is connected,
// minutes the app was open, the country (two letters, from Cloudflare), how many times each button or hotkey was used, a few counters (IP changes, kicks, bans marked,
// list sizes: servers, accounts, flagged players, "Open with CS2" programs and turned-off regions) and errors as type + code location. Never kept: the sender's IP (Cloudflare needs it to deliver the
// request and works out the country from it; this code only gets the country, never the IP), and nothing the app doesn't send (no IPs, servers, accounts, names). Logs are off (wrangler.jsonc).

const MAX_BODY = 16 * 1024;
// Rows written a day, index updates included (D1 counts them). Well under D1's free 100,000: the per-install limit below is keyed
// on an id the sender picks, so a flood with fresh ids stops here instead of using up the day's writes (or costing money).
const DAILY_ROWS = 40000;
// Same as schema.sql. wrangler deploy doesn't run the schema, so a database made before the budget existed gets the table here.
const BUDGET_TABLE = 'CREATE TABLE IF NOT EXISTS budget (day TEXT PRIMARY KEY, rows INTEGER NOT NULL)';
const ID = /^[0-9a-f]{32}$/;
const VERSION = /^\d+\.\d+\.\d+(-[0-9A-Za-z.-]{1,30})?$/;
const NAME = /^[A-Za-z0-9_]{1,48}$/;
const WORD = /^[a-z0-9_.-]{1,24}$/;
const LOCATION = /^[A-Za-z0-9_.<>`|+-]{1,120}$/;
const COUNTRY = /^[A-Z][A-Z0-9]$/;   // ISO 3166 (PT, BR, US…), or Cloudflare's XX (unknown) and T1 (Tor)

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (request.method !== 'POST' || url.pathname !== '/v1') return new Response('CS2 IP Changer stats. See the README.', { status: 404 });
    const len = Number(request.headers.get('content-length') || 0);
    if (len > MAX_BODY) return new Response('too big', { status: 413 });
    let b;
    try {
      const text = await request.text();
      if (text.length > MAX_BODY) return new Response('too big', { status: 413 });
      b = JSON.parse(text);
    } catch { return new Response('bad json', { status: 400 }); }

    if (!b || b.v !== 1 || !ID.test(b.id) || !VERSION.test(b.app)) return new Response('bad', { status: 400 });
    const now = Date.now();
    const day = new Date(now).toISOString().slice(0, 10);
    const dev = b.dev === true ? 1 : 0;
    const cc = request.cf && typeof request.cf.country === 'string' && COUNTRY.test(request.cf.country) ? request.cf.country : null;

    // One report per install every 10 minutes at most: more is a loop or someone playing with the endpoint.
    const last = await env.DB.prepare('SELECT ts FROM pings WHERE id = ? ORDER BY ts DESC LIMIT 1').bind(b.id).first();
    if (last && now - last.ts < 10 * 60 * 1000) return new Response('later', { status: 429 });

    const word = (x) => (typeof x === 'string' && WORD.test(x) ? x : null);
    const int = (x, max) => (Number.isInteger(x) && x >= 0 && x <= max ? x : null);
    const stmts = [
      env.DB.prepare('INSERT INTO pings (day, ts, id, app, os, mode, link, minutes, country, dev) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)')
        .bind(day, now, b.id, b.app, word(b.os), word(b.mode), word(b.link), int(b.minutes, 60 * 24 * 31), cc, dev),
    ];
    // Caps a little above what the app sends (about 130 buttons, a few counters, at most 30 errors)
    for (const [table, obj, max, cap] of [['clicks', b.clicks, 100000, 150], ['counts', b.counts, 1000000, 40]]) {
      if (!obj || typeof obj !== 'object') continue;
      for (const [name, n] of Object.entries(obj).slice(0, cap))
        if (NAME.test(name) && int(n, max) !== null)
          stmts.push(env.DB.prepare(`INSERT INTO ${table} (day, id, name, n, dev) VALUES (?, ?, ?, ?, ?)`).bind(day, b.id, name, n, dev));
    }
    if (Array.isArray(b.errors))
      for (const e of b.errors.slice(0, 30))
        if (e && NAME.test(e.type || '') && LOCATION.test(e.at || '') && int(e.n, 100000) !== null)
          stmts.push(env.DB.prepare('INSERT INTO errors (day, id, app, type, at, n, dev) VALUES (?, ?, ?, ?, ?, ?, ?)').bind(day, b.id, b.app, e.type, e.at, e.n, dev));
    // pings has two indexes, the other tables one: 3 rows for the ping, 2 for each other row, 2 for the budget row
    const cost = 3 + 2 * (stmts.length - 1) + 2;
    // Read first, so a refused report writes nothing: the budget only grows with reports that are kept
    const readBudget = () => env.DB.prepare('SELECT rows FROM budget WHERE day = ?').bind(day).first();
    let used;
    try { used = await readBudget(); } catch (e) {
      if (!/no such table/i.test(String(e && e.message))) throw e;
      await env.DB.prepare(BUDGET_TABLE).run();
      used = await readBudget();
    }
    if ((used ? used.rows : 0) + cost > DAILY_ROWS) return new Response('later', { status: 429 });
    stmts.push(env.DB.prepare('INSERT INTO budget (day, rows) VALUES (?, ?) ON CONFLICT (day) DO UPDATE SET rows = rows + excluded.rows')
      .bind(day, cost));
    await env.DB.batch(stmts);
    return new Response(null, { status: 204 });
  },
};
