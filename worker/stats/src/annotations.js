// Markers on the dashboard's time series and in /api/brief (spec dashboard-v4 5, "Annotations"): videos, posts and incidents,
// by hand, in the repo. The releases come by themselves from GitHub (gh_daily and /api/github), so they are not listed here.
// One entry: { date: 'YYYY-MM-DD' (UTC), kind: 'video' | 'post' | 'incident' | 'release', label: 'short text', url: 'https://…' (optional) }
// Only real events: nothing goes here before it happened.
export const ANNOTATIONS = [
];

const DATE = /^\d{4}-\d{2}-\d{2}$/;
const KINDS = new Set(['release', 'video', 'post', 'incident']);
/** The list checked: a bad entry is left out rather than breaking the page. */
export function annotations(list = ANNOTATIONS) {
  return (Array.isArray(list) ? list : []).filter((a) => a && DATE.test(a.date) && KINDS.has(a.kind) && typeof a.label === 'string' && a.label.length <= 80)
    .map((a) => ({ date: a.date, kind: a.kind, label: a.label, url: typeof a.url === 'string' && /^https:\/\//.test(a.url) ? a.url : null }))
    .sort((a, b) => (a.date < b.date ? -1 : a.date > b.date ? 1 : 0));
}
