-- Anonymous usage stats (worker/stats). No IP column: the Worker never reads the sender's address, only the country Cloudflare works out.
-- A database made before the country column needs it once: ALTER TABLE pings ADD COLUMN country TEXT;
CREATE TABLE IF NOT EXISTS pings (day TEXT NOT NULL, ts INTEGER NOT NULL, id TEXT NOT NULL, app TEXT NOT NULL, os TEXT, mode TEXT, link TEXT, minutes INTEGER, dev INTEGER NOT NULL DEFAULT 0, country TEXT);
CREATE INDEX IF NOT EXISTS pings_id_ts ON pings (id, ts);
CREATE INDEX IF NOT EXISTS pings_day ON pings (day);
CREATE TABLE IF NOT EXISTS clicks (day TEXT NOT NULL, id TEXT NOT NULL, name TEXT NOT NULL, n INTEGER NOT NULL, dev INTEGER NOT NULL DEFAULT 0);
CREATE INDEX IF NOT EXISTS clicks_day ON clicks (day);
CREATE TABLE IF NOT EXISTS counts (day TEXT NOT NULL, id TEXT NOT NULL, name TEXT NOT NULL, n INTEGER NOT NULL, dev INTEGER NOT NULL DEFAULT 0);
CREATE INDEX IF NOT EXISTS counts_day ON counts (day);
CREATE TABLE IF NOT EXISTS errors (day TEXT NOT NULL, id TEXT NOT NULL, app TEXT NOT NULL, type TEXT NOT NULL, at TEXT NOT NULL, n INTEGER NOT NULL, dev INTEGER NOT NULL DEFAULT 0);
CREATE INDEX IF NOT EXISTS errors_day ON errors (day);
-- Rows written each day (src/index.js DAILY_ROWS): a flood of fresh ids can't use up D1's daily writes
CREATE TABLE IF NOT EXISTS budget (day TEXT PRIMARY KEY, rows INTEGER NOT NULL);
