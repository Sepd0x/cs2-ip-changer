-- Anonymous usage stats (worker/stats). No IP column: the Worker never reads the sender's address, only the country Cloudflare works out.
-- id: random, made on the PC. Since beta.8 it is new every day (one id = one install on one day); before beta.8 it was fixed per install.
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
-- beta.9. Clicks on the app's screens: a 24x16 grid cell, kind c (click), d (dead: nothing there to click) or r (rage: 3+ in 1 s).
-- Added up per day and version, with no install ID, so the map can never be split per install. Made by the Worker if missing.
CREATE TABLE IF NOT EXISTS heat (day TEXT NOT NULL, app TEXT NOT NULL, screen TEXT NOT NULL, kind TEXT NOT NULL, cell TEXT NOT NULL, n INTEGER NOT NULL, dev INTEGER NOT NULL DEFAULT 0, PRIMARY KEY (day, app, screen, kind, cell, dev));
-- beta.9. The live signal: one row per install-day ID, written over every 5 minutes while the app is open. The dashboard only counts it.
CREATE TABLE IF NOT EXISTS live (id TEXT PRIMARY KEY, ts INTEGER NOT NULL, app TEXT NOT NULL, mode TEXT, link TEXT, screen TEXT, cs2 INTEGER, prot INTEGER, tray INTEGER, dev INTEGER NOT NULL DEFAULT 0);
CREATE INDEX IF NOT EXISTS live_ts ON live (ts);
-- beta.9. The environment report (POST /v1/env): no install ID and no country, only counts per day of each value (Windows language,
-- window and scale bands, Windows build, phone brand and Android version, 4G/5G). k = reports counts the reports. Made by the Worker if missing.
CREATE TABLE IF NOT EXISTS env (day TEXT NOT NULL, k TEXT NOT NULL, v TEXT NOT NULL, n INTEGER NOT NULL, dev INTEGER NOT NULL DEFAULT 0, PRIMARY KEY (day, k, v, dev));
