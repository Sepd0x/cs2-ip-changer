-- Pseudonymous usage stats (worker/stats). No IP column: the Worker never reads the sender's address, only the country Cloudflare works out.
-- id: random, made on the PC, never from the PC or the user. Kept per install until beta.7, new every day in beta.8, kept per install again
-- since beta.9 (counts id_install marks those). Delete my stats in the app deletes every row under it.
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
-- Rows written each day (src/index.js DAILY_ROWS): a flood of fresh ids can't use up D1's daily writes. Nov. 2026: day:live is the
-- live signal's share of the day (LIVE_ROWS) and day:forget the budget of Delete my stats (FORGET_ROWS). The triggers keep each row
-- under its cap inside the write itself, so requests at the same moment can't pass it together. Same text as src/index.js
-- BUDGET_TRIGGERS; the Worker also makes them when they are missing.
CREATE TABLE IF NOT EXISTS budget (day TEXT PRIMARY KEY, rows INTEGER NOT NULL);
CREATE TRIGGER IF NOT EXISTS budget_cap_insert BEFORE INSERT ON budget WHEN NEW.rows > CASE WHEN NEW.day LIKE '%:live' THEN 12000 WHEN NEW.day LIKE '%:forget' THEN 2000 ELSE 40000 END BEGIN SELECT RAISE(ABORT, 'over budget'); END;
CREATE TRIGGER IF NOT EXISTS budget_cap_update BEFORE UPDATE ON budget WHEN NEW.rows > CASE WHEN NEW.day LIKE '%:live' THEN 12000 WHEN NEW.day LIKE '%:forget' THEN 2000 ELSE 40000 END BEGIN SELECT RAISE(ABORT, 'over budget'); END;
-- beta.9. Clicks on the app's screens: a 24x16 grid cell, kind c (click), d (dead: nothing there to click) or r (rage: 3+ in 1 s).
-- Added up per day and version, with no install ID, so the map can never be split per install. Made by the Worker if missing.
CREATE TABLE IF NOT EXISTS heat (day TEXT NOT NULL, app TEXT NOT NULL, screen TEXT NOT NULL, kind TEXT NOT NULL, cell TEXT NOT NULL, n INTEGER NOT NULL, dev INTEGER NOT NULL DEFAULT 0, PRIMARY KEY (day, app, screen, kind, cell, dev));
-- beta.9. The live signal: one row per install ID, written over every 5 minutes while the app is open. The dashboard only counts it.
CREATE TABLE IF NOT EXISTS live (id TEXT PRIMARY KEY, ts INTEGER NOT NULL, app TEXT NOT NULL, mode TEXT, link TEXT, screen TEXT, cs2 INTEGER, prot INTEGER, tray INTEGER, dev INTEGER NOT NULL DEFAULT 0);
CREATE INDEX IF NOT EXISTS live_ts ON live (ts);
-- beta.9. The environment report (POST /v1/env): no install ID and no country, only counts per day of each value (Windows language,
-- window and scale bands, Windows build, phone brand and Android version, 4G/5G). k = reports counts the reports. Made by the Worker if missing.
CREATE TABLE IF NOT EXISTS env (day TEXT NOT NULL, k TEXT NOT NULL, v TEXT NOT NULL, n INTEGER NOT NULL, dev INTEGER NOT NULL DEFAULT 0, PRIMARY KEY (day, k, v, dev));
-- Nov. 2026. The website's visit counter (POST /v1/site): no ID, no IP, no cookie, only counts per day of page, referrer host,
-- country and event (view or download). Its own share of the day's budget (day:site, SITE_ROWS in src/index.js) with its own
-- triggers, next to the two above. Made by the Worker if missing.
CREATE TABLE IF NOT EXISTS site (day TEXT NOT NULL, page TEXT NOT NULL, ref TEXT NOT NULL, country TEXT NOT NULL, event TEXT NOT NULL, n INTEGER NOT NULL, PRIMARY KEY (day, page, ref, country, event));
CREATE TRIGGER IF NOT EXISTS budget_site_insert BEFORE INSERT ON budget WHEN NEW.day LIKE '%:site' AND NEW.rows > 3000 BEGIN SELECT RAISE(ABORT, 'over budget'); END;
CREATE TRIGGER IF NOT EXISTS budget_site_update BEFORE UPDATE ON budget WHEN NEW.day LIKE '%:site' AND NEW.rows > 3000 BEGIN SELECT RAISE(ABORT, 'over budget'); END;
-- Nov. 2026 (security audit). Reports from installs with nothing before today: at most NEW_ROWS of the day (day:new), so the
-- installs already here keep the rest even through a flood of made-up reports. Made by the Worker if missing.
CREATE TRIGGER IF NOT EXISTS budget_new_insert BEFORE INSERT ON budget WHEN NEW.day LIKE '%:new' AND NEW.rows > 30000 BEGIN SELECT RAISE(ABORT, 'over budget'); END;
CREATE TRIGGER IF NOT EXISTS budget_new_update BEFORE UPDATE ON budget WHEN NEW.day LIKE '%:new' AND NEW.rows > 30000 BEGIN SELECT RAISE(ABORT, 'over budget'); END;
-- Nov. 2026, written by the dashboard Worker's cron (src/dashboard.js scheduled), no ID in either. gh_daily: once a day a picture of
-- GitHub's public numbers (kind repo: stars, forks, watchers, issues; kind asset: tag/file and its download count), so downloads
-- and stars per day are the difference between two days. live_hourly: every 10 minutes the live signal is counted and the
-- highest of each UTC hour kept (dev 0 everyone, dev 1 only the developer's PCs).
CREATE TABLE IF NOT EXISTS gh_daily (day TEXT NOT NULL, kind TEXT NOT NULL, name TEXT NOT NULL, n INTEGER NOT NULL, PRIMARY KEY (day, kind, name));
CREATE TABLE IF NOT EXISTS live_hourly (hour INTEGER NOT NULL, dev INTEGER NOT NULL DEFAULT 0, n INTEGER NOT NULL, game INTEGER NOT NULL, prot INTEGER NOT NULL, PRIMARY KEY (hour, dev));
