// Anonymous usage stats for CS2 IP Changer (beta). README > Telemetry says what the app sends.
//
// What is kept: a random install ID made on the PC, the app and Windows versions, the mode, how the phone is connected,
// minutes the app was open, how many times each button or hotkey was used, a few counters (IP changes, kicks, bans marked,
// list sizes in buckets) and errors as type + code location. Never kept: the sender's IP (Cloudflare needs it to deliver the
// request; this code never reads it), and nothing the app doesn't send (no IPs, servers, accounts, names). Logs are off (wrangler.toml).

const MAX_BODY = 16 * 1024;
const ID = /^[0-9a-f]{32}$/;
const VERSION = /^\d+\.\d+\.\d+(-[0-9A-Za-z.-]{1,30})?$/;
const NAME = /^[A-Za-z0-9_]{1,48}$/;
const WORD = /^[a-z0-9_.-]{1,24}$/;
const LOCATION = /^[A-Za-z0-9_.<>`|+-]{1,120}$/;

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

    // One report per install every 10 minutes at most: more is a loop or someone playing with the endpoint.
    const last = await env.DB.prepare('SELECT ts FROM pings WHERE id = ? ORDER BY ts DESC LIMIT 1').bind(b.id).first();
    if (last && now - last.ts < 10 * 60 * 1000) return new Response('later', { status: 429 });

    const word = (x) => (typeof x === 'string' && WORD.test(x) ? x : null);
    const int = (x, max) => (Number.isInteger(x) && x >= 0 && x <= max ? x : null);
    const stmts = [
      env.DB.prepare('INSERT INTO pings (day, ts, id, app, os, mode, link, minutes, dev) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)')
        .bind(day, now, b.id, b.app, word(b.os), word(b.mode), word(b.link), int(b.minutes, 60 * 24 * 31), dev),
    ];
    for (const [table, obj, max] of [['clicks', b.clicks, 100000], ['counts', b.counts, 1000000]]) {
      if (!obj || typeof obj !== 'object') continue;
      for (const [name, n] of Object.entries(obj).slice(0, 150))
        if (NAME.test(name) && int(n, max) !== null)
          stmts.push(env.DB.prepare(`INSERT INTO ${table} (day, id, name, n, dev) VALUES (?, ?, ?, ?, ?)`).bind(day, b.id, name, n, dev));
    }
    if (Array.isArray(b.errors))
      for (const e of b.errors.slice(0, 30))
        if (e && NAME.test(e.type || '') && LOCATION.test(e.at || '') && int(e.n, 100000) !== null)
          stmts.push(env.DB.prepare('INSERT INTO errors (day, id, app, type, at, n, dev) VALUES (?, ?, ?, ?, ?, ?, ?)').bind(day, b.id, b.app, e.type, e.at, e.n, dev));
    await env.DB.batch(stmts);
    return new Response(null, { status: 204 });
  },
};
