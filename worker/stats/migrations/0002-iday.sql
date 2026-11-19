-- 15 Nov. 2026 (audit F-01): the written days the dashboard's summary reads instead of the reports (src/roll.js). Only CREATE ... IF NOT
-- EXISTS: nothing existing changes, safe to run twice. The Workers make these tables themselves when they find them missing, and work
-- without them (the summary then reads the reports as before: same numbers, more rows read). The dashboard Worker's cron fills iday, the
-- oldest day first, one day every 10 minutes (and five at 03:07 UTC).
--
-- Then, from worker/stats (owner):
--   npx wrangler d1 execute cs2ipc-stats --remote --file migrations/0002-iday.sql
-- To undo: DROP TABLE IF EXISTS iday; DROP TABLE IF EXISTS iday_done;  (Delete my stats and the cleanup also work without them.)
--
-- iday has the install ID the other tables have: Delete my stats removes it too (src/index.js forget, by iday_id), and so does the
-- 12-month cleanup (src/dashboard.js prune).
CREATE TABLE IF NOT EXISTS iday (day TEXT NOT NULL, id TEXT NOT NULL, dev INTEGER NOT NULL DEFAULT 0, ms INTEGER, ta TEXT, tm TEXT, country TEXT, lk INTEGER, os TEXT, r INTEGER NOT NULL DEFAULT 0, kept INTEGER NOT NULL DEFAULT 0, j TEXT NOT NULL, PRIMARY KEY (day, id, dev));
CREATE INDEX IF NOT EXISTS iday_id ON iday (id);
CREATE TABLE IF NOT EXISTS iday_done (day TEXT PRIMARY KEY);
