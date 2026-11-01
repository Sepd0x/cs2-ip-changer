// Checks the Cloudflare Access token on every dashboard request, so the page never opens without Access, even if the Access
// application were removed or set up wrong. Only Web Crypto, no packages.
//
// Needs three secrets (wrangler secret put, never in this public repo): TEAM_DOMAIN (https://<team>.cloudflareaccess.com),
// POLICY_AUD (the Access application's AUD tag) and DASHBOARD_EMAIL (the only e-mail let in). Any of them missing: always 403.

const enc = new TextEncoder();
const dec = new TextDecoder();
const TEAM = /^https:\/\/[a-z0-9-]{1,63}\.cloudflareaccess\.com$/;
const MAX_TOKEN = 8 * 1024;
const SKEW = 60;   // seconds of clock difference allowed on exp and nbf

function b64url(s) {
  if (typeof s !== 'string' || !/^[A-Za-z0-9_-]*$/.test(s)) throw new Error('not base64url');
  const pad = s.replace(/-/g, '+').replace(/_/g, '/') + '==='.slice((s.length + 3) % 4);
  const bin = atob(pad);
  const out = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
  return out;
}

const json = (bytes) => JSON.parse(dec.decode(bytes));

// Keys of the team, kept for an hour. An unknown kid fetches them again, at most once a minute (Access rotates its keys).
let cache = { team: null, at: 0, keys: new Map() };

async function keysFor(team, kid, now, fetchFn) {
  const fresh = cache.team === team && now - cache.at < 3600_000;
  if (fresh && cache.keys.has(kid)) return cache.keys.get(kid);
  if (cache.team === team && now - cache.at < 60_000) return cache.keys.get(kid) ?? null;
  const res = await fetchFn(`${team}/cdn-cgi/access/certs`, { headers: { accept: 'application/json' } });
  if (!res.ok) return null;
  const body = await res.json();
  const keys = new Map();
  for (const jwk of Array.isArray(body?.keys) ? body.keys : []) {
    if (jwk?.kty !== 'RSA' || typeof jwk.kid !== 'string' || (jwk.alg && jwk.alg !== 'RS256')) continue;
    try {
      keys.set(jwk.kid, await crypto.subtle.importKey('jwk', { kty: 'RSA', n: jwk.n, e: jwk.e, alg: 'RS256', ext: true },
        { name: 'RSASSA-PKCS1-v1_5', hash: 'SHA-256' }, false, ['verify']));
    } catch { /* a key that doesn't import is skipped */ }
  }
  cache = { team, at: now, keys };
  return keys.get(kid) ?? null;
}

/** For the tests: forget the keys between cases. */
export function resetAccessCache() { cache = { team: null, at: 0, keys: new Map() }; }

/**
 * { ok: true, email } only for a token signed by the team's keys (RS256), for this application (aud), from this team (iss),
 * in date, with the one allowed e-mail. Anything else: { ok: false, why }.
 */
export async function verifyAccess(request, env, { now = Date.now(), fetchFn = fetch } = {}) {
  const team = String(env.TEAM_DOMAIN || '').trim().replace(/\/+$/, '');
  const aud = String(env.POLICY_AUD || '').trim();
  const email = String(env.DASHBOARD_EMAIL || '').trim().toLowerCase();
  if (!TEAM.test(team) || !aud || !email) return { ok: false, why: 'not configured' };
  const assertion = request.headers.get('cf-access-jwt-assertion');
  if (!assertion) return { ok: false, why: 'no token' };
  if (assertion.length > MAX_TOKEN) return { ok: false, why: 'token too long' };
  const parts = assertion.split('.');
  if (parts.length !== 3) return { ok: false, why: 'not a JWT' };
  let header, payload, sig;
  try {
    header = json(b64url(parts[0]));
    payload = json(b64url(parts[1]));
    sig = b64url(parts[2]);
  } catch { return { ok: false, why: 'not a JWT' }; }
  // Only RS256: never "none" or HS256 (a key from the JWKS used as an HMAC secret)
  if (header?.alg !== 'RS256' || typeof header.kid !== 'string') return { ok: false, why: 'bad alg' };
  let key;
  try { key = await keysFor(team, header.kid, now, fetchFn); } catch { key = null; }
  if (!key) return { ok: false, why: 'unknown key' };
  const signed = await crypto.subtle.verify('RSASSA-PKCS1-v1_5', key, sig, enc.encode(`${parts[0]}.${parts[1]}`));
  if (!signed) return { ok: false, why: 'bad signature' };
  const t = Math.floor(now / 1000);
  const auds = Array.isArray(payload.aud) ? payload.aud : [payload.aud];
  if (!auds.includes(aud)) return { ok: false, why: 'wrong aud' };
  if (payload.iss !== team) return { ok: false, why: 'wrong iss' };
  if (!Number.isFinite(payload.exp) || payload.exp + SKEW < t) return { ok: false, why: 'expired' };
  if (Number.isFinite(payload.nbf) && payload.nbf - SKEW > t) return { ok: false, why: 'not yet valid' };
  if (typeof payload.email !== 'string' || payload.email.toLowerCase() !== email) return { ok: false, why: 'wrong email' };
  return { ok: true, email: payload.email };
}
