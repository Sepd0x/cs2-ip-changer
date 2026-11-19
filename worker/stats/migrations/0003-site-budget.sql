-- 15 Nov. 2026 (audit F-02): the website counter's daily budget from 3,000 to 20,000 (750 visits a day to 5,000: a visit costs 4).
-- The cap is also a trigger inside the database, and CREATE TRIGGER IF NOT EXISTS leaves an existing one as it was, so the two old
-- site triggers are dropped and made again (the Worker's own cap is SITE_ROWS in src/index.js). Safe to run twice.
-- Then, from worker/stats (owner):  npx wrangler d1 execute cs2ipc-stats --remote --file migrations/0003-site-budget.sql
-- Either order with the Worker's deploy is safe: until both are done the counter stops at the lower cap, as before.
-- To undo: run it with 3000 in place of 20000 (and SITE_ROWS = 3000).
DROP TRIGGER IF EXISTS budget_site_insert;
DROP TRIGGER IF EXISTS budget_site_update;
CREATE TRIGGER IF NOT EXISTS budget_site_insert BEFORE INSERT ON budget WHEN NEW.day LIKE '%:site' AND NEW.rows > 20000 BEGIN SELECT RAISE(ABORT, 'over budget'); END;
CREATE TRIGGER IF NOT EXISTS budget_site_update BEFORE UPDATE ON budget WHEN NEW.day LIKE '%:site' AND NEW.rows > 20000 BEGIN SELECT RAISE(ABORT, 'over budget'); END;
