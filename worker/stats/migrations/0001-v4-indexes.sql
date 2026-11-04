-- v4 dashboard (Nov. 2026): indexes for the reads of api.js summary(). Only CREATE INDEX IF NOT EXISTS: no table or row changes,
-- safe to run twice. The Workers work without them (same answers); with them D1 reads fewer rows per dashboard load.
--
-- Before (owner, from worker/stats), a backup of the production database:
--   npx wrangler d1 export cs2ipc-stats --remote --output ../../publish/backup-d1-AAAA-MM-DD-antes-v4.sql
-- Then:
--   npx wrangler d1 execute cs2ipc-stats --remote --file migrations/0001-v4-indexes.sql
-- To undo: DROP INDEX IF EXISTS counts_day_name; DROP INDEX IF EXISTS counts_install;
--
-- counts by day and name: the per-install read (only the counters needed per install), the accounts read of the sums and the
-- sums themselves find their rows without reading every counter of the day
CREATE INDEX IF NOT EXISTS counts_day_name ON counts (day, name);
-- the installs with a kept ID (id_install), read on every load (installDays, firstSeen): one small index instead of a scan of counts
CREATE INDEX IF NOT EXISTS counts_install ON counts (id) WHERE name = 'id_install';
